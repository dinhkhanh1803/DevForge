using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Application.Execution;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Persistence;
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
            digest).Value;
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
            digest).Value;
        return RunCheckpoint.Create(
            run,
            plan,
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
            checkpoint.Blueprint,
            checkpoint.BlueprintFingerprint,
            checkpoint.Staging,
            checkpoint.Target,
            checkpoint.RunArtifacts,
            [],
            checkpoint.FinalizationState,
            checkpoint.ReportState).Value;
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
