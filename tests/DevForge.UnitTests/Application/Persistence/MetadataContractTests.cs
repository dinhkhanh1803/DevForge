using System.Globalization;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.UnitTests.Application.Persistence;

public sealed class MetadataContractTests
{
    [Fact]
    public void CreatesNormalizedMetadataSnapshots()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z", CultureInfo.InvariantCulture);
        var policy = PersistableJson.Create("{\"branch\":\"main\"}").Value;

        var ide = IdeInstallationRecord.Create(
            " vscode ",
            IdeKind.VisualStudioCode,
            @"C:\Tools\Code.exe",
            " 1.2.3 ",
            InstallationValidationState.Valid,
            now).Value;
        var tool = EnvironmentToolRecord.Create(
            " git ",
            @"C:\Tools\git.exe",
            " 2.51.0 ",
            EnvironmentToolStatus.Compatible,
            now,
            now.AddMinutes(30)).Value;
        var blueprint = BlueprintMetadataRecord.Create(
            " web.react-vite-ts ",
            " 1.0.0 ",
            BlueprintSource.BuiltIn,
            BlueprintTrust.BuiltIn,
            new string('a', 64),
            false,
            now).Value;
        var profile = TeamProfileRecord.Create(" team.standard ", " Team Standard ", 1, policy, now).Value;
        var preset = PresetRecord.Create(" preset.react ", " React Preset ", 1, policy, now).Value;
        var recent = RecentProjectRecord.Create(
            @"C:\Projects\portal",
            " Client Portal ",
            "https://github.com/example/portal",
            " vscode ",
            now).Value;

        Assert.Equal("vscode", ide.Id);
        Assert.Equal("1.2.3", ide.Version);
        Assert.Equal("git", tool.Id);
        Assert.Equal("2.51.0", tool.Version);
        Assert.Equal("web.react-vite-ts", blueprint.Id);
        Assert.Equal("team.standard", profile.Id);
        Assert.Equal("preset.react", preset.Id);
        Assert.Equal("vscode", recent.IdeId);
    }

    [Fact]
    public void MetadataEnumsNeverUseDefaultAsAValidValue()
    {
        Assert.DoesNotContain(0, Enum.GetValues<IdeKind>().Cast<int>());
        Assert.DoesNotContain(0, Enum.GetValues<InstallationValidationState>().Cast<int>());
        Assert.DoesNotContain(0, Enum.GetValues<EnvironmentToolStatus>().Cast<int>());
        Assert.DoesNotContain(0, Enum.GetValues<BlueprintSource>().Cast<int>());
    }

    [Fact]
    public void EnvironmentCacheRejectsExpiryBeforeScan()
    {
        var now = DateTimeOffset.UtcNow;

        var result = EnvironmentToolRecord.Create(
            "git",
            @"C:\Tools\git.exe",
            "2.51.0",
            EnvironmentToolStatus.Compatible,
            now,
            now.AddSeconds(-1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.tool.expiry.invalid");
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("ABCDEF")]
    public void BlueprintRejectsInvalidChecksums(string checksum)
    {
        var result = BlueprintMetadataRecord.Create(
            "web.react-vite-ts",
            "1.0.0",
            BlueprintSource.BuiltIn,
            BlueprintTrust.BuiltIn,
            checksum,
            false,
            DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.blueprint.checksum.invalid");
    }

    [Theory]
    [InlineData(@"relative\project")]
    [InlineData(@"\\server\share\project")]
    [InlineData(@"C:\Projects\portal:stream")]
    [InlineData(@"C:\CON\portal")]
    public void RecentProjectRejectsNonLocalPaths(string path)
    {
        var result = RecentProjectRecord.Create(
            path,
            "Project",
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.recent.path.invalid");
    }

    [Theory]
    [InlineData("https://github.com/example/portal?token=value")]
    [InlineData("https://user:password@github.com/example/portal")]
    [InlineData("http://github.com/example/portal")]
    public void RecentProjectRejectsUnsafeRepositoryUrls(string repositoryUrl)
    {
        var result = RecentProjectRecord.Create(
            @"C:\Projects\portal",
            "Project",
            repositoryUrl,
            null,
            DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.recent.repository-url.invalid");
    }

    [Fact]
    public void StoreContractsRequireCancellationTokens()
    {
        var storeTypes = new[]
        {
            typeof(IAppSettingsStore),
            typeof(IIdeInstallationStore),
            typeof(IEnvironmentToolStore),
            typeof(IBlueprintMetadataStore),
            typeof(ITeamProfileStore),
            typeof(IPresetStore),
            typeof(IRecentProjectStore),
        };

        foreach (var storeType in storeTypes)
        {
            Assert.All(
                storeType.GetMethods(),
                method => Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType));
        }
    }
}
