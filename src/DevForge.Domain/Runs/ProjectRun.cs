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
        DevForgeError? error,
        string? outputDigest)
    {
        StepId = stepId;
        AttemptNumber = attemptNumber;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Outcome = outcome;
        ExitCode = exitCode;
        Error = error;
        OutputDigest = outputDigest;
    }

    public string StepId { get; }

    public int AttemptNumber { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public StepAttemptOutcome Outcome { get; }

    public int? ExitCode { get; }

    public DevForgeError? Error { get; }

    public string? OutputDigest { get; }

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
        return Rehydrate(
            stepId,
            attemptNumber,
            startedAt,
            completedAt,
            outcome,
            exitCode,
            error,
            null);
    }

    public static ValidationResult<StepAttempt> Rehydrate(
        string? stepId,
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        StepAttemptOutcome outcome,
        int? exitCode,
        DevForgeError? error,
        string? outputDigest)
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
                case StepAttemptOutcome.Running when completedAt is not null
                    || exitCode is not null
                    || error is not null
                    || outputDigest is not null:
                    issues.Add(
                        new ValidationIssue(
                            outputDigest is null
                                ? "attempt.running.inconsistent"
                                : "attempt.running.output-digest-unexpected",
                            "A running attempt cannot have completion data.",
                            "outcome"));
                    break;
                case StepAttemptOutcome.Succeeded when completedAt is null || error is not null:
                    issues.Add(
                        new ValidationIssue(
                            "attempt.succeeded.inconsistent",
                            "A succeeded attempt requires completion without an error.",
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

        if (outputDigest is not null && !IsCanonicalDigest(outputDigest))
        {
            issues.Add(
                new ValidationIssue(
                    "attempt.output-digest.invalid",
                    "An attempt output digest must be a canonical lowercase SHA-256 value.",
                    "outputDigest"));
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
                    error,
                    outputDigest))
            : ValidationResult.Failure<StepAttempt>(issues);
    }

    private static bool IsCanonicalDigest(string value)
    {
        const string prefix = "sha256:";
        if (value.Length != prefix.Length + 64
            || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value.AsSpan(prefix.Length))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
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
        string? currentStepId,
        IEnumerable<StepAttempt> attempts,
        IEnumerable<DevForgeError> errors)
    {
        Id = id;
        RecipeId = recipeId;
        Status = status;
        CurrentStepId = currentStepId;
        Attempts = [.. attempts];
        Errors = [.. errors];
    }

    public string Id { get; }

    public string RecipeId { get; }

    public RunStatus Status { get; }

    public string? CurrentStepId { get; }

    public ImmutableArray<StepAttempt> Attempts { get; }

    public ImmutableArray<DevForgeError> Errors { get; }

    public bool AllowsStagingCleanup => Status is RunStatus.ValidationFailed
        or RunStatus.Cancelled
        or RunStatus.Failed;

    public static ValidationResult<ProjectRun> Create(string? id, string? recipeId)
    {
        return CreateCore(id, recipeId, RunStatus.Draft, null, [], [], isRehydration: false);
    }

    public static ValidationResult<ProjectRun> Rehydrate(
        string? id,
        string? recipeId,
        RunStatus status,
        string? currentStepId,
        IEnumerable<StepAttempt?>? attempts,
        IEnumerable<DevForgeError?>? errors)
    {
        return CreateCore(id, recipeId, status, currentStepId, attempts, errors, isRehydration: true);
    }

    public ValidationResult<ProjectRun> StartAttempt(string? stepId, DateTimeOffset startedAt)
    {
        if (Status != RunStatus.Executing)
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.attempt.start.status",
                    "An attempt can start only while the run is executing.",
                    "status"),
            ]);
        }

        if (CurrentStepId is not null || Attempts.Any(attempt => attempt.Outcome == StepAttemptOutcome.Running))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.attempt.running",
                    "A run cannot start another attempt while an attempt is running.",
                    "attempts"),
            ]);
        }

        var normalizedStepId = stepId?.Trim();
        var attemptNumber = Attempts
            .Where(attempt => string.Equals(attempt.StepId, normalizedStepId, StringComparison.Ordinal))
            .Select(attempt => attempt.AttemptNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var attempt = StepAttempt.Start(stepId, attemptNumber, startedAt);
        return attempt.IsValid
            ? ValidationResult.Success(
                new ProjectRun(
                    Id,
                    RecipeId,
                    Status,
                    attempt.Value.StepId,
                    Attempts.Add(attempt.Value),
                    Errors))
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
        return CompleteAttempt(
            stepId,
            attemptNumber,
            outcome,
            completedAt,
            exitCode,
            error,
            null);
    }

    public ValidationResult<ProjectRun> CompleteAttempt(
        string? stepId,
        int attemptNumber,
        StepAttemptOutcome outcome,
        DateTimeOffset completedAt,
        int? exitCode,
        DevForgeError? error,
        string? outputDigest)
    {
        if (Status != RunStatus.Executing)
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.attempt.complete.status",
                    "An attempt can complete only while the run is executing.",
                    "status"),
            ]);
        }

        var normalizedStepId = stepId?.Trim();
        var index = -1;
        for (var candidateIndex = 0; candidateIndex < Attempts.Length; candidateIndex++)
        {
            var attempt = Attempts[candidateIndex];
            if (string.Equals(CurrentStepId, normalizedStepId, StringComparison.Ordinal)
                && string.Equals(attempt.StepId, normalizedStepId, StringComparison.Ordinal)
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
                    "The matching current running attempt was not found.",
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
            error,
            outputDigest);
        return completed.IsValid
            ? ValidationResult.Success(
                new ProjectRun(
                    Id,
                    RecipeId,
                    Status,
                    null,
                    Attempts.SetItem(index, completed.Value),
                    Errors))
            : ValidationResult.Failure<ProjectRun>(completed.Issues);
    }

    public ValidationResult<ProjectRun> InterruptCurrentAttempt(
        DateTimeOffset completedAt,
        DevForgeError? error,
        string? outputDigest)
    {
        var running = Attempts.FirstOrDefault(attempt =>
            attempt.Outcome == StepAttemptOutcome.Running
            && string.Equals(attempt.StepId, CurrentStepId, StringComparison.Ordinal));
        if (Status != RunStatus.Executing || running is null)
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.interruption.attempt-required",
                    "An executing running attempt is required for interruption recovery.",
                    "attempts"),
            ]);
        }

        if (error is null
            || !error.IsRetryable
            || !string.Equals(error.Code, "DF-EXEC-003", StringComparison.Ordinal)
            || !string.Equals(error.StepId, running.StepId, StringComparison.Ordinal))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.interruption.error.invalid",
                    "Interruption recovery requires matching retryable DF-EXEC-003 evidence.",
                    "error"),
            ]);
        }

        var completed = StepAttempt.Rehydrate(
            running.StepId,
            running.AttemptNumber,
            running.StartedAt,
            completedAt,
            StepAttemptOutcome.Failed,
            null,
            error,
            outputDigest);
        if (!completed.IsValid)
        {
            return ValidationResult.Failure<ProjectRun>(completed.Issues);
        }

        var index = Attempts.IndexOf(running);
        return ValidationResult.Success(new ProjectRun(
            Id,
            RecipeId,
            Status,
            null,
            Attempts.SetItem(index, completed.Value),
            Errors.Add(error)));
    }

    public ValidationResult<ProjectRun> ResumeExecution()
    {
        var hasRunningAttempt = CurrentStepId is not null
            || Attempts.Any(attempt => attempt.Outcome == StepAttemptOutcome.Running);
        var lastAttempt = Attempts.LastOrDefault();
        var isInterrupted = Status == RunStatus.Executing
            && lastAttempt is
            {
                Outcome: StepAttemptOutcome.Failed,
                Error.Code: "DF-EXEC-003",
                Error.IsRetryable: true,
            }
            && Errors.Any(error =>
                string.Equals(error.Code, lastAttempt.Error.Code, StringComparison.Ordinal)
                && string.Equals(error.StepId, lastAttempt.StepId, StringComparison.Ordinal));
        if (hasRunningAttempt
            || Status is not (RunStatus.Cancelled or RunStatus.ValidationFailed)
                && !isInterrupted)
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.resume.status.invalid",
                    "The run is not in a safely resumable state.",
                    "status"),
            ]);
        }

        return ValidationResult.Success(new ProjectRun(
            Id,
            RecipeId,
            RunStatus.Executing,
            null,
            Attempts,
            Errors));
    }

    public ValidationResult<ProjectRun> AppendError(DevForgeError? error)
    {
        if (Status == RunStatus.Draft || IsTerminalStatus(Status))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.error.append.status",
                    "Run error history cannot be changed in the current status.",
                    "status"),
            ]);
        }

        return error is null
            ? ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue("run.error.required", "A run error is required.", "error"),
            ])
            : ValidationResult.Success(
                new ProjectRun(Id, RecipeId, Status, CurrentStepId, Attempts, Errors.Add(error)));
    }

    private static ValidationResult<ProjectRun> CreateCore(
        string? id,
        string? recipeId,
        RunStatus status,
        string? currentStepId,
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

        string? normalizedCurrentStepId = null;
        if (currentStepId is not null)
        {
            if (string.IsNullOrWhiteSpace(currentStepId))
            {
                issues.Add(
                    new ValidationIssue(
                        "run.current-step.invalid",
                        "A current step identifier cannot be blank.",
                        "currentStepId"));
            }
            else
            {
                normalizedCurrentStepId = currentStepId.Trim();
            }
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

        var runningAttempts = attemptsSnapshot
            .Where(attempt => attempt?.Outcome == StepAttemptOutcome.Running)
            .Select(attempt => attempt!)
            .ToImmutableArray();

        if (status == RunStatus.Draft && (!attemptsSnapshot.IsEmpty || !errorsSnapshot.IsEmpty))
        {
            issues.Add(
                new ValidationIssue(
                    "run.draft.history.invalid",
                    "A draft run cannot contain attempt or error history.",
                    "status"));
        }

        if (status is RunStatus.Planning or RunStatus.PreflightFailed && !attemptsSnapshot.IsEmpty)
        {
            issues.Add(
                new ValidationIssue(
                    "run.attempt-history.status.invalid",
                    "Attempt history is not valid before execution begins.",
                    "attempts"));
        }

        if (status != RunStatus.Executing)
        {
            if (normalizedCurrentStepId is not null)
            {
                issues.Add(
                    new ValidationIssue(
                        "run.current-step.status.invalid",
                        "Only an executing run can have a current step.",
                        "currentStepId"));
            }

            if (!runningAttempts.IsEmpty)
            {
                issues.Add(
                    new ValidationIssue(
                        "run.attempt.running-status.invalid",
                        "Running attempts are valid only while the run is executing.",
                        "attempts"));
            }
        }
        else
        {
            if (runningAttempts.IsEmpty && normalizedCurrentStepId is not null)
            {
                issues.Add(
                    new ValidationIssue(
                        "run.current-step.running-required",
                        "A current step requires a running attempt.",
                        "currentStepId"));
            }

            if (!runningAttempts.IsEmpty && normalizedCurrentStepId is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "run.current-step.required",
                        "A running attempt requires a current step.",
                        "currentStepId"));
            }

            if (runningAttempts.Length > 1)
            {
                issues.Add(
                    new ValidationIssue(
                        "run.attempt.running.multiple",
                        "A run cannot contain more than one running attempt.",
                        "attempts"));
            }

            if (runningAttempts.Length == 1
                && normalizedCurrentStepId is not null
                && !string.Equals(runningAttempts[0].StepId, normalizedCurrentStepId, StringComparison.Ordinal))
            {
                issues.Add(
                    new ValidationIssue(
                        "run.current-step.mismatch",
                        "The current step must identify the running attempt.",
                        "currentStepId"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new ProjectRun(
                    id!.Trim(),
                    recipeId!.Trim(),
                    status,
                    normalizedCurrentStepId,
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

        if (Status == RunStatus.Executing
            && status != Status
            && Attempts.Any(attempt => attempt.Outcome == StepAttemptOutcome.Running))
        {
            return ValidationResult.Failure<ProjectRun>(
            [
                new ValidationIssue(
                    "run.transition.attempt-running",
                    "An executing run cannot change status while an attempt is running.",
                    "attempts"),
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
            new ProjectRun(Id, RecipeId, status, null, Attempts, Errors));
    }

    private static bool IsTerminalStatus(RunStatus status)
    {
        return status is RunStatus.PreflightFailed
            or RunStatus.ValidationFailed
            or RunStatus.Completed
            or RunStatus.Cancelled
            or RunStatus.Failed;
    }
}
