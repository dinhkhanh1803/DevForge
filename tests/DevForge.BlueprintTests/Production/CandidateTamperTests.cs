using System.Text;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.BlueprintTests.Production;

public sealed class CandidateTamperTests
{
    [Theory]
    [InlineData("desktop.csharp-winforms-tool")]
    [InlineData("tool.python-desktop")]
    [InlineData("web.next-ts")]
    public async Task CandidatePayloadUsesGitCanonicalLfBytes(string id)
    {
        // .gitattributes declares blueprint payloads as text with LF checkout bytes.
        // Hashing CRLF locally would otherwise pass here but break a fresh clone.
        using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        var paths = (await fixture.Source.Workspace.EnumerateAllFilesAsync(CancellationToken.None))
            .Where(path => path.Value.StartsWith(id + "\\", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var bytes = await ReadAsync(fixture.Source.Workspace, path);
            Assert.True(!bytes.Contains((byte)'\r'), path.Value + " must use LF before checksumming.");
        }
    }

    [Theory]
    [InlineData("desktop.csharp-winforms-tool", "manifest.yaml")]
    [InlineData("desktop.csharp-winforms-tool", "overlays/base/src/TeamTool.Desktop/MainForm.cs")]
    [InlineData("desktop.csharp-winforms-tool", "templates/Directory.Packages.props")]
    [InlineData("desktop.csharp-winforms-tool", "checksums.json")]
    [InlineData("tool.python-desktop", "manifest.yaml")]
    [InlineData("tool.python-desktop", "overlays/base/src/team_tool/desktop.py")]
    [InlineData("tool.python-desktop", "templates/uv.lock")]
    [InlineData("tool.python-desktop", "checksums.json")]
    [InlineData("web.next-ts", "manifest.yaml")]
    [InlineData("web.next-ts", "overlays/base/src/app/page.tsx")]
    [InlineData("web.next-ts", "templates/pnpm-lock.yaml")]
    [InlineData("web.next-ts", "checksums.json")]
    public async Task TamperedCandidateIsQuarantinedAndCannotResolve(string id, string payload)
    {
        using var original = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
        var fileSystem = new WindowsFileSystem();
        var parent = await fileSystem.OpenWorkspaceAsync(WorkspaceRoot.Create(Path.GetTempPath()).Value,
            CancellationToken.None);
        var owned = Relative("DevForge-Candidate-Tamper-" + Guid.NewGuid().ToString("N"));
        Assert.True(await ((IAtomicWorkspaceFileSystem)parent).TryCreateDirectoryAsync(owned, CancellationToken.None));
        try
        {
            using var copy = await ProductionBlueprintCatalogFixture.CreateAtAsync(
                Path.Combine(Path.GetTempPath(), owned.Value));
            foreach (var path in await original.Source.Workspace.EnumerateAllFilesAsync(CancellationToken.None))
            {
                var separator = path.Value.LastIndexOf('\\');
                if (separator >= 0)
                {
                    await copy.Source.Workspace.CreateDirectoryAsync(Relative(path.Value[..separator]), CancellationToken.None);
                }
                await using var input = await original.Source.Workspace.OpenReadAsync(path, CancellationToken.None);
                await using var output = await copy.Source.Workspace.OpenWriteAsync(path, false, CancellationToken.None);
                await input.CopyToAsync(output);
            }
            var reference = BlueprintReference.Create(id, "1.0.0").Value;
            await copy.Catalog.RefreshAsync(CancellationToken.None);
            Assert.NotNull(await copy.Catalog.FindAsync(reference, CancellationToken.None));
            var pristine = await copy.Catalog.ListAsync(CancellationToken.None);
            var target = Relative(id + "\\" + payload.Replace('/', '\\'));
            var before = await ReadAsync(original.Source.Workspace, target);
            await using (var stream = await copy.Source.Workspace.OpenWriteAsync(target, true, CancellationToken.None))
            {
                await stream.WriteAsync(payload == "checksums.json"
                    ? Encoding.UTF8.GetBytes("{}\n")
                    : [.. before, .. Encoding.UTF8.GetBytes("\n# tampered\n")]);
            }
            await copy.Catalog.RefreshAsync(CancellationToken.None);
            var snapshot = await copy.Catalog.InspectAsync(CancellationToken.None);
            Assert.Null(await copy.Catalog.FindAsync(reference, CancellationToken.None));
            Assert.DoesNotContain(snapshot.ExecutableBlueprints, item => item.Manifest.Id == id);
            var inspection = Assert.Single(snapshot.Inspections, item => item.PackageDirectory.Value == id);
            Assert.Equal(BlueprintTrust.Quarantined, inspection.Trust);
            Assert.Contains(inspection.Issues, issue => issue.Code == "DF-BP-002");
            Assert.Equal(pristine.Where(item => item.Manifest.Id != id).Select(item => item.Manifest.Id),
                snapshot.ExecutableBlueprints.Select(item => item.Manifest.Id));
            Assert.Equal(before, await ReadAsync(original.Source.Workspace, target));
            await original.Catalog.RefreshAsync(CancellationToken.None);
            Assert.NotNull(await original.Catalog.FindAsync(reference, CancellationToken.None));
        }
        finally
        {
            await parent.DeleteDirectoryAsync(owned, DirectoryCleanupIntent.RecursiveRunOwned, CancellationToken.None);
        }
    }

    private static WorkspaceRelativePath Relative(string path) => WorkspaceRelativePath.Create(path).Value;

    private static async Task<byte[]> ReadAsync(IWorkspaceFileSystem workspace, WorkspaceRelativePath path)
    {
        await using var stream = await workspace.OpenReadAsync(path, CancellationToken.None);
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        return bytes.ToArray();
    }
}
