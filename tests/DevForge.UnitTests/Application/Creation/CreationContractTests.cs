using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.UnitTests.Application.Creation;

public sealed class CreationContractTests
{
    [Fact]
    public void ClosedCreationEnumsHaveExplicitNonzeroValues()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], Enum.GetValues<ProjectCreationStage>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<DynamicInputValueKind>().Select(value => (int)value));
    }

    [Fact]
    public void DynamicInputValuesAreTypedAndBounded()
    {
        var text = DynamicInputValue.Text("  keep intentional spacing  ");
        var boolean = DynamicInputValue.Boolean(true);
        var number = DynamicInputValue.WholeNumber(42);

        Assert.True(text.IsValid);
        Assert.Equal("  keep intentional spacing  ", text.Value.TextValue);
        Assert.True(boolean.Value.BooleanValue);
        Assert.Equal(42, number.Value.WholeNumberValue);
        Assert.False(DynamicInputValue.Text(new string('x', 4_097)).IsValid);
        Assert.False(DynamicInputValue.Text("token=not-safe").IsValid);
    }

    [Fact]
    public void DraftSnapshotsValuesAndRejectsSensitiveKeys()
    {
        var values = new Dictionary<string, DynamicInputValue?>
        {
            ["include-tests"] = DynamicInputValue.Boolean(true).Value,
        };
        var features = new List<string?> { "docs" };
        var blueprint = BlueprintReference.Create("sample.local", "1.0.0").Value;

        var draft = ProjectCreationDraft.Create(
            "Client Portal",
            @"D:\Projects",
            "client-portal",
            blueprint,
            values,
            features,
            "none");
        values["include-tests"] = DynamicInputValue.Boolean(false).Value;
        features[0] = "changed";

        Assert.True(draft.IsValid);
        Assert.True(draft.Value.Inputs["include-tests"].BooleanValue);
        Assert.Equal(["docs"], draft.Value.Features.ToArray());
        Assert.True(draft.Value.Git.InitializeRepository);
        Assert.Equal(DevForge.Domain.Projects.GitBranchPolicy.Main, draft.Value.Git.BranchPolicy);
        Assert.False(draft.Value.Git.PublishToGitHub);
        Assert.True(draft.Value.Git.IsPrivate);
        Assert.False(ProjectCreationDraft.Create(
            "Client Portal",
            @"D:\Projects",
            "client-portal",
            blueprint,
            new Dictionary<string, DynamicInputValue?>
            {
                ["githubToken"] = DynamicInputValue.Text("value").Value,
            },
            [],
            "none").IsValid);
    }

    [Fact]
    public void DraftCapturesExactReviewedGitHubIntent()
    {
        var result = ProjectCreationDraft.Create(
            "Client Portal",
            @"D:\Projects",
            "client-portal",
            BlueprintReference.Create("sample.local", "1.0.0").Value,
            [],
            [],
            "none",
            initializeRepository: true,
            branchPolicy: DevForge.Domain.Projects.GitBranchPolicy.MainAndDevelop,
            publishToGitHub: true,
            isPrivate: false,
            githubAccount: "octocat",
            githubRepository: "client-portal");

        Assert.True(result.IsValid);
        Assert.True(result.Value.Git.InitializeRepository);
        Assert.Equal(DevForge.Domain.Projects.GitBranchPolicy.MainAndDevelop, result.Value.Git.BranchPolicy);
        Assert.True(result.Value.Git.PublishToGitHub);
        Assert.False(result.Value.Git.IsPrivate);
        Assert.Equal("octocat", result.Value.Git.GitHubAccount);
        Assert.Equal("client-portal", result.Value.Git.GitHubRepository);
    }

    [Fact]
    public void DraftAggregatesInvalidGitIntentAtTheBoundary()
    {
        var result = ProjectCreationDraft.Create(
            "Client Portal",
            @"D:\Projects",
            "client-portal",
            BlueprintReference.Create("sample.local", "1.0.0").Value,
            [],
            [],
            "none",
            initializeRepository: false,
            branchPolicy: (DevForge.Domain.Projects.GitBranchPolicy)999,
            publishToGitHub: true,
            isPrivate: false,
            githubAccount: "bad--account",
            githubRepository: "../repo");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "git.publish.requires-initialization");
        Assert.Contains(result.Issues, issue => issue.Code == "git.github-account.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "git.github-repository.invalid");
    }

    [Fact]
    public void DraftAggregatesBoundaryIssuesWithoutThrowing()
    {
        var result = ProjectCreationDraft.Create(null, null, null, null, null, null, null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "creation.name.required");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.root.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.output-folder.invalid");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.blueprint.required");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.inputs.required");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.features.required");
        Assert.Contains(result.Issues, issue => issue.Code == "creation.ide.invalid");
    }

    [Theory]
    [InlineData("child\\nested")]
    [InlineData("CON")]
    [InlineData("..")]
    public void DraftRequiresOneGuardedOutputFolderSegment(string value)
    {
        var result = ValidDraft(outputFolder: value);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "creation.output-folder.invalid");
    }

    [Fact]
    public void DraftUsesClosedIdeIdentifiers()
    {
        Assert.True(ValidDraft(ideId: "none").IsValid);
        Assert.True(ValidDraft(ideId: "vscode").IsValid);
        Assert.False(ValidDraft(ideId: "arbitrary-editor").IsValid);
    }

    [Fact]
    public void CreationPortsRequireCancellationTokens()
    {
        Type[] portTypes =
        [
            typeof(IProjectTargetPreflight),
            typeof(IProjectExecutionWorkspaceFactory),
            typeof(IProjectCreationWorkflow),
        ];

        Assert.All(
            portTypes.SelectMany(type => type.GetMethods()),
            method => Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType));
    }

    [Fact]
    public void DraftCollectionsAreImmutableSnapshots()
    {
        var result = ValidDraft();

        Assert.True(result.IsValid);
        Assert.IsType<ImmutableSortedDictionary<string, DynamicInputValue>>(result.Value.Inputs);
        Assert.IsType<ImmutableArray<string>>(result.Value.Features);
    }

    [Fact]
    public void CreationSnapshotsCannotBypassGuardedFactories()
    {
        Type[] snapshotTypes =
        [
            typeof(ProjectTargetDescriptor),
            typeof(ProjectExecutionWorkspaces),
            typeof(ProjectCreationPlanSnapshot),
            typeof(ProjectCreationExecutionSnapshot),
            typeof(ProjectCreationPresetDraft),
        ];

        Assert.All(snapshotTypes, type => Assert.Empty(type.GetConstructors()));
    }

    private static DevForge.Domain.Validation.ValidationResult<ProjectCreationDraft> ValidDraft(
        string outputFolder = "client-portal",
        string ideId = "none")
    {
        return ProjectCreationDraft.Create(
            "Client Portal",
            @"D:\Projects",
            outputFolder,
            BlueprintReference.Create("sample.local", "1.0.0").Value,
            new Dictionary<string, DynamicInputValue?>(),
            [],
            ideId);
    }
}
