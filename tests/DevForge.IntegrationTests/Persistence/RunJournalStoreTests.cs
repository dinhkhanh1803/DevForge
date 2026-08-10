using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DevForge.IntegrationTests.Persistence;

public sealed class RunJournalStoreTests
{
    [Fact]
    public async Task RoundTripsEmptyAndCompletedRunSnapshots()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunJournalStore(factory);
        var draft = ProjectRun.Create("run-empty", "recipe.empty").Value;
        var completed = CreateCompletedRun("run-completed");

        await store.SaveAsync(draft, CancellationToken.None);
        await store.SaveAsync(completed, CancellationToken.None);

        var loaded = await store.ListAsync(CancellationToken.None);
        Assert.Equal(2, loaded.Length);
        var loadedDraft = Assert.Single(loaded, run => run.Id == draft.Id);
        var loadedCompleted = Assert.Single(loaded, run => run.Id == completed.Id);
        Assert.Equal(RunStatus.Draft, loadedDraft.Status);
        Assert.Empty(loadedDraft.Attempts);
        Assert.Equal(RunStatus.Completed, loadedCompleted.Status);
        var attempt = Assert.Single(loadedCompleted.Attempts);
        Assert.Equal("generate", attempt.StepId);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(StepAttemptOutcome.Succeeded, attempt.Outcome);
    }

    [Fact]
    public async Task RoundTripsFailedAttemptAndRedactedError()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunJournalStore(factory);
        var error = CreateError();
        var run = ProjectRun.Create("run-failed", "recipe.failed").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .StartAttempt("restore", DateTimeOffset.UnixEpoch).Value
            .CompleteAttempt(
                "restore",
                1,
                StepAttemptOutcome.Failed,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                1,
                error).Value
            .AppendError(error).Value
            .TransitionTo(RunStatus.Failed).Value;

        await store.SaveAsync(run, CancellationToken.None);

        var loaded = Assert.Single(await store.ListAsync(CancellationToken.None));
        var loadedAttempt = Assert.Single(loaded.Attempts);
        Assert.Equal(error.Code, loadedAttempt.Error?.Code);
        Assert.Null(loadedAttempt.Error?.StepId);
        Assert.Equal(error.TechnicalDetail, loadedAttempt.Error?.TechnicalDetail);
        Assert.Equal(error.RedactedContext, loadedAttempt.Error?.RedactedContext);
        var loadedRunError = Assert.Single(loaded.Errors);
        Assert.Equal(error.Code, loadedRunError.Code);
        Assert.Equal(error.Summary, loadedRunError.Summary);
        Assert.Equal(error.TechnicalDetail, loadedRunError.TechnicalDetail);
        Assert.Equal(error.SuggestedActions.ToArray(), loadedRunError.SuggestedActions.ToArray());
    }

    [Fact]
    public async Task RepeatedSaveAtomicallyReplacesImmutableSnapshot()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var clock = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new SqliteRunJournalStore(factory, clock);
        var draft = ProjectRun.Create("run-replace", "recipe.replace").Value;
        await store.SaveAsync(draft, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));

        var replacement = CreateCompletedRun("run-replace", "recipe.replace");
        await store.SaveAsync(replacement, CancellationToken.None);

        var loaded = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(RunStatus.Completed, loaded.Status);
        Assert.Single(loaded.Attempts);
    }

    [Fact]
    public async Task ListsByUpdateTimeDescendingThenIdentifier()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var clock = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new SqliteRunJournalStore(factory, clock);
        await store.SaveAsync(ProjectRun.Create("run-b", "recipe").Value, CancellationToken.None);
        await store.SaveAsync(ProjectRun.Create("run-a", "recipe").Value, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await store.SaveAsync(ProjectRun.Create("run-c", "recipe").Value, CancellationToken.None);

        var loaded = await store.ListAsync(CancellationToken.None);

        Assert.Equal(["run-c", "run-a", "run-b"], loaded.Select(run => run.Id));
    }

    [Fact]
    public async Task PreCancelledSaveDoesNotMutateDatabase()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        var store = new SqliteRunJournalStore(factory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(ProjectRun.Create("run-cancel", "recipe").Value, cancellation.Token));

        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("InvalidStatus")]
    public async Task InvalidStoredRunStatusFailsClosed(string storedStatus)
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        await InsertRunAsync(factory, "run-invalid", storedStatus);
        var store = new SqliteRunJournalStore(factory);

        var exception = await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.ListAsync(CancellationToken.None));

        Assert.Equal("DF-DB-001", exception.Code);
        Assert.DoesNotContain(storedStatus, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalizedDuplicateStoredAttemptsFailClosed()
    {
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        await InsertRunAsync(factory, "run-duplicate", "Completed");
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO RunSteps "
                + "(RunId, StepId, AttemptNumber, Outcome, StartedAtUnixMs, CompletedAtUnixMs) VALUES "
                + "('run-duplicate', ' build ', 1, 'Succeeded', 0, 1), "
                + "('run-duplicate', 'build', 1, 'Succeeded', 0, 1)");
        }

        var store = new SqliteRunJournalStore(factory);
        await Assert.ThrowsAsync<PersistenceDataException>(() => store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SecretShapedStoredDiagnosticFailsClosedWithoutEchoingValue()
    {
        const string secretShapedValue = "Bearer abcdefghijk";
        await using var database = PersistenceTestDatabase.Create();
        var factory = await CreateMigratedFactoryAsync(database);
        await InsertRunAsync(factory, "run-secret", "Failed");
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO RunSteps "
                + "(RunId, StepId, AttemptNumber, Outcome, StartedAtUnixMs, CompletedAtUnixMs, ExitCode, "
                + "ErrorCode, ErrorSummary, ErrorTechnicalDetail, ErrorPhase, ErrorIsRetryable, "
                + "ErrorSuggestedActionsJson, ErrorContextJson) VALUES "
                + "('run-secret', 'restore', 1, 'Failed', 0, 1, 1, 'DF-TEST-001', "
                + "'Restore failed.', 'Bearer abcdefghijk', 'restore', 0, '[]', '{{}}')");
        }

        var store = new SqliteRunJournalStore(factory);
        var exception = await Assert.ThrowsAsync<PersistenceDataException>(
            () => store.ListAsync(CancellationToken.None));

        Assert.DoesNotContain(secretShapedValue, exception.Message, StringComparison.Ordinal);
    }

    private static ProjectRun CreateCompletedRun(string id, string recipeId = "recipe.completed")
    {
        return ProjectRun.Create(id, recipeId).Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value
            .StartAttempt("generate", DateTimeOffset.UnixEpoch).Value
            .CompleteAttempt(
                "generate",
                1,
                StepAttemptOutcome.Succeeded,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                0,
                null).Value
            .TransitionTo(RunStatus.LocalReady).Value
            .TransitionTo(RunStatus.Completed).Value;
    }

    private static DevForgeError CreateError()
    {
        return DevForgeError.Create(
            "DF-TEST-001",
            "Restore failed.",
            RedactedText.FromTrustedRedaction("The restore tool returned exit code 1.").Value,
            "restore",
            null,
            false,
            ["Review the generated report."],
            [KeyValuePair.Create("workspace", RedactedText.FromTrustedRedaction("[REDACTED]").Value)]).Value;
    }

    private static async Task InsertRunAsync(
        DevForgeDbContextFactory factory,
        string id,
        string status)
    {
        long? completedAt = status is "Completed" or "Failed" or "Cancelled"
            or "PreflightFailed" or "ValidationFailed"
            ? 0
            : null;
        await using var context = factory.CreateDbContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ProjectRuns
            (Id, RecipeId, Status, CreatedAtUnixMs, UpdatedAtUnixMs, CompletedAtUnixMs, ErrorsJson)
            VALUES ({id}, 'recipe', {status}, 0, 0, {completedAt}, '[]')
            """);
    }

    private static async Task<DevForgeDbContextFactory> CreateMigratedFactoryAsync(
        PersistenceTestDatabase database)
    {
        var factory = new DevForgeDbContextFactory(database.Location);
        await using var context = factory.CreateDbContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return factory;
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
