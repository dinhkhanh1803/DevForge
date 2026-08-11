using System.Collections.Immutable;
using System.Globalization;
using DevForge.Application.Contracts;
using DevForge.Application.Planning;
using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Environment;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;

namespace DevForge.UnitTests.Application.Planning;

public sealed class ProjectPlannerTests
{
    [Fact]
    public async Task CreatesOrderedExecutablePlanAndPrivacySafePreview()
    {
        var blueprint = CreateBlueprint();
        var catalog = new StubCatalog(blueprint);
        var doctor = new StubDoctor(Environment("10.0.302", DateTimeOffset.Parse("2026-08-11T10:00:00Z", CultureInfo.InvariantCulture)));
        var planner = CreatePlanner(catalog, doctor);

        var result = await planner.CreatePlanAsync(CreateRecipe("C:\\one"), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(["create", "render"], result.Value.Plan.Steps.Select(step => step.Id));
        Assert.Equal("validate", Assert.Single(result.Value.Plan.Validators).Id);
        Assert.Equal(["create", "render"], result.Value.Preview.Steps.Select(step => step.Id));
        Assert.Equal("validate", Assert.Single(result.Value.Preview.Validators).Id);
        Assert.Equal("dotnet", Assert.Single(result.Value.Preview.RequiredTools).Id);
        var toolStatus = Assert.Single(result.Value.Preview.ToolStatuses);
        Assert.True(toolStatus.IsAvailable);
        Assert.True(toolStatus.IsCompatible);
        Assert.Equal("10.0.302", toolStatus.DetectedVersion);
        Assert.Equal("hosting", Assert.Single(result.Value.Preview.Dependencies).Id);
        Assert.Equal("src\\App.csproj", Assert.Single(result.Value.Preview.Artifacts).Path);
        Assert.Equal("net10.0", result.Value.Preview.EffectiveInputs["framework"].StringValue);
        Assert.Equal(["tests"], result.Value.Preview.EnabledFeatures.ToArray());
        Assert.True(result.Value.Preview.Git.InitializeRepository);
        Assert.Equal("main", result.Value.Preview.Git.PrimaryBranch);
        Assert.True(result.Value.Preview.Completion.WriteGenerationReport);
        Assert.Null(result.Value.Preview.Steps[0].ProcessPreview);
        Assert.Equal(
            "validate-command: process arguments redacted",
            Assert.Single(result.Value.Preview.Validators).ProcessPreview?.Value);
        Assert.StartsWith("sha256:", result.Value.Preview.PlanHash, StringComparison.Ordinal);
        Assert.Equal(
            "sha256:69b15acea75d45d2129e6b8aa43fde136a362a44ba5486efa37fff51f671677d",
            result.Value.Preview.PlanHash);
        Assert.Equal(result.Value.Preview.PlanHash, result.Value.Plan.Id);
        Assert.Equal("Sample App", result.Value.Plan.TemplateContext["project.name"]);
        Assert.Equal("sample-app", result.Value.Plan.TemplateContext["project.safe_name"]);
        Assert.Equal("net10.0", result.Value.Plan.TemplateContext["recipe.input.framework"]);
        Assert.Equal("true", result.Value.Plan.TemplateContext["recipe.feature.tests"]);
        Assert.DoesNotContain("runtime.staging-path", result.Value.Plan.TemplateContext.Keys);
        Assert.DoesNotContain("project.target-path", result.Value.Plan.TemplateContext.Keys);
        Assert.Equal(1, catalog.FindCalls);
        Assert.Equal(1, doctor.InspectCalls);
    }

    [Fact]
    public async Task CanonicalHashIgnoresCultureTargetRootDetectionTimestampAndWarnings()
    {
        var firstBlueprint = CreateBlueprint(warningMessage: "First warning.");
        var secondBlueprint = CreateBlueprint(warningMessage: "Different warning text.");
        var first = CreatePlanner(
            new StubCatalog(firstBlueprint),
            new StubDoctor(Environment("10.0.302", DateTimeOffset.Parse("2025-01-01T00:00:00Z", CultureInfo.InvariantCulture))));
        var second = CreatePlanner(
            new StubCatalog(secondBlueprint),
            new StubDoctor(Environment("10.0.999", DateTimeOffset.Parse("2030-01-01T00:00:00Z", CultureInfo.InvariantCulture))));
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var firstResult = await first.CreatePlanAsync(CreateRecipe("C:\\machine-one"), CancellationToken.None);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var secondResult = await second.CreatePlanAsync(CreateRecipe("D:\\machine-two"), CancellationToken.None);

            Assert.True(firstResult.IsValid);
            Assert.True(secondResult.IsValid);
            Assert.Equal(firstResult.Value.Preview.PlanHash, secondResult.Value.Preview.PlanHash);
            Assert.NotEqual(
                firstResult.Value.Preview.ToolStatuses.Single().DetectedVersion,
                secondResult.Value.Preview.ToolStatuses.Single().DetectedVersion);
            Assert.NotEqual(
                firstResult.Value.Preview.Warnings.Single().Message,
                secondResult.Value.Preview.Warnings.Single().Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task CanonicalHashChangesForEffectBearingValuesAndOrderedActions()
    {
        var baseline = await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root"));
        var changedInput = await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root", framework: "net10.1"));
        var reversed = await PlanHashAsync(CreateBlueprint(reverseActions: true), CreateRecipe("C:\\root"));
        var changedProjectName = await PlanHashAsync(
            CreateBlueprint(),
            CreateRecipe("C:\\root", projectName: "Different App"));

        Assert.NotEqual(baseline, changedInput);
        Assert.NotEqual(baseline, reversed);
        Assert.NotEqual(baseline, changedProjectName);
    }

    [Fact]
    public async Task CanonicalHashSortsObjectKeysButCoversAllStructuralPolicies()
    {
        var baseline = await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root"));
        var reorderedMap = await PlanHashAsync(
            CreateBlueprint(reverseParameterInsertion: true),
            CreateRecipe("C:\\root"));
        Assert.Equal(baseline, reorderedMap);

        var mutations = new[]
        {
            await PlanHashAsync(CreateBlueprint(checksumCharacter: 'b'), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(toolRange: ">=10.0.0 <12.0.0"), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(dependencyVersion: "10.1.0"), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(artifactPath: "src\\Other.csproj"), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(validatorTimeoutSeconds: 31), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(actionTimeoutSeconds: 11), CreateRecipe("C:\\root")),
            await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root", useDevelop: true)),
            await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root", openIde: true)),
            await PlanHashAsync(CreateBlueprint(), CreateRecipe("C:\\root", teamCompany: "Contoso")),
        };

        Assert.All(mutations, hash => Assert.NotEqual(baseline, hash));
        Assert.Equal(mutations.Length, mutations.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task OptionalMissingToolIsPreviewedWithoutBlockingPlanning()
    {
        var planner = CreatePlanner(
            new StubCatalog(CreateBlueprint(toolRequired: false)),
            new StubDoctor(Environment(null, DateTimeOffset.UtcNow, available: false)));

        var result = await planner.CreatePlanAsync(CreateRecipe("C:\\root"), CancellationToken.None);

        Assert.True(result.IsValid);
        var status = Assert.Single(result.Value.Preview.ToolStatuses);
        Assert.False(status.Required);
        Assert.False(status.IsAvailable);
        Assert.False(status.IsCompatible);
        Assert.Null(status.DetectedVersion);
    }

    [Fact]
    public async Task ConcurrentPlanningPublishesTheSameDeterministicHash()
    {
        var planner = CreatePlanner(
            new StubCatalog(CreateBlueprint()),
            new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow)));

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            planner.CreatePlanAsync(CreateRecipe("C:\\root"), CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.IsValid));
        Assert.Single(results.Select(result => result.Value.Preview.PlanHash).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RejectsSecretShapedProjectAndTeamValuesWithoutThrowing()
    {
        var planner = CreatePlanner(
            new StubCatalog(CreateBlueprint()),
            new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow)));

        var projectResult = await planner.CreatePlanAsync(
            CreateRecipe("C:\\root", projectName: "token=abcdefgh"),
            CancellationToken.None);
        var teamResult = await planner.CreatePlanAsync(
            CreateRecipe("C:\\root", teamCompany: "password=abcdefgh"),
            CancellationToken.None);

        Assert.False(projectResult.IsValid);
        Assert.False(teamResult.IsValid);
        Assert.All(projectResult.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
        Assert.All(teamResult.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
    }

    [Fact]
    public async Task RejectsNullRecipeAndUnsafeCompletionIntentAsGuardedResults()
    {
        var planner = CreatePlanner(
            new StubCatalog(CreateBlueprint()),
            new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow)));

        var nullResult = await planner.CreatePlanAsync(null!, CancellationToken.None);
        var completion = CompletionOptions.Create(openIde: true, ideId: "token=abcdefgh").Value;
        var draft = CreateRecipeDraft("C:\\root") with { Completion = completion };
        var unsafeResult = await planner.CreatePlanAsync(
            ProjectRecipe.Create(draft).Value,
            CancellationToken.None);

        Assert.False(nullResult.IsValid);
        Assert.False(unsafeResult.IsValid);
        Assert.All(nullResult.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
        Assert.All(unsafeResult.Issues, issue => Assert.Equal("DF-PLAN-001", issue.Code));
    }

    [Fact]
    public async Task AggregatesIndependentEngineToolInputAndBlockingRuleFailures()
    {
        var blueprint = CreateBlueprint(
            engineRange: ">=99.0.0",
            blockingExpression: "runtime.os == 'linux'");
        var recipe = CreateRecipe("C:\\root", framework: "", extraInput: true);
        var planner = CreatePlanner(
            new StubCatalog(blueprint),
            new StubDoctor(Environment(null, DateTimeOffset.UtcNow, available: false)));

        var result = await planner.CreatePlanAsync(recipe, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.True(result.Issues.Count(issue => issue.Code == "DF-PLAN-001") >= 4);
        Assert.Contains(result.Issues, issue => issue.Location == "engine.version");
        Assert.Contains(result.Issues, issue => issue.Location == "tools.dotnet");
        Assert.Contains(result.Issues, issue => issue.Location == "inputs.extra");
        Assert.Contains(result.Issues, issue => issue.Location == "rules.windows-only");
    }

    [Fact]
    public async Task MissingExactBlueprintFailsWithoutInspectingEnvironment()
    {
        var doctor = new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow));
        var planner = CreatePlanner(new StubCatalog(null), doctor);

        var result = await planner.CreatePlanAsync(CreateRecipe("C:\\root"), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("DF-PLAN-001", Assert.Single(result.Issues).Code);
        Assert.Equal(0, doctor.InspectCalls);
    }

    [Fact]
    public async Task PropagatesCancellationBetweenPlannerStages()
    {
        using var source = new CancellationTokenSource();
        var catalog = new StubCatalog(CreateBlueprint(), () => source.Cancel());
        var planner = CreatePlanner(catalog, new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planner.CreatePlanAsync(CreateRecipe("C:\\root"), source.Token));
    }

    private static ProjectPlanner CreatePlanner(IBlueprintCatalog catalog, IEnvironmentDoctor doctor)
    {
        var runtime = PlanningRuntimeContext.Create("1.5.0", "windows", "x64").Value;
        return new ProjectPlanner(
            catalog,
            doctor,
            new FixedRuntimeProvider(runtime),
            new InputSchemaValidator(),
            new CompatibilityRuleEvaluator(),
            new VariableTemplateResolver());
    }

    private static async Task<string> PlanHashAsync(ResolvedBlueprint blueprint, ProjectRecipe recipe)
    {
        var result = await CreatePlanner(
            new StubCatalog(blueprint),
            new StubDoctor(Environment("10.0.302", DateTimeOffset.UtcNow)))
            .CreatePlanAsync(recipe, CancellationToken.None);
        Assert.True(result.IsValid);
        return result.Value.Preview.PlanHash;
    }

    private static ProjectRecipe CreateRecipe(
        string targetPath,
        string framework = "net10.0",
        bool extraInput = false,
        bool useDevelop = false,
        bool openIde = false,
        string? teamCompany = null,
        string projectName = "Sample App")
    {
        var draft = CreateRecipeDraft(
            targetPath,
            framework,
            extraInput,
            useDevelop,
            openIde,
            teamCompany,
            projectName);
        return ProjectRecipe.Create(draft).Value;
    }

    private static ProjectRecipeDraft CreateRecipeDraft(
        string targetPath,
        string framework = "net10.0",
        bool extraInput = false,
        bool useDevelop = false,
        bool openIde = false,
        string? teamCompany = null,
        string projectName = "Sample App")
    {
        var inputs = new Dictionary<string, string?>
        {
            ["framework"] = framework,
        };
        if (extraInput)
        {
            inputs["extra"] = "value";
        }

        var git = GitOptions.Create(useDevelopBranch: useDevelop).Value;
        var completion = CompletionOptions.Create(openIde: openIde, ideId: openIde ? "visual-studio" : null).Value;
        var team = teamCompany is null
            ? null
            : TeamProfile.Create(
                "team",
                "Team",
                [KeyValuePair.Create<string, string?>("company-name", teamCompany)]).Value;
        return new ProjectRecipeDraft(
            projectName,
            targetPath,
            "desktop.csharp-wpf-tool",
            "1.0.0",
            inputs,
            ["tests"],
            team,
            git,
            completion);
    }

    private static ResolvedBlueprint CreateBlueprint(
        string engineRange = ">=1.0.0 <2.0.0",
        string blockingExpression = "runtime.os == 'windows'",
        string warningMessage = "Warning one.",
        bool reverseActions = false,
        bool reverseParameterInsertion = false,
        char checksumCharacter = 'a',
        string toolRange = ">=10.0.0 <11.0.0",
        string dependencyVersion = "10.0.0",
        string artifactPath = "src\\App.csproj",
        int validatorTimeoutSeconds = 30,
        int actionTimeoutSeconds = 10,
        bool toolRequired = true)
    {
        var text = BlueprintValue.FromString("{{ recipe.input.framework }}").Value;
        var path = BlueprintValue.FromString("src").Value;
        var target = BlueprintValue.FromString("src\\App.csproj").Value;
        var renderParameters = reverseParameterInsertion
            ? ImmutableDictionary.CreateRange<string, BlueprintValue>(
            [
                KeyValuePair.Create("target", target),
                KeyValuePair.Create("source", text),
            ])
            : ImmutableDictionary.CreateRange<string, BlueprintValue>(
            [
                KeyValuePair.Create("source", text),
                KeyValuePair.Create("target", target),
            ]);
        var actions = new[]
        {
            new BlueprintActionDefinition(
                "create",
                "create-directory",
                ImmutableDictionary<string, BlueprintValue>.Empty.Add("path", path),
                TimeSpan.FromSeconds(5)),
            new BlueprintActionDefinition(
                "render",
                "render-template",
                renderParameters,
                TimeSpan.FromSeconds(actionTimeoutSeconds)),
        };
        if (reverseActions)
        {
            Array.Reverse(actions);
        }

        var validator = new ValidatorDefinition(
            "validate",
            "validate-command",
            TimeSpan.FromSeconds(validatorTimeoutSeconds),
            ImmutableDictionary<string, BlueprintValue>.Empty
                .Add("required", BlueprintValue.FromBoolean(true)),
            required: true);
        var manifest = BlueprintManifest.Create(
            new BlueprintManifestDraft(
                "desktop.csharp-wpf-tool",
                "1.0.0",
                engineRange,
                [new ToolRequirement("dotnet", toolRange, toolRequired)],
                [new InputDefinition("framework", BlueprintInputKind.Text, true, "net10.0")],
                [
                    new CompatibilityRule(
                        "windows-only",
                        blockingExpression,
                        CompatibilityRuleSeverity.Blocking,
                        "Windows is required."),
                    new CompatibilityRule(
                        "warning-one",
                        "recipe.feature.tests == false",
                        CompatibilityRuleSeverity.Warning,
                        warningMessage),
                ],
                [],
                [validator],
                Features: [new BlueprintFeatureDefinition("tests", false)],
                Actions: actions,
                Dependencies: [new BlueprintDependency("hosting", dependencyVersion)],
                Artifacts: [new BlueprintArtifact(artifactPath)]),
            new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
        var schema = BlueprintInputPropertyDefinition.Create(new BlueprintInputPropertyDraft(
            "framework",
            BlueprintInputKind.Text,
            true,
            BlueprintValue.FromString("net10.0").Value,
            [],
            1,
            40,
            null,
            null)).Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            WorkspaceRelativePath.Create("desktop.csharp-wpf-tool\\1.0.0").Value,
            BlueprintTrust.BuiltIn,
            $"sha256:{new string(checksumCharacter, 64)}").Value;
        return ResolvedBlueprint.Create(manifest, [schema], fingerprint).Value;
    }

    private static EnvironmentSnapshot Environment(
        string? version,
        DateTimeOffset capturedAt,
        bool available = true)
    {
        return EnvironmentSnapshot.Create(
            capturedAt,
            [new EnvironmentTool("dotnet", version, available)],
            []).Value;
    }

    private sealed class StubCatalog(ResolvedBlueprint? blueprint, Action? afterFind = null) : IBlueprintCatalog
    {
        public int FindCalls { get; private set; }

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<BlueprintCatalogSnapshot> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(BlueprintCatalogSnapshot.Create([], []).Value);

        public Task<ImmutableArray<ResolvedBlueprint>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(blueprint is null ? ImmutableArray<ResolvedBlueprint>.Empty : [blueprint]);

        public Task<ResolvedBlueprint?> FindAsync(BlueprintReference reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            afterFind?.Invoke();
            return Task.FromResult(blueprint);
        }
    }

    private sealed class StubDoctor(EnvironmentSnapshot snapshot) : IEnvironmentDoctor
    {
        public int InspectCalls { get; private set; }

        public Task<EnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FixedRuntimeProvider(PlanningRuntimeContext context) : IPlanningRuntimeContextProvider
    {
        public PlanningRuntimeContext GetCurrent() => context;
    }
}
