using DevForge.Application.Planning.CompatibilityRules;
using DevForge.Blueprints.Abstractions.Models;

namespace DevForge.UnitTests.Application.Planning;

public sealed class CompatibilityRuleEngineTests
{
    [Theory]
    [InlineData("runtime.os == \"windows\" || runtime.arch == \"arm64\" && engine.version satisfies \">=10.0.0 <11.0.0\"", true)]
    [InlineData("(runtime.os == \"windows\" || runtime.arch == \"arm64\") && engine.version satisfies \">=11.0.0\"", false)]
    [InlineData("runtime.os != \"linux\"", true)]
    [InlineData("team.package-manager in [\"nuget\", \"npm\"]", true)]
    [InlineData("team.package-manager not-in [\"pnpm\", \"npm\"]", true)]
    [InlineData("recipe.input.retries == 3", true)]
    [InlineData("recipe.feature.api == true", true)]
    public void ParserAndEvaluatorHonorPrecedenceOperatorsAndTypedLiterals(
        string expression,
        bool expected)
    {
        var parsed = new CompatibilityRuleParser().Parse(expression);

        Assert.True(parsed.IsValid);
        var evaluated = new CompatibilityRuleEvaluator().Evaluate(
            parsed.Value,
            ValidContext(),
            CancellationToken.None);

        Assert.True(evaluated.IsValid);
        Assert.Equal(expected, evaluated.Value);
    }

    [Theory]
    [InlineData("runtime.os")]
    [InlineData("runtime.arch")]
    [InlineData("engine.version")]
    [InlineData("blueprint.id")]
    [InlineData("blueprint.version")]
    [InlineData("recipe.input.project-name")]
    [InlineData("recipe.feature.api")]
    [InlineData("team.package-manager")]
    [InlineData("git.branch-policy")]
    [InlineData("tool.dotnet.available")]
    [InlineData("tool.dotnet.version")]
    public void ParserAcceptsOnlyTheFixedIdentifierCatalog(string identifier)
    {
        var comparison = identifier.EndsWith(".available", StringComparison.Ordinal)
            || identifier.StartsWith("recipe.feature.", StringComparison.Ordinal)
                ? " == true"
                : identifier.EndsWith(".version", StringComparison.Ordinal)
                    || identifier == "engine.version"
                        ? " satisfies \">=1.0.0\""
                        : " == \"value\"";

        Assert.True(new CompatibilityRuleParser().Parse(identifier + comparison).IsValid);
    }

    [Theory]
    [InlineData("environment.PATH == \"x\"")]
    [InlineData("runtime.os.GetType == \"x\"")]
    [InlineData("env(\"PATH\") == \"x\"")]
    [InlineData("runtime.os = \"windows\"")]
    [InlineData("runtime.os =~ \"win.*\"")]
    [InlineData("runtime.os == /win.*/")]
    [InlineData("runtime.os == ${HOME}")]
    [InlineData("runtime.os == \"windows\" trailing")]
    [InlineData("runtime.os == \"unterminated")]
    [InlineData("runtime.os == [\"windows\",]")]
    [InlineData("runtime.os == []")]
    public void ParserRejectsForbiddenOrMalformedSyntax(string expression)
    {
        var result = new CompatibilityRuleParser().Parse(expression);

        Assert.False(result.IsValid);
        Assert.Equal("DF-PLAN-001", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void ParserEnforcesInputTokenDepthAndListBounds()
    {
        var tooLong = new string('x', CompatibilityRuleParser.MaximumInputCharacters + 1);
        var tooManyTokens = string.Join(
            " && ",
            Enumerable.Repeat("runtime.os == \"windows\"", CompatibilityRuleParser.MaximumTokens));
        var tooDeep = new string('(', CompatibilityRuleParser.MaximumDepth + 1)
            + "runtime.os == \"windows\""
            + new string(')', CompatibilityRuleParser.MaximumDepth + 1);
        var tooManyItems = "recipe.input.retries in ["
            + string.Join(',', Enumerable.Range(0, CompatibilityRuleParser.MaximumListItems + 1))
            + "]";

        Assert.False(new CompatibilityRuleParser().Parse(tooLong).IsValid);
        Assert.False(new CompatibilityRuleParser().Parse(tooManyTokens).IsValid);
        Assert.False(new CompatibilityRuleParser().Parse(tooDeep).IsValid);
        Assert.False(new CompatibilityRuleParser().Parse(tooManyItems).IsValid);
    }

    [Fact]
    public void EvaluatorRejectsMissingContextTypeMismatchAndInvalidSemanticRange()
    {
        var parser = new CompatibilityRuleParser();
        var evaluator = new CompatibilityRuleEvaluator();
        var missing = PlanningRuleContext.Create(
        [
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "runtime.os",
                PlanningRuleValue.FromText("windows").Value),
        ]).Value;

        var missingResult = evaluator.Evaluate(
            parser.Parse("runtime.arch == \"x64\"").Value,
            missing,
            CancellationToken.None);
        var typeResult = evaluator.Evaluate(
            parser.Parse("engine.version == 10").Value,
            ValidContext(),
            CancellationToken.None);
        var rangeResult = evaluator.Evaluate(
            parser.Parse("engine.version satisfies \"latest\"").Value,
            ValidContext(),
            CancellationToken.None);

        Assert.Equal("DF-PLAN-001", Assert.Single(missingResult.Issues).Code);
        Assert.Equal("DF-PLAN-001", Assert.Single(typeResult.Issues).Code);
        Assert.Equal("DF-PLAN-001", Assert.Single(rangeResult.Issues).Code);
    }

    [Fact]
    public void SemanticVersionEqualityUsesPrecedenceAndIgnoresBuildMetadata()
    {
        var context = ValidContext(
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "engine.version",
                PlanningRuleValue.FromSemanticVersion("10.0.0+machine-a").Value));
        var expression = new CompatibilityRuleParser().Parse(
            "engine.version == blueprint.version").Value;

        var result = new CompatibilityRuleEvaluator().Evaluate(
            expression,
            context,
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.True(result.Value);
    }

    [Fact]
    public void RuleSetEvaluationPreservesFindingOrderAndSeparatesBlockingFromWarnings()
    {
        var rules = new List<CompatibilityRule?>
        {
            Rule("warning-two", "runtime.os == \"linux\"", CompatibilityRuleSeverity.Warning),
            Rule("blocking-one", "runtime.arch == \"arm64\"", CompatibilityRuleSeverity.Blocking),
            Rule("warning-one", "team.package-manager == \"npm\"", CompatibilityRuleSeverity.Warning),
        };

        var result = new CompatibilityRuleEvaluator().EvaluateRules(
            rules,
            ValidContext(),
            CancellationToken.None);
        rules.Clear();

        Assert.True(result.IsValid);
        Assert.False(result.Value.IsCompatible);
        Assert.Equal(
            ["warning-two", "blocking-one", "warning-one"],
            result.Value.Findings.Select(item => item.RuleId));
        Assert.Equal("blocking-one", Assert.Single(result.Value.BlockingFailures).RuleId);
        Assert.Equal(["warning-two", "warning-one"], result.Value.Warnings.Select(item => item.RuleId));
    }

    [Fact]
    public void EvaluationHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expression = new CompatibilityRuleParser().Parse("runtime.os == \"windows\"").Value;

        Assert.Throws<OperationCanceledException>(() =>
            new CompatibilityRuleEvaluator().Evaluate(
                expression,
                ValidContext(),
                cancellation.Token));
    }

    [Fact]
    public void ContextSnapshotsOnceAndRejectsUnknownDuplicateOrWronglyTypedIdentifiers()
    {
        var entries = new SingleUseEnumerable<KeyValuePair<string, PlanningRuleValue?>>(
        [
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "runtime.os",
                PlanningRuleValue.FromText("windows").Value),
        ]);

        var valid = PlanningRuleContext.Create(entries);
        var unknown = PlanningRuleContext.Create(
        [
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "runtime.path",
                PlanningRuleValue.FromText("value").Value),
        ]);
        var duplicate = PlanningRuleContext.Create(
        [
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "runtime.os",
                PlanningRuleValue.FromText("windows").Value),
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "runtime.os",
                PlanningRuleValue.FromText("linux").Value),
        ]);
        var wrongKind = PlanningRuleContext.Create(
        [
            KeyValuePair.Create<string, PlanningRuleValue?>(
                "tool.dotnet.available",
                PlanningRuleValue.FromText("yes").Value),
        ]);

        Assert.True(valid.IsValid);
        Assert.Equal(1, entries.EnumerationCount);
        Assert.False(unknown.IsValid);
        Assert.False(duplicate.IsValid);
        Assert.False(wrongKind.IsValid);
    }

    [Fact]
    public async Task ParserCanBeReusedSafelyByConcurrentCatalogValidation()
    {
        var parser = new CompatibilityRuleParser();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 128).Select(index => Task.Run(() =>
        {
            start.Wait();
            var expression = index % 2 == 0
                ? "runtime.os == \"windows\" && tool.dotnet.available == true"
                : "engine.version satisfies \">=10.0.0 <11.0.0\"";
            return parser.Parse(expression);
        })).ToArray();

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.IsValid));
    }

    private static CompatibilityRule Rule(
        string id,
        string expression,
        CompatibilityRuleSeverity severity)
    {
        return new CompatibilityRule(
            id,
            expression,
            severity,
            "Compatibility requirement was not satisfied.",
            "Choose a compatible option.",
            CompatibilityRuleOverride.None);
    }

    private static PlanningRuleContext ValidContext(
        params KeyValuePair<string, PlanningRuleValue?>[] overrides)
    {
        var values = new Dictionary<string, PlanningRuleValue?>(StringComparer.Ordinal)
        {
            ["runtime.os"] = PlanningRuleValue.FromText("windows").Value,
            ["runtime.arch"] = PlanningRuleValue.FromText("x64").Value,
            ["engine.version"] = PlanningRuleValue.FromSemanticVersion("10.0.0").Value,
            ["blueprint.id"] = PlanningRuleValue.FromText("sample.blueprint").Value,
            ["blueprint.version"] = PlanningRuleValue.FromSemanticVersion("10.0.0+machine-b").Value,
            ["recipe.input.project-name"] = PlanningRuleValue.FromText("value").Value,
            ["recipe.input.retries"] = PlanningRuleValue.FromInteger(3),
            ["recipe.feature.api"] = PlanningRuleValue.FromBoolean(true),
            ["team.package-manager"] = PlanningRuleValue.FromText("nuget").Value,
            ["git.branch-policy"] = PlanningRuleValue.FromText("main").Value,
            ["tool.dotnet.available"] = PlanningRuleValue.FromBoolean(true),
            ["tool.dotnet.version"] = PlanningRuleValue.FromSemanticVersion("10.0.0").Value,
        };
        foreach (var item in overrides)
        {
            values[item.Key] = item.Value;
        }

        return PlanningRuleContext.Create(values).Value;
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The source was enumerated more than once.");
            }

            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
