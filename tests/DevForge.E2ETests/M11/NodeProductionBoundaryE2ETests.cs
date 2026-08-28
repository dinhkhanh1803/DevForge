using System.IO;
using System.Security.Cryptography;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.E2ETests.M9;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Processes;
using DevForge.Infrastructure.Security;
using Xunit.Abstractions;

namespace DevForge.E2ETests.M11;

[Collection(M9ExecutionTestGroup.Name)]
public sealed class NodeProductionBoundaryE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProductionPnpmIgnoresAncestorWorkspaceConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "DevForge-NodeAncestor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "project"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "pnpm-workspace.yaml"), "packages: [invalid:\n");
            await File.WriteAllTextAsync(Path.Combine(root, ".npmrc"), "node-linker=invalid\n");
            await File.WriteAllTextAsync(Path.Combine(root, "sentinel.txt"), "untouched");
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(WorkspaceRoot.Create(Path.Combine(root, "project")).Value, default);
            await ((IAtomicFileWorkspaceFileSystem)workspace).WriteFileAtomicallyAsync(WorkspaceRelativePath.Create("package.json").Value,
                "{\"private\":true,\"scripts\":{\"lint\":\"node --version\"}}"u8.ToArray(), false, default);
            var command = CommandSpec.CreateAtWorkspaceRoot(ExecutableIdentity.Create("pnpm").Value,
                ["run", "lint"], workspace, [], TimeSpan.FromSeconds(30), [0], []).Value;
            var result = await new WindowsProcessRunner().RunAsync(command, null, default);
            Assert.True(result.ExitCode == 0, string.Join("\n", result.RetainedLines.Select(line => line.Text.Value)));
            Assert.Equal("untouched", await File.ReadAllTextAsync(Path.Combine(root, "sentinel.txt")));
            Assert.Equal("node-linker=invalid\n", await File.ReadAllTextAsync(Path.Combine(root, ".npmrc")));
        }
        finally
        {
            Assert.StartsWith(Path.GetTempPath() + "DevForge-NodeAncestor-", root, StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "ReleaseAcceptance")]
    public async Task RealReactProductionRunnerReachesGuardedPublicationAndRecovery()
    {
        await using var fixture = await WpfBlueprintFixture.CreateReactAsync(new ObservingRunner(output));
        var plan = await fixture.Workflow.CreatePlanAsync(fixture.Draft, default);
        Assert.True(plan.IsValid);
        var run = await fixture.Workflow.ExecuteAsync(plan.Value, null, default);
        Assert.True(run.IsValid);
        Assert.True(run.Value.Checkpoint.Run.Status == RunStatus.LocalReady,
            string.Join("; ", run.Value.Checkpoint.Run.Errors.Select(error => error.Code + ": " + error.Summary)));
        foreach (var directory in new[] { "node_modules", ".next", ".devforge-node", "tooling" })
        {
            Assert.False(Directory.Exists(Path.Combine(fixture.TargetPath, directory)), directory);
        }
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, ".devforge-staging", plan.Value.RunId)));
        var published = await fixture.PublishLocalAsync(plan.Value.RunId);
        Assert.True(published.IsSuccessful, published.Error?.Summary);
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        var source = Path.Combine(fixture.TargetPath, "package.json");
        var original = await File.ReadAllBytesAsync(source);
        await File.WriteAllBytesAsync(source, [.. original, 32]);
        Assert.False((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        await File.WriteAllBytesAsync(source, original);
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        Assert.Equal(0, fixture.RemoteGitHub.Calls);
        var artifact = Path.Combine(fixture.TargetPath, "dist", "index.html");
        var originalArtifact = await File.ReadAllBytesAsync(artifact);
        await File.WriteAllBytesAsync(artifact, [.. originalArtifact, 32]);
        Assert.False((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        await File.WriteAllBytesAsync(artifact, originalArtifact);
        Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        var bundle = Assert.Single(Directory.GetFiles(Path.Combine(fixture.TargetPath, "dist", "assets"), "*.js"));
        var bundleBytes = await File.ReadAllBytesAsync(bundle);
        Assert.Equal("0dc53246ec934df87e6acfa00a2471debd43f04b14226866942282655cb5236d",
            Convert.ToHexStringLower(SHA256.HashData(bundleBytes)));
        foreach (var changed in new byte[][] { [.. bundleBytes, 32], [0, .. bundleBytes] })
        {
            await File.WriteAllBytesAsync(bundle, changed);
            Assert.False((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
            await File.WriteAllBytesAsync(bundle, bundleBytes);
            Assert.True((await fixture.PublishLocalAsync(plan.Value.RunId)).IsSuccessful);
        }
    }

    private sealed class ObservingRunner(ITestOutputHelper output) : IProcessRunner
    {
        private readonly WindowsProcessRunner _runner = new();
        public Task CheckPreconditionsAsync(CommandSpec command, CancellationToken cancellationToken) =>
            _runner.CheckPreconditionsAsync(command, cancellationToken);
        public async Task<ProcessResult> RunAsync(CommandSpec command, IProgress<ProcessOutputLine>? progress, CancellationToken cancellationToken)
        {
            var result = await _runner.RunAsync(command, progress, cancellationToken);
            output.WriteLine(string.Join(' ', command.ArgumentList) + ": " + result.TerminationReason + "/" + result.ExitCode);
            output.WriteLine(string.Join(Environment.NewLine, result.RetainedLines.Select(line => line.Text.Value)));
            if (result.ExitCode == 0 && command.ArgumentList.SequenceEqual(["run", "build"]))
            {
                var files = await command.Workspace.EnumerateFilesAsync(WorkspaceRelativePath.Create("dist").Value, true, cancellationToken);
                foreach (var file in files.Where(file => file.Value.EndsWith(".js", StringComparison.Ordinal)))
                {
                    await using var artifact = await command.Workspace.OpenReadAsync(file, cancellationToken);
                    output.WriteLine("Public artifact SHA256: " + Convert.ToHexStringLower(await SHA256.HashDataAsync(artifact, cancellationToken)));
                }
                var scan = await new WorkspaceSecretScanner().ScanAsync(SecretScanRequest.ExplicitPaths(command.Workspace, files).Value, cancellationToken);
                Assert.True(scan.Findings.IsEmpty, string.Join("; ", scan.Findings.Select(
                    finding => $"{finding.Path.Value}: {finding.Description.Value}")));
            }
            return result;
        }
    }
}
