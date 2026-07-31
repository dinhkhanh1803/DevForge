using System.Collections.Immutable;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Validation;

namespace DevForge.Domain.Runs;

public enum RunStatus
{
    Draft,
    Planning,
    PreflightFailed,
    Executing,
    ValidationFailed,
    LocalReady,
    PublishPending,
    Completed,
    Cancelled,
    Failed,
}

public enum StepAttemptOutcome
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public sealed record StepAttempt(
    string StepId,
    int AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    StepAttemptOutcome Outcome,
    int? ExitCode,
    DevForgeError? Error);

public sealed class ProjectRun
{
    private static readonly Dictionary<RunStatus, ImmutableHashSet<RunStatus>> _allowedTransitions =
        new Dictionary<RunStatus, ImmutableHashSet<RunStatus>>
        {
            [RunStatus.Draft] = [RunStatus.Planning, RunStatus.Cancelled, RunStatus.Failed],
            [RunStatus.Planning] =
            [
                RunStatus.PreflightFailed,
                RunStatus.Executing,
                RunStatus.Cancelled,
                RunStatus.Failed,
            ],
            [RunStatus.PreflightFailed] = [],
            [RunStatus.Executing] =
            [
                RunStatus.ValidationFailed,
                RunStatus.LocalReady,
                RunStatus.Cancelled,
                RunStatus.Failed,
            ],
            [RunStatus.ValidationFailed] = [],
            [RunStatus.LocalReady] =
            [
                RunStatus.PublishPending,
                RunStatus.Completed,
                RunStatus.Cancelled,
                RunStatus.Failed,
            ],
            [RunStatus.PublishPending] = [RunStatus.Completed, RunStatus.Cancelled, RunStatus.Failed],
            [RunStatus.Completed] = [],
            [RunStatus.Cancelled] = [],
            [RunStatus.Failed] = [],
        };

    private ProjectRun(
        string id,
        string recipeId,
        RunStatus status,
        IEnumerable<StepAttempt> attempts,
        IEnumerable<DevForgeError> errors)
    {
        Id = id;
        RecipeId = recipeId;
        Status = status;
        Attempts = [.. attempts];
        Errors = [.. errors];
    }

    public string Id { get; }

    public string RecipeId { get; }

    public RunStatus Status { get; }

    public ImmutableArray<StepAttempt> Attempts { get; }

    public ImmutableArray<DevForgeError> Errors { get; }

    public static ProjectRun Create(
        string id,
        string recipeId,
        IEnumerable<StepAttempt>? attempts = null,
        IEnumerable<DevForgeError>? errors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);

        return new ProjectRun(
            id.Trim(),
            recipeId.Trim(),
            RunStatus.Draft,
            attempts ?? [],
            errors ?? []);
    }

    public ValidationResult<ProjectRun> TransitionTo(RunStatus status)
    {
        if (!_allowedTransitions[Status].Contains(status))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.transition.invalid",
                    $"A run cannot transition from {Status} to {status}.",
                    "status"),
            ]);
        }

        return ValidationResult.Success(
            new ProjectRun(Id, RecipeId, status, Attempts, Errors));
    }
}
