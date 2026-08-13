using System.Text.RegularExpressions;
using DevForge.Application.Contracts;
using DevForge.Infrastructure.Creation;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Creation;

public sealed partial class WindowsProjectTargetServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DevForge-CreationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AbsentTargetPassesWithoutLeavingWriteProbe()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.PreflightAsync(
            fixture.ProjectRoot,
            "sample-project",
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("sample-project", result.Value.TargetDirectory.Value);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ProjectRoot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingTargetIsRejectedBeforeRunArtifactsAreCreated(bool asDirectory)
    {
        var fixture = CreateFixture();
        var target = Path.Combine(fixture.ProjectRoot, "occupied");
        if (asDirectory)
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            await File.WriteAllTextAsync(target, "owned by user");
        }

        var result = await fixture.Service.PreflightAsync(
            fixture.ProjectRoot,
            "occupied",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "project.target.not-empty");
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.LocalDataRoot));
    }

    [Fact]
    public async Task InvalidOrReservedTargetIsRejectedWithoutMutation()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.PreflightAsync(
            "relative-root",
            "CON",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "project.target.root.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "project.target.directory.invalid");
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ProjectRoot));
    }

    [Fact]
    public async Task CancellationIsObservedBeforeFileSystemMutation()
    {
        var fixture = CreateFixture();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.PreflightAsync(
            fixture.ProjectRoot,
            "sample-project",
            source.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ProjectRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.LocalDataRoot));
    }

    [Fact]
    public async Task OpenCreatesExactGuardedRunArtifactWorkspace()
    {
        var fixture = CreateFixture();
        var target = await fixture.Service.PreflightAsync(
            fixture.ProjectRoot,
            "sample-project",
            CancellationToken.None);

        var result = await fixture.Service.OpenAsync(
            target.Value,
            "run-0123456789abcdef0123456789abcdef",
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(target.Value.TargetDirectory, result.Value.TargetDirectory);
        Assert.Equal(target.Value.ParentRoot, result.Value.TargetParent.Root);
        Assert.True(Directory.Exists(Path.Combine(
            fixture.LocalDataRoot,
            "runs",
            "run-0123456789abcdef0123456789abcdef")));
    }

    [Fact]
    public async Task OpenRejectsMalformedOrReusedRunIdentity()
    {
        var fixture = CreateFixture();
        var target = await fixture.Service.PreflightAsync(
            fixture.ProjectRoot,
            "sample-project",
            CancellationToken.None);
        var runId = "run-0123456789abcdef0123456789abcdef";

        Assert.False((await fixture.Service.OpenAsync(
            target.Value,
            "run-not-canonical",
            CancellationToken.None)).IsValid);
        Assert.True((await fixture.Service.OpenAsync(
            target.Value,
            runId,
            CancellationToken.None)).IsValid);
        Assert.False((await fixture.Service.OpenAsync(
            target.Value,
            runId,
            CancellationToken.None)).IsValid);
    }

    [Fact]
    public void RunIdentityGeneratorProducesBoundedCanonicalIds()
    {
        var sut = new GuidRunIdentityGenerator();

        var runIds = Enumerable.Range(0, 32).Select(_ => sut.CreateRunId()).ToArray();
        var recipeIds = Enumerable.Range(0, 32).Select(_ => sut.CreateRecipeId()).ToArray();

        Assert.All(runIds, value => Assert.Matches(RunIdentityPattern(), value));
        Assert.All(recipeIds, value => Assert.Matches(RecipeIdentityPattern(), value));
        Assert.Equal(runIds.Length, runIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(recipeIds.Length, recipeIds.Distinct(StringComparer.Ordinal).Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Fixture CreateFixture()
    {
        var projectRoot = Path.Combine(_root, "projects");
        var localDataRoot = Path.Combine(_root, "local-data");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(localDataRoot);
        var localData = WorkspaceRoot.Create(localDataRoot);
        Assert.True(localData.IsValid);
        return new Fixture(
            projectRoot,
            localDataRoot,
            new WindowsProjectTargetService(new WindowsFileSystem(), localData.Value));
    }

    [GeneratedRegex("^run-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdentityPattern();

    [GeneratedRegex("^recipe-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecipeIdentityPattern();

    private sealed record Fixture(
        string ProjectRoot,
        string LocalDataRoot,
        WindowsProjectTargetService Service);
}
