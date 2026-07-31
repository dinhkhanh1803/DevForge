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

public sealed class StepAttempt
{
    private StepAttempt(
        string stepId,
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        StepAttemptOutcome outcome,
        int? exitCode,
        DevForgeError? error)
    {
        StepId = stepId;
        AttemptNumber = attemptNumber;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Outcome = outcome;
        ExitCode = exitCode;
        Error = error;
    }

    public string StepId { get; }

    public int AttemptNumber { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public StepAttemptOutcome Outcome { get; }

    public int? ExitCode { get; }

    public DevForgeError? Error { get; }

    public static ValidationResult<StepAttempt> Start(
        string? stepId,
        int attemptNumber,
        DateTimeOffset startedAt)
    {
        return Rehydrate(
            stepId,
            attemptNumber,
            startedAt,
            null,
            StepAttemptOutcome.Running,
            null,
            null);
    }

    public static ValidationResult<StepAttempt> Rehydrate(
        string? stepId,
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        StepAttemptOutcome outcome,
        int? exitCode,
        DevForgeError? error)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(stepId))
        {
            issues.Add(new ValidationIssue("attempt.step-id.required", "A step identifier is required.", "stepId"));
        }

        if (attemptNumber < 1)
        {
            issues.Add(new ValidationIssue("attempt.number.invalid", "Attempt number must be at least one.", "attemptNumber"));
        }

        if (completedAt < startedAt)
        {
            issues.Add(
                new ValidationIssue(
                    "attempt.completed-at.invalid",
                    "Attempt completion cannot precede its start.",
                    "completedAt"));
        }

        if (!Enum.IsDefined(outcome))
        {
            issues.Add(new ValidationIssue("attempt.outcome.invalid", "The attempt outcome is not defined.", "outcome"));
        }
        else
        {
            switch (outcome)
            {
                case StepAttemptOutcome.Running when completedAt is not null || exitCode is not null || error is not null:
                    issues.Add(
                        new ValidationIssue(
                            "attempt.running.inconsistent",
                            "A running attempt cannot have completion data.",
                            "outcome"));
                    break;
                case StepAttemptOutcome.Succeeded when completedAt is null || exitCode is null || error is not null:
                    issues.Add(
                        new ValidationIssue(
                            "attempt.succeeded.inconsistent",
                            "A succeeded attempt requires completion and exit code without an error.",
                            "outcome"));
                    break;
                case StepAttemptOutcome.Failed:
                    if (completedAt is null)
                    {
                        issues.Add(
                            new ValidationIssue(
                                "attempt.failed.inconsistent",
                                "A failed attempt requires a completion timestamp.",
                                "completedAt"));
                    }

                    if (error is null)
                    {
                        issues.Add(
                            new ValidationIssue(
                                "attempt.failed.error-required",
                                "A failed attempt requires an error.",
                                "error"));
                    }

                    break;
                case StepAttemptOutcome.Cancelled:
                    if (completedAt is null)
                    {
                        issues.Add(
                            new ValidationIssue(
                                "attempt.cancelled.inconsistent",
                                "A cancelled attempt requires a completion timestamp.",
                                "completedAt"));
                    }

                    if (error is not null)
                    {
                        issues.Add(
                            new ValidationIssue(
                                "attempt.cancelled.error-unexpected",
                                "A cancelled attempt cannot retain an error.",
                                "error"));
                    }

                    break;
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new StepAttempt(
                    stepId!.Trim(),
                    attemptNumber,
                    startedAt,
                    completedAt,
                    outcome,
                    exitCode,
                    error))
            : ValidationResult.Failure<StepAttempt>(issues);
    }
}

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

    public static ValidationResult<ProjectRun> Create(string? id, string? recipeId)
    {
        return CreateCore(id, recipeId, RunStatus.Draft, [], [], isRehydration: false);
    }

    public static ValidationResult<ProjectRun> Rehydrate(
        string? id,
        string? recipeId,
        RunStatus status,
        IEnumerable<StepAttempt?>? attempts,
        IEnumerable<DevForgeError?>? errors)
    {
        return CreateCore(id, recipeId, status, attempts, errors, isRehydration: true);
    }

    public ValidationResult<ProjectRun> StartAttempt(string? stepId, DateTimeOffset startedAt)
    {
        var normalizedStepId = stepId?.Trim();
        var attemptNumber = Attempts
            .Where(attempt => string.Equals(attempt.StepId, normalizedStepId, StringComparison.Ordinal))
            .Select(attempt => attempt.AttemptNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var attempt = StepAttempt.Start(stepId, attemptNumber, startedAt);
        return attempt.IsValid
            ? ValidationResult.Success(new ProjectRun(Id, RecipeId, Status, Attempts.Add(attempt.Value), Errors))
            : ValidationResult.Failure<ProjectRun>(attempt.Issues);
    }

    public ValidationResult<ProjectRun> CompleteAttempt(
        string? stepId,
        int attemptNumber,
        StepAttemptOutcome outcome,
        DateTimeOffset completedAt,
        int? exitCode,
        DevForgeError? error)
    {
        var index = -1;
        for (var candidateIndex = 0; candidateIndex < Attempts.Length; candidateIndex++)
        {
            var attempt = Attempts[candidateIndex];
            if (string.Equals(attempt.StepId, stepId?.Trim(), StringComparison.Ordinal)
                && attempt.AttemptNumber == attemptNumber
                && attempt.Outcome == StepAttemptOutcome.Running)
            {
                index = candidateIndex;
                break;
            }
        }
        if (index < 0)
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.attempt.running-not-found",
                    "The running attempt was not found.",
                    "attempt"),
            ]);
        }

        var current = Attempts[index];
        var completed = StepAttempt.Rehydrate(
            current.StepId,
            current.AttemptNumber,
            current.StartedAt,
            completedAt,
            outcome,
            exitCode,
            error);
        return completed.IsValid
            ? ValidationResult.Success(new ProjectRun(Id, RecipeId, Status, Attempts.SetItem(index, completed.Value), Errors))
            : ValidationResult.Failure<ProjectRun>(completed.Issues);
    }

    public ValidationResult<ProjectRun> AppendError(DevForgeError? error)
    {
        return error is null
            ? ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue("run.error.required", "A run error is required.", "error"),
            ])
            : ValidationResult.Success(new ProjectRun(Id, RecipeId, Status, Attempts, Errors.Add(error)));
    }

    private static ValidationResult<ProjectRun> CreateCore(
        string? id,
        string? recipeId,
        RunStatus status,
        IEnumerable<StepAttempt?>? attempts,
        IEnumerable<DevForgeError?>? errors,
        bool isRehydration)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add(new ValidationIssue("run.id.required", "A run identifier is required.", "id"));
        }

        if (string.IsNullOrWhiteSpace(recipeId))
        {
            issues.Add(new ValidationIssue("run.recipe-id.required", "A recipe identifier is required.", "recipeId"));
        }

        if (!Enum.IsDefined(status))
        {
            issues.Add(new ValidationIssue("run.status.invalid", "The run status is not defined.", "status"));
        }

        var attemptsSnapshot = attempts?.ToImmutableArray() ?? [];
        var errorsSnapshot = errors?.ToImmutableArray() ?? [];
        if (isRehydration && attempts is null)
        {
            issues.Add(new ValidationIssue("run.attempts.required", "Run attempts are required.", "attempts"));
        }

        if (isRehydration && errors is null)
        {
            issues.Add(new ValidationIssue("run.errors.required", "Run errors are required.", "errors"));
        }

        var attemptKeys = new HashSet<(string StepId, int Number)>();
        for (var index = 0; index < attemptsSnapshot.Length; index++)
        {
            var attempt = attemptsSnapshot[index];
            if (attempt is null)
            {
                issues.Add(new ValidationIssue("run.attempt.required", "Run attempts cannot be null.", $"attempts[{index}]"));
            }
            else if (!attemptKeys.Add((attempt.StepId, attempt.AttemptNumber)))
            {
                issues.Add(
                    new ValidationIssue(
                        "run.attempt.duplicate",
                        "Run attempt identifiers must be unique.",
                        $"attempts[{index}]"));
            }
        }

        for (var index = 0; index < errorsSnapshot.Length; index++)
        {
            if (errorsSnapshot[index] is null)
            {
                issues.Add(new ValidationIssue("run.error.required", "Run errors cannot be null.", $"errors[{index}]"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new ProjectRun(
                    id!.Trim(),
                    recipeId!.Trim(),
                    status,
                    attemptsSnapshot.Select(attempt => attempt!),
                    errorsSnapshot.Select(error => error!)))
            : ValidationResult.Failure<ProjectRun>(issues);
    }

    public ValidationResult<ProjectRun> TransitionTo(RunStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue("run.status.invalid", "The run status is not defined.", "status"),
            ]);
        }

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
