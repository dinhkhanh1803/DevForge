using System.Collections.Immutable;
using System.Security;
using System.Text.RegularExpressions;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public sealed class WorkspaceRoot : IEquatable<WorkspaceRoot>
{
    private static readonly Regex _driveRootPattern = new(
        "^[A-Za-z]:\\\\",
        RegexOptions.CultureInvariant);

    private readonly string _canonicalPath;

    private WorkspaceRoot(string canonicalPath)
    {
        _canonicalPath = canonicalPath;
    }

    internal string RevealForFileSystem()
    {
        return _canonicalPath;
    }

    public static ValidationResult<WorkspaceRoot> Create(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return Failure("workspace.root.required", "A workspace root is required.");
        }

        try
        {
            if (fullPath != fullPath.Trim()
                || fullPath.Any(char.IsControl)
                || IsDeviceOrUncPath(fullPath)
                || !_driveRootPattern.IsMatch(fullPath)
                || HasInvalidSegments(fullPath[3..]))
            {
                return Failure(
                    "workspace.root.invalid",
                    "The workspace root must be a canonical local Windows drive path.");
            }

            var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
            return ValidationResult.Success(new WorkspaceRoot(canonicalPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or SecurityException)
        {
            return Failure(
                "workspace.root.invalid",
                "The workspace root is not a valid local Windows path.");
        }
    }

    public bool Equals(WorkspaceRoot? other)
    {
        return other is not null
            && StringComparer.OrdinalIgnoreCase.Equals(_canonicalPath, other._canonicalPath);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorkspaceRoot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(_canonicalPath);
    }

    public override string ToString()
    {
        return "[WORKSPACE ROOT]";
    }

    private static ValidationResult<WorkspaceRoot> Failure(string code, string message)
    {
        return ValidationResult.Failure<WorkspaceRoot>(
        [
            new ValidationIssue(code, message, "fullPath"),
        ]);
    }

    private static bool IsDeviceOrUncPath(string value)
    {
        return value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase)
            || value.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasInvalidSegments(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var withoutTrailingSeparator = value.EndsWith('\\') ? value[..^1] : value;
        var segments = withoutTrailingSeparator.Split('\\');
        return segments.Any(IsInvalidWindowsSegment);
    }

    internal static bool IsInvalidWindowsSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)
            || segment != segment.Trim()
            || segment.EndsWith('.')
            || segment.Any(char.IsControl)
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return true;
        }

        var baseName = segment.Split('.')[0];
        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDevice(baseName, "COM")
            || IsNumberedDevice(baseName, "LPT");
    }

    private static bool IsNumberedDevice(string value, string prefix)
    {
        return value.Length == 4
            && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && value[3] is >= '1' and <= '9';
    }
}

public sealed class WorkspaceRelativePath : IEquatable<WorkspaceRelativePath>
{
    private readonly string _canonicalValue;

    private WorkspaceRelativePath(string canonicalValue)
    {
        _canonicalValue = canonicalValue;
    }

    public string Value => _canonicalValue;

    internal string RevealForFileSystem()
    {
        return _canonicalValue;
    }

    public static ValidationResult<WorkspaceRelativePath> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Failure("workspace.path.required", "A workspace-relative path is required.");
        }

        try
        {
            if (value != value.Trim()
                || value.Any(char.IsControl)
                || value.Contains('/')
                || value.StartsWith('\\')
                || value.Contains(':')
                || value.StartsWith(@"\\?\", StringComparison.Ordinal)
                || value.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    "workspace.path.invalid",
                    "A workspace path must use a canonical guarded relative Windows form.");
            }

            var segments = value.Split('\\');
            if (segments.Any(
                segment => segment is "." or ".."
                    || WorkspaceRoot.IsInvalidWindowsSegment(segment)))
            {
                return Failure(
                    "workspace.path.segment.invalid",
                    "A workspace path contains an invalid, traversal, or reserved segment.");
            }

            return ValidationResult.Success(new WorkspaceRelativePath(string.Join('\\', segments)));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return Failure("workspace.path.invalid", "The workspace-relative path is invalid.");
        }
    }

    public bool Equals(WorkspaceRelativePath? other)
    {
        return other is not null
            && StringComparer.OrdinalIgnoreCase.Equals(_canonicalValue, other._canonicalValue);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorkspaceRelativePath other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(_canonicalValue);
    }

    public override string ToString()
    {
        return _canonicalValue;
    }

    private static ValidationResult<WorkspaceRelativePath> Failure(string code, string message)
    {
        return ValidationResult.Failure<WorkspaceRelativePath>(
        [
            new ValidationIssue(code, message, "value"),
        ]);
    }
}

public enum DirectoryCleanupIntent
{
    RecursiveRunOwned = 1,
}

public enum WorkspaceMoveIntent
{
    AtomicNoOverwriteFinalize = 1,
}

/// <summary>
/// Provides operations scoped to an opened root. Concrete M3 implementations must additionally enforce
/// canonical handle containment and reject reparse-point, junction, and symbolic-link escapes.
/// </summary>
public interface IWorkspaceFileSystem
{
    WorkspaceRoot Root { get; }

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

    Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
        CancellationToken cancellationToken);

    Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
        CancellationToken cancellationToken);

    Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
        WorkspaceRelativePath directory,
        bool recursive,
        CancellationToken cancellationToken);

    Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
        WorkspaceRelativePath directory,
        CancellationToken cancellationToken);

    Task DeleteDirectoryAsync(
        WorkspaceRelativePath path,
        DirectoryCleanupIntent intent,
        CancellationToken cancellationToken);

    Task MoveDirectoryAsync(
        WorkspaceRelativePath source,
        WorkspaceRelativePath destination,
        WorkspaceMoveIntent intent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Adds an atomic create-if-absent operation for infrastructure that must prove ownership of a new directory.
/// </summary>
public interface IAtomicWorkspaceFileSystem : IWorkspaceFileSystem
{
    Task<bool> TryCreateDirectoryAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);
}

/// <summary>
/// Writes a complete bounded caller-owned byte snapshot through a guarded sibling file and
/// publishes it with one same-volume atomic rename. Implementations must never expose a
/// partially written destination and must preserve an existing destination on failure.
/// </summary>
public interface IAtomicFileWorkspaceFileSystem : IWorkspaceFileSystem
{
    Task WriteFileAtomicallyAsync(
        WorkspaceRelativePath path,
        ReadOnlyMemory<byte> content,
        bool overwrite,
        CancellationToken cancellationToken);
}

public interface IWorkspaceExclusiveLease : IAsyncDisposable
{
}

/// <summary>
/// Acquires an OS-exclusive handle to a guarded workspace-relative lease file. A null result means
/// another process owns the lease; implementations must not expose the underlying path or handle.
/// </summary>
public interface IExclusiveLeaseWorkspaceFileSystem : IWorkspaceFileSystem
{
    Task<IWorkspaceExclusiveLease?> TryAcquireExclusiveLeaseAsync(
        WorkspaceRelativePath path,
        CancellationToken cancellationToken);
}

public interface IFileSystem
{
    Task<IWorkspaceFileSystem> OpenWorkspaceAsync(
        WorkspaceRoot allowedRoot,
        CancellationToken cancellationToken);
}
