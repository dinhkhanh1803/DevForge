using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Mapping;
using DevForge.Infrastructure.Persistence.Migrations;
using DevForge.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevForge.IntegrationTests.Persistence;

[Collection(ExecutionRecoveryActivityTestGroup.Name)]
public sealed class RunCheckpointStoreTests
{
    private static readonly JsonSerializerOptions _indentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void NonPublishingM7PreviewShapeRemainsCanonicalWithoutM8IdentityFields()
    {
        var preview = CreateCheckpoint("run-m7-preview").Preview!;

        var encoded = CheckpointPreviewCodec.Encode(preview);
        var decoded = CheckpointPreviewCodec.Decode(encoded.Json, encoded.BodyChecksum);

        Assert.DoesNotContain("\"account\"", encoded.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"repository\"", encoded.Json, StringComparison.Ordinal);
        Assert.Null(decoded.Git.GitHubAccount);
        Assert.Null(decoded.Git.GitHubRepository);
        Assert.Equal(preview.PlanHash, decoded.PlanHash);
    }

    [Fact]
    public async Task CompletedPublicationRoundTripsEveryFieldAndRejectsCanonicalTampering()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateCompletedPublicationCheckpoint("run-publication");
        var publication = checkpoint.Publication;
        await store.SaveAsync(checkpoint, CancellationToken.None);

        var loaded = Assert.IsType<RunCheckpoint>(
            await store.FindAsync(checkpoint.Run.Id, CancellationToken.None));
        Assert.Equal(RunStatus.Completed, loaded.Run.Status);
        Assert.Equal(publication.GitState, loaded.Publication.GitState);
        Assert.Equal(publication.GitHubState, loaded.Publication.GitHubState);
        Assert.Equal(publication.ReceiptState, loaded.Publication.ReceiptState);
        Assert.Equal(publication.FinalTreeDigest, loaded.Publication.FinalTreeDigest);
        Assert.Equal(publication.InitialCommitId, loaded.Publication.InitialCommitId);
        Assert.Equal(publication.Branches.ToArray(), loaded.Publication.Branches.ToArray());
        Assert.Equal(publication.RepositoryIdentity, loaded.Publication.RepositoryIdentity);
        Assert.Equal(publication.IsPrivate, loaded.Publication.IsPrivate);
        Assert.Equal(publication.OwnershipNonce, loaded.Publication.OwnershipNonce);
        Assert.Equal(publication.RepositoryUrl, loaded.Publication.RepositoryUrl);
        Assert.Equal(publication.ReceiptPath, loaded.Publication.ReceiptPath);
        Assert.Equal(publication.ReceiptBodyDigest, loaded.Publication.ReceiptBodyDigest);

        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE ProjectRuns SET PublicationJson = replace(PublicationJson, 'sha256:9', 'sha256:8') "
                + "WHERE Id = 'run-publication'");
        }

        await Assert.ThrowsAsync<PersistenceDataException>(() =>
            store.FindAsync(checkpoint.Run.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("checksum")]
    public async Task PublicationColumnNullMismatchIsRejected(string column)
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateCompletedPublicationCheckpoint($"run-null-{column}");
        await store.SaveAsync(checkpoint, CancellationToken.None);

        await using (var context = factory.CreateDbContext())
        {
            var command = column switch
            {
                "json" =>
                    "UPDATE ProjectRuns SET PublicationJson = NULL WHERE Id = {0}",
                "checksum" =>
                    "UPDATE ProjectRuns SET PublicationBodyChecksum = NULL WHERE Id = {0}",
                _ => throw new InvalidOperationException(),
            };
            await context.Database.ExecuteSqlRawAsync(
                command,
                checkpoint.Run.Id);
        }

        await Assert.ThrowsAsync<PersistenceDataException>(() =>
            store.FindAsync(checkpoint.Run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task LegacyCheckpointWithoutPublicationBodyLoadsAsM7NotRequested()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateCheckpoint("run-legacy-publication");
        await store.SaveAsync(checkpoint, CancellationToken.None);

        var loaded = Assert.IsType<RunCheckpoint>(
            await store.FindAsync(checkpoint.Run.Id, CancellationToken.None));

        Assert.Null(loaded.Publication.FinalTreeDigest);
        Assert.Equal(GitPublicationState.NotRequested, loaded.Publication.GitState);
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(
            0L,
            ReadCount(
                connection,
                "SELECT COUNT(*) FROM ProjectRuns WHERE Id = 'run-legacy-publication' "
                + "AND PublicationJson IS NOT NULL"));
    }

    [Fact]
    public async Task StartupRecoveryDurablyClosesInterruptedAttemptAndIsIdempotent()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateInterruptedCheckpoint("run-interrupted");
        await store.SaveAsync(checkpoint, CancellationToken.None);
        var service = new RunRecoveryService(
            store,
            new UnusedOrchestrator(),
            new UnusedStagingManager(),
            new TestTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(2)));

        var first = await service.RecoverInterruptedAsync(CancellationToken.None);
        var persisted = await store.FindAsync(checkpoint.Run.Id, CancellationToken.None);
        var second = await service.RecoverInterruptedAsync(CancellationToken.None);

        Assert.True(first.IsSuccessful);
        Assert.NotNull(persisted);
        Assert.Equal(RunStatus.Executing, persisted.Run.Status);
        Assert.Null(persisted.Run.CurrentStepId);
        var attempt = Assert.Single(persisted.Run.Attempts);
        Assert.Equal(StepAttemptOutcome.Failed, attempt.Outcome);
        Assert.Equal("DF-EXEC-003", attempt.Error?.Code);
        Assert.True(attempt.Error?.IsRetryable);
        Assert.True(second.IsSuccessful);
        var rescanned = Assert.Single(second.Value.Checkpoints);
        Assert.Equal(persisted.Run.Id, rescanned.Run.Id);
        Assert.Equal(persisted.Run.Status, rescanned.Run.Status);
        var rescannedAttempt = Assert.Single(rescanned.Run.Attempts);
        Assert.Equal(attempt.AttemptNumber, rescannedAttempt.AttemptNumber);
        Assert.Equal(attempt.Outcome, rescannedAttempt.Outcome);
        Assert.Equal(attempt.CompletedAt, rescannedAttempt.CompletedAt);
        Assert.Equal(attempt.Error?.Code, rescannedAttempt.Error?.Code);
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM ProjectRuns;"));
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM RunSteps;"));
    }

    [Fact]
    public async Task RoundTripsCompleteCheckpointAndAttemptDigest()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateCheckpoint("run-roundtrip");

        await store.SaveAsync(checkpoint, CancellationToken.None);

        var loaded = await store.FindAsync(checkpoint.Run.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(checkpoint.Run.Id, loaded.Run.Id);
        Assert.Equal(checkpoint.Run.Status, loaded.Run.Status);
        Assert.Equal(checkpoint.PlanHash, loaded.PlanHash);
        Assert.Equal(checkpoint.Blueprint, loaded.Blueprint);
        Assert.Equal(checkpoint.BlueprintFingerprint, loaded.BlueprintFingerprint);
        Assert.Equal(checkpoint.Staging.MarkerId, loaded.Staging.MarkerId);
        Assert.Equal(checkpoint.Target.ParentRoot, loaded.Target.ParentRoot);
        Assert.Equal(checkpoint.Target.TargetDirectory, loaded.Target.TargetDirectory);
        Assert.Equal(checkpoint.RunArtifacts.Root, loaded.RunArtifacts.Root);
        Assert.Equal(checkpoint.FinalizationState, loaded.FinalizationState);
        Assert.Equal(checkpoint.ReportState, loaded.ReportState);
        Assert.Equal(checkpoint.Evidence.ToArray(), loaded.Evidence.ToArray());
        Assert.NotNull(loaded.Preview);
        Assert.Equal(checkpoint.Preview!.PlanHash, loaded.Preview.PlanHash);
        Assert.Equal("vscode", loaded.Preview.Completion.IdeId);

        var step = Assert.Single(loaded.Plan.Steps);
        Assert.Equal("create-directory", step.Handler);
        Assert.Equal(RetryMode.Manual, step.RetryPolicy.Mode);
        Assert.Equal("src", step.Inputs["path"].StringValue);
        Assert.Equal(checkpoint.Plan.Steps[0].Inputs["metadata"], step.Inputs["metadata"]);
        Assert.Equal("Sample App", loaded.Plan.TemplateContext["project.name"]);
        Assert.Equal("net10.0", loaded.Plan.TemplateContext["recipe.input.framework"]);
        Assert.Equal($"sha256:{new string('a', 64)}", Assert.Single(loaded.Run.Attempts).OutputDigest);
    }

    [Fact]
    public async Task RepeatedSaveAtomicallyReplacesWholeCheckpoint()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var clock = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new SqliteRunCheckpointStore(factory, clock);
        await store.SaveAsync(CreateCheckpoint("run-replace"), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        var replacement = CreateCheckpoint(
            "run-replace",
            digestCharacter: 'b',
            FinalizationState.Succeeded,
            ReportPersistenceState.Failed);

        await store.SaveAsync(replacement, CancellationToken.None);

        var loaded = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(FinalizationState.Succeeded, loaded.FinalizationState);
        Assert.Equal(ReportPersistenceState.Failed, loaded.ReportState);
        Assert.Equal($"sha256:{new string('b', 64)}", Assert.Single(loaded.Evidence).OutputDigest);
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM ProjectRuns;"));
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM RunSteps;"));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotCreatePartialCheckpoint()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(CreateCheckpoint("run-cancel"), cancellation.Token));

        Assert.Empty(await store.ListAsync(CancellationToken.None));
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(0L, ReadCount(connection, "SELECT COUNT(*) FROM RunSteps;"));
    }

    [Fact]
    public async Task CorruptedPlanBodyFailsClosedWithoutEchoingStoredContent()
    {
        const string secretShapedContent = "Bearer abcdefghijk";
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        await store.SaveAsync(CreateCheckpoint("run-corrupt"), CancellationToken.None);
        await using (var context = factory.CreateDbContext())
        {
            const string runId = "run-corrupt";
            var corruptJson = "{\"id\":\"sha256:"
                + new string('1', 64)
                + "\",\"steps\":[],\"validators\":[],\"unexpected\":\""
                + secretShapedContent
                + "\"}";
            var corruptChecksum = $"sha256:{Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(corruptJson)))}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ProjectRuns SET PlanJson = {corruptJson}, PlanBodyChecksum = {corruptChecksum} WHERE Id = {runId}");
        }

        var exception = await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.FindAsync("run-corrupt", CancellationToken.None));

        Assert.Equal("DF-DB-001", exception.Code);
        Assert.DoesNotContain(secretShapedContent, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalPreviewContentTamperingFailsChecksumValidation()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        await store.SaveAsync(CreateCheckpoint("run-preview-tamper"), CancellationToken.None);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE ProjectRuns SET PlanPreviewJson = replace(PlanPreviewJson, "
                + "'Safe warning.', 'Changed warning.') WHERE Id = 'run-preview-tamper';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.FindAsync("run-preview-tamper", CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentWholeCheckpointWritesRemainAtomic()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var stores = Enumerable.Range(0, 16)
            .Select(_ => new SqliteRunCheckpointStore(factory))
            .ToArray();
        var writes = stores.Select((store, index) => store.SaveAsync(
            CreateCheckpoint("run-concurrent", "abcdef"[index % 6]),
            CancellationToken.None));

        await Task.WhenAll(writes);

        var loaded = Assert.Single(await stores[0].ListAsync(CancellationToken.None));
        Assert.Equal(
            Assert.Single(loaded.Run.Attempts).OutputDigest,
            Assert.Single(loaded.Evidence).OutputDigest);
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM ProjectRuns;"));
        Assert.Equal(1L, ReadCount(connection, "SELECT COUNT(*) FROM RunSteps;"));
    }

    [Fact]
    public async Task SemanticallyValidNonCanonicalPlanBodyFailsClosed()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        await store.SaveAsync(CreateCheckpoint("run-noncanonical"), CancellationToken.None);
        using var connection = database.OpenConnection();
        var canonicalJson = ReadString(
            connection,
            "SELECT PlanJson FROM ProjectRuns WHERE Id = 'run-noncanonical';");
        using var document = JsonDocument.Parse(canonicalJson);
        var nonCanonicalJson = JsonSerializer.Serialize(
            document.RootElement,
            _indentedJsonOptions);
        var checksum = $"sha256:{Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(nonCanonicalJson)))}";
        using (var update = connection.CreateCommand())
        {
            update.CommandText =
                "UPDATE ProjectRuns SET PlanJson = $json, PlanBodyChecksum = $checksum "
                + "WHERE Id = 'run-noncanonical';";
            update.Parameters.AddWithValue("$json", nonCanonicalJson);
            update.Parameters.AddWithValue("$checksum", checksum);
            Assert.Equal(1, update.ExecuteNonQuery());
        }

        await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.FindAsync("run-noncanonical", CancellationToken.None));
    }

    [Fact]
    public async Task EquivalentInputInsertionOrdersPersistIdenticalCanonicalPlanBodies()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        await store.SaveAsync(
            CreateCheckpoint("run-order-a", reverseInputs: false),
            CancellationToken.None);
        await store.SaveAsync(
            CreateCheckpoint("run-order-b", reverseInputs: true),
            CancellationToken.None);

        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT PlanJson, PlanBodyChecksum FROM ProjectRuns ORDER BY Id;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var firstJson = reader.GetString(0);
        var firstChecksum = reader.GetString(1);
        Assert.True(reader.Read());
        Assert.Equal(firstJson, reader.GetString(0));
        Assert.Equal(firstChecksum, reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task ExactLegacyFourPropertyEvidenceLoadsAndSavesWithoutShapeUpgrade()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        const string runId = "run-legacy-evidence";
        await store.SaveAsync(CreateCheckpoint(runId), CancellationToken.None);
        var legacyJson = "[{\"kind\":\"Step\",\"id\":\"create\",\"status\":\"Passed\","
            + $"\"outputDigest\":\"sha256:{new string('a', 64)}\"}}]";
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ProjectRuns SET EvidenceJson = {legacyJson} WHERE Id = {runId}");
        }

        var loaded = Assert.IsType<RunCheckpoint>(
            await store.FindAsync(runId, CancellationToken.None));
        var evidence = Assert.Single(loaded.Evidence);
        Assert.Null(evidence.StartedAt);
        Assert.Null(evidence.CompletedAt);

        await store.SaveAsync(loaded, CancellationToken.None);
        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        Assert.Equal(
            legacyJson,
            ReadString(connection, "SELECT EvidenceJson FROM ProjectRuns WHERE Id = 'run-legacy-evidence';"));
    }

    [Theory]
    [InlineData("[{\"kind\":\"Step\",\"id\":\"create\",\"status\":\"Passed\",\"outputDigest\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startedAt\":\"1970-01-01T00:00:00+00:00\"}]")]
    [InlineData("[{\"kind\":\"Step\",\"id\":\"create\",\"status\":\"Passed\",\"outputDigest\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startedAt\":null,\"completedAt\":null,\"errorCode\":null,\"errorSummary\":null}]")]
    public async Task PartiallyUpgradedEvidenceShapesFailClosed(string evidenceJson)
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        const string runId = "run-partial-evidence";
        await store.SaveAsync(CreateCheckpoint(runId), CancellationToken.None);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ProjectRuns SET EvidenceJson = {evidenceJson} WHERE Id = {runId}");
        }

        await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.FindAsync(runId, CancellationToken.None));
    }

    [Fact]
    public async Task LegacyJournalCannotDowngradeACompleteCheckpoint()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var checkpointStore = new SqliteRunCheckpointStore(factory);
        var checkpoint = CreateCheckpoint("run-no-downgrade");
        await checkpointStore.SaveAsync(checkpoint, CancellationToken.None);
        var journal = new SqliteRunJournalStore(factory);

        await Assert.ThrowsAsync<PersistenceDataException>(
            () => journal.SaveAsync(checkpoint.Run, CancellationToken.None));

        Assert.NotNull(await checkpointStore.FindAsync(checkpoint.Run.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PreservesChronologicalAttemptOrderAcrossStepIdentifiers()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunCheckpointStore(factory);
        var original = CreateCheckpoint("run-order-history");
        var digest = $"sha256:{new string('c', 64)}";
        var alphaStep = ExecutionStep.Create(
            "alpha",
            "Alpha",
            "create-directory",
            [],
            TimeSpan.FromSeconds(10),
            RetryPolicy.None).Value;
        var plan = ExecutionPlan.Create(
            original.PlanHash,
            [.. original.Plan.Steps, alphaStep],
            original.Plan.Validators).Value;
        var run = original.Run
            .StartAttempt("alpha", DateTimeOffset.UnixEpoch.AddSeconds(2)).Value
            .CompleteAttempt(
                "alpha",
                1,
                StepAttemptOutcome.Succeeded,
                DateTimeOffset.UnixEpoch.AddSeconds(3),
                null,
                null,
                digest).Value;
        var alphaEvidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Step,
            "alpha",
            ExecutionEvidenceStatus.Passed,
            digest,
            DateTimeOffset.UnixEpoch.AddSeconds(2),
            DateTimeOffset.UnixEpoch.AddSeconds(3),
            null,
            null).Value;
        var checkpoint = RunCheckpoint.Create(
            run,
            plan,
            original.Blueprint,
            original.BlueprintFingerprint,
            original.Staging,
            original.Target,
            original.RunArtifacts,
            [.. original.Evidence, alphaEvidence],
            original.FinalizationState,
            original.ReportState).Value;

        await store.SaveAsync(checkpoint, CancellationToken.None);

        var loaded = await store.FindAsync(checkpoint.Run.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(["create", "alpha"], loaded.Run.Attempts.Select(attempt => attempt.StepId));
    }

    [Fact]
    public async Task LatestMigrationPreservesLegacyRunAndAddsCheckpointColumns()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = new DevForgeDbContextFactory(database.Location);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                PersistenceMigrationNames.RetentionAndLookupIndexes,
                CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO ProjectRuns "
                + "(Id, RecipeId, Status, CreatedAtUnixMs, UpdatedAtUnixMs, ErrorsJson) "
                + "VALUES ('legacy-run', 'legacy-recipe', 'Draft', 0, 0, '[]')");
            await migrator.MigrateAsync(cancellationToken: CancellationToken.None);
        }

        var journal = new SqliteRunJournalStore(factory);
        Assert.Equal("legacy-run", Assert.Single(await journal.ListAsync(CancellationToken.None)).Id);
        var checkpoints = new SqliteRunCheckpointStore(factory);
        Assert.Empty(await checkpoints.ListAsync(CancellationToken.None));

        using var connection = database.OpenConnection(SqliteOpenMode.ReadOnly);
        var projectRunColumns = ReadColumnNames(connection, "ProjectRuns");
        var runStepColumns = ReadColumnNames(connection, "RunSteps");
        Assert.Contains("PlanHash", projectRunColumns);
        Assert.Contains("PlanJson", projectRunColumns);
        Assert.Contains("PlanBodyChecksum", projectRunColumns);
        Assert.Contains("BlueprintChecksum", projectRunColumns);
        Assert.Contains("OwnershipMarkerId", projectRunColumns);
        Assert.Contains("FinalizationState", projectRunColumns);
        Assert.Contains("ReportState", projectRunColumns);
        Assert.Contains("PublicationJson", projectRunColumns);
        Assert.Contains("PublicationBodyChecksum", projectRunColumns);
        Assert.Contains("OutputDigest", runStepColumns);
        Assert.Contains("SequenceNumber", runStepColumns);
    }

    private static RunCheckpoint CreateCheckpoint(
        string runId,
        char digestCharacter = 'a',
        FinalizationState finalizationState = FinalizationState.NotStarted,
        ReportPersistenceState reportState = ReportPersistenceState.NotStarted,
        bool reverseInputs = false)
    {
        var digest = $"sha256:{new string(digestCharacter, 64)}";
        var pathInput = KeyValuePair.Create<string, PlanValue?>(
            "path",
            PlanValue.FromString("src").Value);
        var cleanInput = KeyValuePair.Create<string, PlanValue?>(
            "clean",
            PlanValue.FromBoolean(false));
        var labels = PlanValue.FromArray(
        [
            PlanValue.FromString("desktop").Value,
            PlanValue.FromInteger(10),
        ]).Value;
        var metadata = PlanValue.FromObject(reverseInputs
            ?
            [
                KeyValuePair.Create<string, PlanValue?>("labels", labels),
                KeyValuePair.Create<string, PlanValue?>("enabled", PlanValue.FromBoolean(true)),
            ]
            :
            [
                KeyValuePair.Create<string, PlanValue?>("enabled", PlanValue.FromBoolean(true)),
                KeyValuePair.Create<string, PlanValue?>("labels", labels),
            ]).Value;
        var metadataInput = KeyValuePair.Create<string, PlanValue?>("metadata", metadata);
        var inputs = reverseInputs
            ? new[] { metadataInput, cleanInput, pathInput }
            : [pathInput, cleanInput, metadataInput];
        var step = ExecutionStep.Create(
            "create",
            "Create directory",
            "create-directory",
            inputs,
            TimeSpan.FromSeconds(30),
            RetryPolicy.Manual(3).Value).Value;
        var validator = ExecutionValidator.Create(
            "validate",
            "validate-command",
            [KeyValuePair.Create<string, PlanValue?>("required", PlanValue.FromBoolean(true))],
            TimeSpan.FromMinutes(1),
            required: true).Value;
        var planHash = $"sha256:{new string('1', 64)}";
        var plan = ExecutionPlan.Create(
            planHash,
            [step],
            [validator],
            [
                KeyValuePair.Create<string, string?>("project.name", "Sample App"),
                KeyValuePair.Create<string, string?>("recipe.input.framework", "net10.0"),
            ]).Value;
        var run = ProjectRun.Create(runId, "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .StartAttempt("create", DateTimeOffset.UnixEpoch).Value
            .CompleteAttempt(
                "create",
                1,
                StepAttemptOutcome.Succeeded,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                null,
                null,
                digest).Value;
        var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            WorkspaceRelativePath.Create("desktop.csharp-wpf-tool\\1.0.0").Value,
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        var preview = PlanPreview.Create(
            blueprint,
            [new PlanPreviewStep(step.Id, step.Handler, step.Timeout)],
            [new PlanPreviewValidator(
                validator.Id, validator.Handler, validator.Timeout, validator.Required)],
            [], [], [], [],
            [new ValidationIssue("preview.warning", "Safe warning.", "preview")],
            [KeyValuePair.Create<string, PlanValue?>("framework", PlanValue.FromString("net10.0").Value)],
            ["tests"],
            GitOptions.Create(initializeRepository: false).Value,
            CompletionOptions.Create(openIde: true, ideId: "vscode").Value,
            planHash).Value;
        var staging = StagingDescriptor.Create(
            WorkspaceRelativePath.Create($".devforge-staging\\{runId}").Value,
            WorkspaceRelativePath.Create($".devforge-staging\\{runId}\\payload").Value,
            WorkspaceRelativePath.Create($".devforge-staging\\{runId}\\ownership.json").Value,
            $"marker-{runId}").Value;
        var target = TargetDescriptor.Create(
            WorkspaceRoot.Create("C:\\target-parent").Value,
            WorkspaceRelativePath.Create("project").Value,
            null).Value;
        var artifacts = RunArtifactDescriptor.Create(
            WorkspaceRoot.Create("C:\\run-artifacts").Value).Value;
        var evidence = ExecutionEvidence.Create(
            ExecutionEvidenceKind.Step,
            "create",
            ExecutionEvidenceStatus.Passed,
            digest,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            null,
            null).Value;
        return RunCheckpoint.Create(
            run,
            plan,
            preview,
            blueprint,
            fingerprint,
            staging,
            target,
            artifacts,
            [evidence],
            finalizationState,
            reportState).Value;
    }

    private static RunCheckpoint CreateInterruptedCheckpoint(string runId)
    {
        var checkpoint = CreateCheckpoint(runId);
        var run = ProjectRun.Create(runId, checkpoint.Run.RecipeId).Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .StartAttempt(checkpoint.Plan.Steps[0].Id, DateTimeOffset.UnixEpoch).Value;
        return RunCheckpoint.Create(
            run,
            checkpoint.Plan,
            checkpoint.Preview,
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            [],
            checkpoint.FinalizationState,
            checkpoint.ReportState).Value;
    }

    private static RunCheckpoint CreateCompletedPublicationCheckpoint(string runId)
    {
        var original = CreateCheckpoint(runId);
        var git = GitOptions.Create(
            initializeRepository: true,
            useDevelopBranch: true,
            publishToGitHub: true,
            isPrivate: true,
            githubAccount: "octocat",
            githubRepository: "devforge").Value;
        var preview = PlanPreview.Create(
            original.Preview!.Blueprint,
            original.Preview.Steps,
            original.Preview.Validators,
            original.Preview.RequiredTools,
            original.Preview.ToolStatuses,
            original.Preview.Dependencies,
            original.Preview.Artifacts,
            original.Preview.Warnings,
            original.Preview.EffectiveInputs.Select(pair =>
                KeyValuePair.Create<string, PlanValue?>(pair.Key, pair.Value)),
            original.Preview.EnabledFeatures,
            git,
            original.Preview.Completion,
            original.Preview.PlanHash).Value;
        var run = original.Run
            .TransitionTo(RunStatus.LocalReady).Value
            .TransitionTo(RunStatus.PublishPending).Value
            .TransitionTo(RunStatus.Completed).Value;
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var publication = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.Succeeded,
            PublicationReceiptState.Succeeded,
            $"sha256:{new string('9', 64)}",
            new string('a', 40),
            ["main", "develop"],
            identity,
            isPrivate: true,
            ownershipNonce: new string('b', 32),
            repositoryUrl: identity.HttpsWebUrl,
            WorkspaceRelativePath.Create($"reports\\{runId}.publication.json").Value,
            $"sha256:{new string('c', 64)}").Value;

        return RunCheckpoint.Create(
            run,
            original.Plan,
            preview,
            original.Blueprint,
            original.BlueprintFingerprint,
            original.Staging,
            original.Target,
            original.RunArtifacts,
            original.Evidence,
            FinalizationState.Succeeded,
            ReportPersistenceState.Succeeded,
            publication).Value;
    }

    private static async Task<DevForgeDbContextFactory> CreateMigratedFactoryAsync(
        PersistenceTestDatabase database)
    {
        var factory = new DevForgeDbContextFactory(database.Location);
        await using var context = factory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return factory;
    }

    private static long ReadCount(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (long)command.ExecuteScalar()!;
    }

    private static string ReadString(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (string)command.ExecuteScalar()!;
    }

    private static string[] ReadColumnNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return [.. names];
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class UnusedOrchestrator : IExecutionOrchestrator
    {
        public Task<RunCheckpoint> ExecuteAsync(
            ExecutionRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedStagingManager : IStagingWorkspaceManager
    {
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> CreateAsync(ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> ValidateOwnershipAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<IStagingWorkspaceLease>> RecreateForReplayAsync(RunCheckpoint checkpoint, ExecutionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionOperationResult<StagingCleanupReceipt>> CleanupFinalizedAsync(RunCheckpoint checkpoint, IWorkspaceFileSystem targetParentWorkspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
