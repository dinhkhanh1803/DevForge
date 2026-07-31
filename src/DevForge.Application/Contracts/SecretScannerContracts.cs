using System.Collections.Immutable;
using DevForge.Domain.Privacy;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class SecretScanRequest
{
    private SecretScanRequest(
        IWorkspaceFileSystem workspace,
        ImmutableArray<WorkspaceRelativePath> paths)
    {
        Workspace = workspace;
        Paths = paths;
    }

    public IWorkspaceFileSystem Workspace { get; }

    public ImmutableArray<WorkspaceRelativePath> Paths { get; }

    public static ValidationResult<SecretScanRequest> Create(
        IWorkspaceFileSystem? workspace,
        IEnumerable<WorkspaceRelativePath?>? paths)
    {
        var pathSnapshot = paths?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (workspace is null)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-scan.workspace.required",
                    "A scoped workspace is required for secret scanning.",
                    "workspace"));
        }

        if (paths is null)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-scan.paths.required",
                    "Scoped workspace paths are required for secret scanning.",
                    "paths"));
        }

        for (var index = 0; index < pathSnapshot.Length; index++)
        {
            if (pathSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "secret-scan.path.required",
                        "Secret scan paths cannot contain null values.",
                        $"paths[{index}]"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new SecretScanRequest(
                    workspace!,
                    [.. pathSnapshot.Select(path => path!)]))
            : ValidationResult.Failure<SecretScanRequest>(issues);
    }
}

public sealed class SecretFinding
{
    private SecretFinding(
        WorkspaceRelativePath path,
        int? lineNumber,
        RedactedText description)
    {
        Path = path;
        LineNumber = lineNumber;
        Description = description;
    }

    public WorkspaceRelativePath Path { get; }

    public int? LineNumber { get; }

    public RedactedText Description { get; }

    public static ValidationResult<SecretFinding> Create(
        WorkspaceRelativePath? path,
        int? lineNumber,
        RedactedText? description)
    {
        var issues = new List<ValidationIssue>();
        if (path is null)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-finding.path.required",
                    "A scoped workspace path is required for a secret finding.",
                    "path"));
        }

        if (lineNumber <= 0)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-finding.line.invalid",
                    "A secret finding line number must be positive when supplied.",
                    "lineNumber"));
        }

        if (description is null)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-finding.description.required",
                    "A redacted secret finding description is required.",
                    "description"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new SecretFinding(path!, lineNumber, description!))
            : ValidationResult.Failure<SecretFinding>(issues);
    }
}

public sealed class SecretScanResult
{
    private SecretScanResult(ImmutableArray<SecretFinding> findings)
    {
        Findings = findings;
    }

    public ImmutableArray<SecretFinding> Findings { get; }

    public static ValidationResult<SecretScanResult> Create(
        IEnumerable<SecretFinding?>? findings)
    {
        var findingSnapshot = findings?.ToImmutableArray() ?? [];
        var issues = new List<ValidationIssue>();
        if (findings is null)
        {
            issues.Add(
                new ValidationIssue(
                    "secret-scan.findings.required",
                    "Secret scan findings are required.",
                    "findings"));
        }

        for (var index = 0; index < findingSnapshot.Length; index++)
        {
            if (findingSnapshot[index] is null)
            {
                issues.Add(
                    new ValidationIssue(
                        "secret-scan.finding.required",
                        "Secret scan findings cannot contain null values.",
                        $"findings[{index}]"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(
                new SecretScanResult([.. findingSnapshot.Select(finding => finding!)]))
            : ValidationResult.Failure<SecretScanResult>(issues);
    }
}

public interface ISecretScanner
{
    Task<SecretScanResult> ScanAsync(
        SecretScanRequest request,
        CancellationToken cancellationToken);
}
