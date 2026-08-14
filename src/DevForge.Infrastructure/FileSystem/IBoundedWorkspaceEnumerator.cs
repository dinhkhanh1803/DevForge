using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.FileSystem;

internal sealed record BoundedWorkspaceEnumeration(
    ImmutableArray<WorkspaceRelativePath> Files,
    ImmutableArray<WorkspaceRelativePath> Directories,
    bool LimitExceeded);

internal interface IBoundedWorkspaceEnumerator
{
    Task<BoundedWorkspaceEnumeration> EnumerateTreeBoundedAsync(
        WorkspaceRelativePath? excludedRootDirectory,
        int maximumFiles,
        int maximumDirectories,
        int maximumDepth,
        CancellationToken cancellationToken);
}
