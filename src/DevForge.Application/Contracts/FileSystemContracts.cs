using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed record WorkspaceRoot
{
    private WorkspaceRoot(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public static ValidationResult<WorkspaceRoot> Create(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            return ValidationResult.Failure<WorkspaceRoot>(
            [
                new ValidationIssue(
                    "workspace.root.absolute",
                    "A workspace root must be an absolute path.",
                    "fullPath"),
            ]);
        }

        return ValidationResult.Success(new WorkspaceRoot(Path.GetFullPath(fullPath)));
    }
}

public sealed record WorkspaceRelativePath
{
    private WorkspaceRelativePath(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ValidationResult<WorkspaceRelativePath> Create(string? value)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(
                new ValidationIssue(
                    "workspace.path.required",
                    "A workspace-relative path is required.",
                    "value"));
        }
        else
        {
            if (Path.IsPathRooted(value) || Path.IsPathFullyQualified(value))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.rooted",
                        "A workspace path must be relative to its opened root.",
                        "value"));
            }

            if (value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.separator.invalid",
                        "A workspace path must use the platform directory separator.",
                        "value"));
            }

            var segments = value.Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment.Length == 0))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.segment.empty",
                        "A workspace path cannot contain empty segments.",
                        "value"));
            }

            if (segments.Any(segment => segment is "." or ".."))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.traversal",
                        "A workspace path cannot contain dot or parent segments.",
                        "value"));
            }

            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment != segment.Trim()))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.segment.invalid",
                        "Workspace path segments cannot be blank or padded with whitespace.",
                        "value"));
            }

            var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            if (segments.Any(segment => segment.IndexOfAny(invalidFileNameCharacters) >= 0))
            {
                issues.Add(
                    new ValidationIssue(
                        "workspace.path.character.invalid",
                        "A workspace path contains an invalid character or alternate data stream.",
                        "value"));
            }
        }

        return issues.Count == 0
            ? ValidationResult.Success(new WorkspaceRelativePath(value!))
            : ValidationResult.Failure<WorkspaceRelativePath>(issues);
    }
}

public interface IFileSystem
{
    Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
        WorkspaceRoot allowedRoot,
        CancellationToken cancellationToken);
}

public interface IWorkspaceFileSystem
{
    Task<bool> FileExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);

    Task<bool> DirectoryExistsAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);

    Task CreateDirectoryAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);

    Task<Stream> OpenWriteAsync(
        WorkspaceRelativePath path,
        bool overwrite,
        CancellationToken cancellationToken);

    Task DeleteFileAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);

    Task MoveAsync(
        WorkspaceRelativePath source,
        WorkspaceRelativePath destination,
        CancellationToken cancellationToken);
}
