namespace DevForge.Infrastructure.Persistence.Entities;

internal sealed class ProjectRunEntity
{
    public string Id { get; set; } = string.Empty;

    public string RecipeId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? CurrentStepId { get; set; }

    public long CreatedAtUnixMs { get; set; }

    public long UpdatedAtUnixMs { get; set; }

    public long? CompletedAtUnixMs { get; set; }

    public string? StagingPath { get; set; }

    public string? TargetPath { get; set; }

    public string? PlanHash { get; set; }

    public string? PlanJson { get; set; }

    public string? PlanBodyChecksum { get; set; }

    public string? BlueprintId { get; set; }

    public string? BlueprintVersion { get; set; }

    public string? BlueprintSourceId { get; set; }

    public string? BlueprintPackageDirectory { get; set; }

    public string? BlueprintTrust { get; set; }

    public string? BlueprintChecksum { get; set; }

    public string? StagingPayloadPath { get; set; }

    public string? OwnershipMarkerPath { get; set; }

    public string? OwnershipMarkerId { get; set; }

    public string? TargetParentRoot { get; set; }

    public string? CrossVolumeTemporaryPath { get; set; }

    public string? RunArtifactRoot { get; set; }

    public string? EvidenceJson { get; set; }

    public string? FinalizationState { get; set; }

    public string? ReportState { get; set; }

    public string ErrorsJson { get; set; } = "[]";

    public List<RunStepEntity> Steps { get; } = [];
}

internal sealed class RunStepEntity
{
    public string RunId { get; set; } = string.Empty;

    public string StepId { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    public int? SequenceNumber { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public long StartedAtUnixMs { get; set; }

    public long? CompletedAtUnixMs { get; set; }

    public int? ExitCode { get; set; }

    public string? OutputDigest { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorSummary { get; set; }

    public string? ErrorTechnicalDetail { get; set; }

    public string? ErrorPhase { get; set; }

    public string? ErrorStepId { get; set; }

    public bool? ErrorIsRetryable { get; set; }

    public string? ErrorSuggestedActionsJson { get; set; }

    public string? ErrorContextJson { get; set; }

    public ProjectRunEntity Run { get; set; } = null!;
}
