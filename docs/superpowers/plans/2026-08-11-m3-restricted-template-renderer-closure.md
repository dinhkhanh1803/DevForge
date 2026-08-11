# M3 Restricted Template Renderer Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the missing M3 exit item by implementing a bounded, deterministic, privacy-safe Scriban renderer behind `ITemplateRenderer`.

**Architecture:** Application owns the immutable validated request and remains package-independent. Infrastructure alone references Scriban, validates a closed AST subset before evaluation, creates a fresh read-only string-only runtime for every render, and writes through a bounded cancellation-aware output. Expected failures cross the boundary only as stable scrubbed `InfrastructureOperationException` codes.

**Tech Stack:** .NET SDK 10.0.302, C# 14, Scriban 7.2.5, xUnit 2.9.3, central package management, NuGet lock files.

---

## Approved source and scope

- Source specification: `docs/DevForge_Studio_Codex_Implementation_Specification_V1.0.docx` and its Markdown companion.
- Approved design: `docs/superpowers/specs/2026-08-10-m3-restricted-template-renderer-closure-design.md`.
- Existing port: `ITemplateRenderer.RenderAsync(TemplateRenderRequest, CancellationToken)`.
- This plan adds no catalog loading, blueprint content, planner rule, plan hashing, orchestration, filesystem access, process execution, WPF UI, Git, GitHub, cloud backend, or AI API.

## File responsibility map

### Production files

- Modify `src/DevForge.Domain/Privacy/RedactedText.cs`: expose the existing bounded credential-shape predicate without converting raw values to trusted redacted text.
- Modify `src/DevForge.Application/Contracts/BlueprintContracts.cs`: remove the renderer request and port after their focused extraction.
- Create `src/DevForge.Application/Contracts/TemplateRendererContracts.cs`: own request bounds, validation, immutable snapshots, and `ITemplateRenderer`.
- Modify `Directory.Packages.props`: pin Scriban exactly at 7.2.5.
- Modify `src/DevForge.Infrastructure/DevForge.Infrastructure.csproj`: add the unversioned Scriban package reference.
- Modify `src/DevForge.Infrastructure/packages.lock.json`: generated lock update for Scriban and its resolved graph.
- Modify dependent lock files only when `dotnet restore --force-evaluate` proves their graph changed.
- Create `src/DevForge.Infrastructure/Templates/TemplateRenderFailures.cs`: internal marker exceptions and stable public-boundary error codes.
- Create `src/DevForge.Infrastructure/Templates/BoundedTemplateOutput.cs`: bounded `IScriptOutput` implementation with cancellation.
- Create `src/DevForge.Infrastructure/Templates/RestrictedTemplateContextFactory.cs`: fresh empty-builtins context and frozen nested `ScriptObject` graph.
- Create `src/DevForge.Infrastructure/Templates/RestrictedTemplatePolicy.cs`: closed semantic AST validator with node/depth limits.
- Create `src/DevForge.Infrastructure/Templates/RestrictedScribanTemplateRenderer.cs`: parse, policy, runtime, render, cancellation, and scrubbed error mapping.

### Test files

- Modify `tests/DevForge.UnitTests/Domain/PrivacyTests.cs`: cover the public credential-shape predicate and safe false positives.
- Create `tests/DevForge.UnitTests/Application/TemplateRenderRequestTests.cs`: exhaustive request aggregation, bounds, normalization, and snapshot tests.
- Modify `tests/DevForge.UnitTests/Application/RequestContractTests.cs`: keep only the general port/request smoke coverage after extraction.
- Create `tests/DevForge.UnitTests/Architecture/TemplateRendererDependencyTests.cs`: enforce exact package ownership and absence from other projects.
- Create `tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplateRendererTests.cs`: permitted rendering, determinism, concurrency, and culture.
- Create `tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplatePolicyTests.cs`: allowed conditional grammar and forbidden AST families.
- Create `tests/DevForge.IntegrationTests/Infrastructure/Templates/TemplateRendererSecurityTests.cs`: bounds, cancellation, stable codes, and non-leakage.

### Documentation files

- Create `docs/decisions/0006-restricted-scriban-template-runtime.md`.
- Modify `docs/superpowers/specs/2026-08-10-m3-core-infrastructure-design.md`.
- Modify `docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md`.
- Modify `docs/implementation-plan.md`.
- Modify `docs/implementation-status.md`.
- Modify `CHANGELOG.md`.

## Stable validation and runtime codes

Request validation uses these exact codes:

- `template.value.required`
- `template.value.too-large`
- `template.value.null-character`
- `template.context.required`
- `template.context.too-many`
- `template.context.name.required`
- `template.context.name.too-long`
- `template.context.name.invalid`
- `template.context.name.duplicate`
- `template.context.name.path-conflict`
- `template.context.name.secret-shaped`
- `template.context.value.required`
- `template.context.value.too-large`
- `template.context.value.null-character`
- `template.context.value.secret-shaped`
- `template.context.total.too-large`

Runtime failures use these exact codes and fixed messages:

| Code | Fixed safe message |
| --- | --- |
| `template.parse.invalid` | `The template syntax is invalid.` |
| `template.policy.forbidden` | `The template uses a forbidden construct.` |
| `template.variable.missing` | `A required template variable is missing.` |
| `template.output.too-large` | `The rendered template exceeds the output limit.` |
| `template.render.failed` | `The template could not be rendered safely.` |

## Task 1: Harden and extract the Application render request

**Files:**
- Modify: `src/DevForge.Domain/Privacy/RedactedText.cs`
- Modify: `src/DevForge.Application/Contracts/BlueprintContracts.cs`
- Create: `src/DevForge.Application/Contracts/TemplateRendererContracts.cs`
- Modify: `tests/DevForge.UnitTests/Domain/PrivacyTests.cs`
- Create: `tests/DevForge.UnitTests/Application/TemplateRenderRequestTests.cs`
- Modify: `tests/DevForge.UnitTests/Application/RequestContractTests.cs`

- [ ] **Step 1: Write failing privacy-predicate and request-boundary tests**

Cover null collections, single enumeration, exact value preservation, 1 MiB template length, 256 entries, 256-character names, 64 KiB values, 2 MiB aggregate context, null characters, dotted-name grammar, duplicates after trimming, parent/child collisions, secret-shaped names, credential-shaped values, and caller mutation after success. Use boundary values at `limit` and `limit + 1`; assert issue codes, never English message fragments.

```csharp
[Fact]
public void Create_AggregatesMalformedContextWithoutReenumerating()
{
    var context = new SingleUseEnumerable<KeyValuePair<string, string?>>(
    [
        KeyValuePair.Create<string, string?>(" project.name ", "  Example  "),
        KeyValuePair.Create<string, string?>("project", "collision"),
        KeyValuePair.Create<string, string?>("apiToken", "safe"),
        KeyValuePair.Create<string, string?>("bad-name", null),
    ]);

    var result = TemplateRenderRequest.Create("{{ project.name }}", context);

    Assert.False(result.IsValid);
    Assert.Equal(1, context.EnumerationCount);
    Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.path-conflict");
    Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.secret-shaped");
    Assert.Contains(result.Issues, issue => issue.Code == "template.context.name.invalid");
    Assert.Contains(result.Issues, issue => issue.Code == "template.context.value.required");
}

[Fact]
public void IsSecretShapedValue_DetectsCredentialsWithoutRejectingWhitespace()
{
    Assert.True(RedactedText.IsSecretShapedValue("Authorization: Bearer abcdefghijklmnop"));
    Assert.False(RedactedText.IsSecretShapedValue("   "));
    Assert.False(RedactedText.IsSecretShapedValue("The .env file was not read"));
}
```

- [ ] **Step 2: Run focused RED**

Run:

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TemplateRenderRequestTests|FullyQualifiedName~PrivacyTests"
```

Expected: compile failure because `RedactedText.IsSecretShapedValue` and the new request constants/validation do not exist.

- [ ] **Step 3: Expose the existing value-shape predicate without changing redaction semantics**

Add this method and make `FromTrustedRedaction` call it. Empty text remains invalid for `FromTrustedRedaction` but is not credential-shaped.

```csharp
public static bool IsSecretShapedValue(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    return LooksSecretShaped(value);
}
```

- [ ] **Step 4: Extract and implement the guarded request**

Move `TemplateRenderRequest` and `ITemplateRenderer` unchanged in namespace/API identity to `TemplateRendererContracts.cs`. Define these public bounds:

```csharp
public const int MaxTemplateLength = 1024 * 1024;
public const int MaxContextEntries = 256;
public const int MaxContextNameLength = 256;
public const int MaxContextValueLength = 64 * 1024;
public const int MaxTotalContextValueLength = 2 * 1024 * 1024;
```

Snapshot once, validate the snapshot, and construct only from the same validated snapshot:

```csharp
var snapshot = context?.ToImmutableArray() ?? [];
var issues = new List<ValidationIssue>();
ValidateTemplate(template, issues);

if (context is null)
{
    issues.Add(new ValidationIssue(
        "template.context.required",
        "A template context is required.",
        "context"));
}
else
{
    ValidateContext(snapshot, issues);
}

if (issues.Count != 0)
{
    return ValidationResult.Failure<TemplateRenderRequest>(issues);
}

var immutableContext = snapshot
    .Select(pair => KeyValuePair.Create(pair.Key.Trim(), pair.Value!))
    .ToImmutableDictionary(StringComparer.Ordinal);
return ValidationResult.Success(new TemplateRenderRequest(template!, immutableContext));
```

Use the compiled identifier regex `^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$` with a 100 ms invariant timeout. For every normalized name, test ordinal duplication and `RedactedText.IsSecretShapedKey`; after the loop, sort names ordinally and detect an adjacent prefix where the longer name starts with `shorter + "."`. Sum value lengths in `long`, preserve valid values exactly, and call `RedactedText.IsSecretShapedValue` only after the individual value length bound passes.

- [ ] **Step 5: Run focused GREEN and commit**

Run the command from Step 2 twice. Expected: every matching test passes with zero failed and zero skipped.

```powershell
git add src/DevForge.Domain/Privacy/RedactedText.cs src/DevForge.Application/Contracts/BlueprintContracts.cs src/DevForge.Application/Contracts/TemplateRendererContracts.cs tests/DevForge.UnitTests/Domain/PrivacyTests.cs tests/DevForge.UnitTests/Application/RequestContractTests.cs tests/DevForge.UnitTests/Application/TemplateRenderRequestTests.cs
git commit -m "feat(application): harden template render requests"
```

## Task 2: Pin Scriban and protect dependency ownership

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DevForge.Infrastructure/DevForge.Infrastructure.csproj`
- Modify: `src/DevForge.Infrastructure/packages.lock.json`
- Modify: dependent `packages.lock.json` files only if restore changes them
- Create: `tests/DevForge.UnitTests/Architecture/TemplateRendererDependencyTests.cs`

- [ ] **Step 1: Write the failing package-ownership test**

```csharp
public sealed class TemplateRendererDependencyTests
{
    private readonly RepositoryModel _repository = RepositoryModel.LoadFrom(AppContext.BaseDirectory);

    [Fact]
    public void ScribanIsPinnedAndOwnedOnlyByInfrastructure()
    {
        var versions = _repository.CentralPackageVersions
            .Where(package => package.Name.Equals("Scriban", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var owners = _repository.Projects.Values
            .Where(project => project.PackageReferences.Contains("Scriban"))
            .Select(project => project.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal("7.2.5", Assert.Single(versions).Version);
        Assert.Equal(["DevForge.Infrastructure"], owners);
    }

    [Fact]
    public void ScribanAndEffectfulApisStayOutsideOtherProductionLayersAndRenderer()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var productionSources = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.cs",
            SearchOption.AllDirectories);
        var scribanOutsideInfrastructure = productionSources
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}DevForge.Infrastructure{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("Scriban", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();
        var rendererDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "DevForge.Infrastructure",
            "Templates");
        var rendererText = Directory.Exists(rendererDirectory)
            ? string.Concat(Directory.EnumerateFiles(rendererDirectory, "*.cs").Select(File.ReadAllText))
            : string.Empty;
        string[] forbiddenRendererTokens =
        [
            "System.Diagnostics.Process",
            "System.IO.File",
            "System.IO.Directory",
            "HttpClient",
            "Environment.",
            "ILogger",
            "Activator.",
            "System.Reflection",
        ];

        Assert.Empty(scribanOutsideInfrastructure);
        Assert.All(
            forbiddenRendererTokens,
            token => Assert.DoesNotContain(token, rendererText, StringComparison.Ordinal));
    }
}
```

Use the existing repository-root convention:

```csharp
private static string FindRepositoryRoot(string startDirectory)
{
    for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DevForge.sln")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("Could not locate the repository root.");
}
```

- [ ] **Step 2: Run dependency RED**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --filter FullyQualifiedName~TemplateRendererDependencyTests
```

Expected: assertion failure because the central version and Infrastructure package reference are absent.

- [ ] **Step 3: Add the exact central package and Infrastructure-only reference**

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Scriban" Version="7.2.5" />

<!-- src/DevForge.Infrastructure/DevForge.Infrastructure.csproj -->
<PackageReference Include="Scriban" />
```

Do not add `Version`, `VersionOverride`, wildcard, range, or floating metadata to the project reference.

- [ ] **Step 4: Regenerate and verify lock files**

```powershell
..\..\.tools\dotnet\dotnet.exe restore DevForge.sln --force-evaluate --verbosity minimal
..\..\.tools\dotnet\dotnet.exe restore DevForge.sln --locked-mode --verbosity minimal
```

Expected: both commands exit 0. Inspect every changed lock file and confirm Scriban resolves to exactly 7.2.5 with no unexpected package introduced.

- [ ] **Step 5: Run dependency GREEN and commit**

Run the test from Step 2 plus `FullyQualifiedName~CentralPackage`. Expected: all pass.

```powershell
git add Directory.Packages.props src/DevForge.Infrastructure/DevForge.Infrastructure.csproj src/DevForge.Infrastructure/packages.lock.json tests/DevForge.UnitTests/Architecture/TemplateRendererDependencyTests.cs
git add src/DevForge.Application/packages.lock.json tests/DevForge.IntegrationTests/packages.lock.json tests/DevForge.E2ETests/packages.lock.json
git commit -m "build: pin Scriban for Infrastructure rendering"
```

Before staging a dependent lock path, omit it when restore did not change it.

## Task 3: Render variables through a bounded isolated runtime

**Files:**
- Create: `src/DevForge.Infrastructure/Templates/TemplateRenderFailures.cs`
- Create: `src/DevForge.Infrastructure/Templates/BoundedTemplateOutput.cs`
- Create: `src/DevForge.Infrastructure/Templates/RestrictedTemplateContextFactory.cs`
- Create: `src/DevForge.Infrastructure/Templates/RestrictedTemplatePolicy.cs`
- Create: `src/DevForge.Infrastructure/Templates/RestrictedScribanTemplateRenderer.cs`
- Create: `tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplateRendererTests.cs`

- [ ] **Step 1: Write failing scalar, dotted, exact-text, concurrency, and culture tests**

```csharp
[Fact]
public async Task RenderAsync_RendersScalarAndDottedValuesVerbatim()
{
    var request = TemplateRenderRequest.Create(
        "Name={{ project.name }}|Value={{ value }}\r\n",
        [
            KeyValuePair.Create<string, string?>("project.name", "DevForge"),
            KeyValuePair.Create<string, string?>("value", "  exact  "),
        ]).Value;

    var result = await new RestrictedScribanTemplateRenderer()
        .RenderAsync(request, CancellationToken.None);

    Assert.Equal("Name=DevForge|Value=  exact  \r\n", result);
}

[Fact]
public async Task RenderAsync_UsesIndependentContextsConcurrently()
{
    var renderer = new RestrictedScribanTemplateRenderer();
    var tasks = Enumerable.Range(0, 32).Select(index => renderer.RenderAsync(
        TemplateRenderRequest.Create(
            "{{ project.name }}",
            [KeyValuePair.Create<string, string?>("project.name", $"P{index}")]).Value,
        CancellationToken.None));

    Assert.Equal(
        Enumerable.Range(0, 32).Select(index => $"P{index}"),
        await Task.WhenAll(tasks));
}
```

Add one table-driven exact-text test for Unicode, LF, CRLF, blank lines, indentation, and leading/trailing context whitespace. The culture test must save and restore both `CurrentCulture` and `CurrentUICulture` in `finally`, render under `tr-TR` and `en-US`, and assert ordinal-equal output.

- [ ] **Step 2: Run renderer RED**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~RestrictedTemplateRendererTests
```

Expected: compile failure because `RestrictedScribanTemplateRenderer` does not exist.

- [ ] **Step 3: Implement bounded output and safe markers**

`BoundedTemplateOutput` implements both `IScriptOutput.Write` methods, checks both the render token and method token, rejects overflow before appending, and overrides `ToString`:

```csharp
internal sealed class BoundedTemplateOutput(int maximumLength, CancellationToken renderToken)
    : IScriptOutput
{
    private readonly StringBuilder _builder = new(Math.Min(maximumLength, 4096));

    public void Write(string text, int offset, int count)
    {
        renderToken.ThrowIfCancellationRequested();
        if (count < 0 || offset < 0 || offset > text.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (_builder.Length > maximumLength - count)
        {
            throw new TemplateOutputLimitExceededException();
        }

        _builder.Append(text, offset, count);
    }

    public ValueTask WriteAsync(
        string text,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(text, offset, count);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => _builder.ToString();
}
```

Marker exception types are internal, parameterless, have fixed messages, and are never attached as `InnerException` to the public-boundary exception.

- [ ] **Step 4: Build a fresh frozen string-only context**

Construct an empty read-only built-in object, build the nested globals from ordinal-sorted validated paths, then recursively freeze every object. Never call `Import`, never expose a CLR model, and never set a loader.

```csharp
var builtins = new ScriptObject { IsReadOnly = true };
var context = new TemplateContext(builtins, StringComparer.Ordinal)
{
    CancellationToken = cancellationToken,
    StrictVariables = true,
    EnableRelaxedTargetAccess = false,
    EnableRelaxedMemberAccess = false,
    EnableRelaxedFunctionAccess = false,
    EnableRelaxedIndexerAccess = false,
    RecursiveLimit = RestrictedTemplatePolicy.MaximumDepth,
    LimitToString = 0,
    TemplateLoader = null,
};
context.TryGetVariable = static (_, _, _, out object? value) =>
{
    value = null;
    throw new MissingTemplateVariableException();
};
context.TryGetMember = static (_, _, _, _, out object? value) =>
{
    value = null;
    throw new MissingTemplateVariableException();
};
context.PushGlobal(CreateFrozenGlobals(request.Context));
context.PushOutput(output);
```

For each path segment, use `ScriptObject.SetValue(segment, childOrValue, readOnly: true)`. Build all descendants before setting `IsReadOnly = true` on each object.

- [ ] **Step 5: Implement the first closed policy and renderer flow**

The first policy permits raw text, normal Scriban delimiter escape nodes, scalar/dotted global variables, and string/Boolean literal output. It rejects every other semantic statement/expression. Task 4 extends this same policy for conditionals. Define `MaximumNodeCount = 10_000` and `MaximumDepth = 64`, then validate the page body through explicit semantic properties:

```csharp
internal static void Validate(ScriptPage page)
{
    var policy = new RestrictedTemplatePolicy();
    if (page.FrontMatter is not null)
    {
        throw new ForbiddenTemplateConstructException();
    }

    policy.ValidateBlock(page.Body, depth: 1);
}

private void ValidateOutput(ScriptExpression? expression, int depth)
{
    Enter(depth);
    switch (expression)
    {
        case ScriptVariableGlobal:
            return;
        case ScriptMemberExpression member:
            ValidateVariablePath(member);
            return;
        case ScriptLiteral { Value: string or bool }:
            return;
        default:
            throw new ForbiddenTemplateConstructException();
    }
}
```

```csharp
public async Task<string> RenderAsync(
    TemplateRenderRequest request,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(request);
    cancellationToken.ThrowIfCancellationRequested();

    Template template;
    try
    {
        template = Template.Parse(request.Template);
    }
    catch (Exception exception) when (!TemplateRenderFailures.IsFatal(exception))
    {
        throw TemplateRenderFailures.Parse();
    }

    if (template.HasErrors || template.Page is null)
    {
        throw TemplateRenderFailures.Parse();
    }

    try
    {
        RestrictedTemplatePolicy.Validate(template.Page);
    }
    catch (ForbiddenTemplateConstructException)
    {
        throw TemplateRenderFailures.Policy();
    }

    cancellationToken.ThrowIfCancellationRequested();

    var output = new BoundedTemplateOutput(MaximumOutputLength, cancellationToken);
    var context = RestrictedTemplateContextFactory.Create(request, output, cancellationToken);
    try
    {
        var result = await template.RenderAsync(context).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (ScriptAbortException)
    {
        throw new OperationCanceledException(cancellationToken);
    }
    catch (Exception exception) when (TemplateRenderFailures.IsOutputLimit(exception))
    {
        throw TemplateRenderFailures.OutputTooLarge();
    }
    catch (Exception exception) when (TemplateRenderFailures.IsMissingVariable(exception))
    {
        throw TemplateRenderFailures.MissingVariable();
    }
    catch (ScriptRuntimeException)
    {
        throw TemplateRenderFailures.RenderFailed();
    }
}
```

`MaximumOutputLength` is exactly `4 * 1024 * 1024`. Error factories create `InfrastructureOperationException` from only the stable table at the top of this plan.

- [ ] **Step 6: Run renderer GREEN and commit**

Run the command from Step 2 twice. Expected: every renderer test passes with zero skipped.

```powershell
git add src/DevForge.Infrastructure/Templates tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplateRendererTests.cs
git commit -m "feat(infrastructure): render bounded Scriban variables"
```

## Task 4: Enforce the closed conditional grammar and forbidden AST matrix

**Files:**
- Modify: `src/DevForge.Infrastructure/Templates/RestrictedTemplatePolicy.cs`
- Create: `tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplatePolicyTests.cs`

- [ ] **Step 1: Write failing allowed-conditional tests**

Cover `if`, `else if`, `else`, nested conditions, `==`, `!=`, `&&`, `||`, `!`, parentheses, string literals, and Boolean literals. Explicitly reject implicit string truthiness.

```csharp
[Theory]
[InlineData("{{ if project.kind == \"api\" }}yes{{ else }}no{{ end }}", "api", "yes")]
[InlineData("{{ if project.kind != \"api\" }}yes{{ else }}no{{ end }}", "worker", "yes")]
[InlineData("{{ if (project.kind == \"api\") && !false }}yes{{ end }}", "api", "yes")]
public async Task RenderAsync_AllowsClosedConditionalGrammar(
    string template,
    string kind,
    string expected)
{
    var request = TemplateRenderRequest.Create(
        template,
        [KeyValuePair.Create<string, string?>("project.kind", kind)]).Value;

    Assert.Equal(expected, await _renderer.RenderAsync(request, CancellationToken.None));
}

[Fact]
public async Task RenderAsync_RejectsImplicitStringTruthiness()
{
    var request = TemplateRenderRequest.Create(
        "{{ if project.enabled }}yes{{ end }}",
        [KeyValuePair.Create<string, string?>("project.enabled", "true")]).Value;

    var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
        () => _renderer.RenderAsync(request, CancellationToken.None));

    Assert.Equal("template.policy.forbidden", exception.Code);
}
```

- [ ] **Step 2: Write the complete forbidden-family theory**

The data table contains one parseable sample for every family and always expects `template.policy.forbidden`: assignment, increment/decrement, `for`, `while`, `tablerow`, `case`/`when`, `break`, `continue`, function definition, direct function call, pipe, `object.eval`, `object.eval_template`, include, import, capture, wrap, array literal, object literal, indexer, optional member access, interpolated string, conditional expression, local variable, `with`, arithmetic/comparison operators outside the allowlist, and whitespace-control/escaped-code delimiters outside the selected syntax.

```csharp
public static TheoryData<string, string> ForbiddenTemplates => new()
{
    { "assignment", "{{ x = \"value\" }}" },
    { "increment", "{{ x++ }}" },
    { "for", "{{ for x in [1] }}{{ x }}{{ end }}" },
    { "while", "{{ while true }}{{ break }}{{ end }}" },
    { "tablerow", "{{ tablerow x in [1] }}{{ x }}{{ end }}" },
    { "case-when", "{{ case project.name }}{{ when \"x\" }}yes{{ end }}" },
    { "break", "{{ break }}" },
    { "continue", "{{ continue }}" },
    { "function-definition", "{{ func f }}x{{ end }}" },
    { "function-call", "{{ string.upcase project.name }}" },
    { "pipe", "{{ project.name | string.upcase }}" },
    { "eval", "{{ object.eval \"1 + 1\" }}" },
    { "eval-template", "{{ object.eval_template \"{{ 1 }}\" }}" },
    { "include", "{{ include \"other\" }}" },
    { "import", "{{ import project }}" },
    { "capture", "{{ capture x }}value{{ end }}" },
    { "wrap", "{{ wrap project }}value{{ end }}" },
    { "array", "{{ [project.name] }}" },
    { "object", "{{ { name: project.name } }}" },
    { "indexer", "{{ project[\"name\"] }}" },
    { "optional-member", "{{ project?.name }}" },
    { "interpolated-string", "{{ $\"{project.name}\" }}" },
    { "conditional-expression", "{{ project.name == \"x\" ? \"a\" : \"b\" }}" },
    { "local-variable", "{{ $x }}" },
    { "with", "{{ with project }}{{ name }}{{ end }}" },
    { "arithmetic", "{{ project.name + \"x\" }}" },
    { "greater-than", "{{ if project.name > \"x\" }}yes{{ end }}" },
    { "whitespace-control", "{{- project.name -}}" },
    { "escaped-code", "{{{ project.name }}}" },
};
```

The test method receives the case name and template, renders with a context containing `project.name`, and includes only the case name in its custom assertion failure.

- [ ] **Step 3: Run policy RED**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~RestrictedTemplatePolicyTests
```

Expected: conditional happy paths fail and at least one forbidden case is accepted by the variable-only policy/parser flow.

- [ ] **Step 4: Implement semantic statement and expression validation**

Use explicit property traversal rather than default visitor traversal so parser tokens are not accidentally treated as executable nodes. Count every visited semantic statement/expression and reject more than 10,000 nodes or depth greater than 64.

```csharp
private void ValidateStatement(ScriptStatement statement, int depth)
{
    Enter(depth);
    switch (statement)
    {
        case ScriptRawStatement:
            return;
        case ScriptEscapeStatement escape when
            escape.EscapeCount == 0 &&
            escape.WhitespaceMode == ScriptWhitespaceMode.None:
            return;
        case ScriptExpressionStatement expression:
            ValidateOutput(expression.Expression, depth + 1);
            return;
        case ScriptIfStatement conditional:
            ValidateCondition(conditional.Condition, depth + 1);
            ValidateBlock(conditional.Then, depth + 1);
            ValidateElse(conditional.Else, depth + 1);
            return;
        default:
            throw new ForbiddenTemplateConstructException();
    }
}

private void ValidateCondition(ScriptExpression? expression, int depth)
{
    Enter(depth);
    switch (expression)
    {
        case ScriptLiteral { Value: bool }:
            return;
        case ScriptNestedExpression nested:
            ValidateCondition(nested.Expression, depth + 1);
            return;
        case ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } unary:
            ValidateCondition(unary.Right, depth + 1);
            return;
        case ScriptBinaryExpression binary when
            binary.Operator is ScriptBinaryOperator.And or ScriptBinaryOperator.Or:
            ValidateCondition(binary.Left, depth + 1);
            ValidateCondition(binary.Right, depth + 1);
            return;
        case ScriptBinaryExpression binary when
            binary.Operator is ScriptBinaryOperator.CompareEqual or
                ScriptBinaryOperator.CompareNotEqual:
            ValidateComparablePair(binary.Left, binary.Right, depth + 1);
            return;
        default:
            throw new ForbiddenTemplateConstructException();
    }
}
```

`ValidateComparablePair` permits two variable/string operands or two Boolean literals. `ValidateVariablePath` permits only `ScriptVariableGlobal` and `ScriptMemberExpression` chains whose dot token is a normal dot and whose members are global identifier nodes. `ValidateBlock` iterates statements; `ValidateElse` accepts null, `ScriptElseStatement`, or an `IsElseIf` `ScriptIfStatement`. Policy exceptions never store a node, span, token, variable name, or template fragment.

- [ ] **Step 5: Run policy GREEN and commit**

Run the command from Step 3 twice. Expected: all allowed and forbidden cases pass, zero skipped.

```powershell
git add src/DevForge.Infrastructure/Templates/RestrictedTemplatePolicy.cs tests/DevForge.IntegrationTests/Infrastructure/Templates/RestrictedTemplatePolicyTests.cs
git commit -m "feat(infrastructure): restrict Scriban template grammar"
```

## Task 5: Prove bounds, cancellation, and privacy-safe failures

**Files:**
- Modify: `src/DevForge.Infrastructure/Templates/TemplateRenderFailures.cs`
- Modify: `src/DevForge.Infrastructure/Templates/BoundedTemplateOutput.cs`
- Modify: `src/DevForge.Infrastructure/Templates/RestrictedScribanTemplateRenderer.cs`
- Create: `tests/DevForge.IntegrationTests/Infrastructure/Templates/TemplateRendererSecurityTests.cs`

- [ ] **Step 1: Write failing stable-error and non-leakage tests**

For parse, policy, missing-variable, output-limit, and render failures, assert exact `Code`, exact fixed `Message`, null `InnerException`, and absence of the template marker, context name, context value, rendered partial output, Scriban message, source span, and credential fixture in `exception.ToString()`.

```csharp
[Fact]
public async Task RenderAsync_MissingVariableReturnsOnlyStableScrubbedFailure()
{
    const string privateMarker = "private-template-marker";
    var request = TemplateRenderRequest.Create(
        $"{privateMarker}: {{{{ missing.value }}}}",
        []).Value;

    var exception = await Assert.ThrowsAsync<InfrastructureOperationException>(
        () => _renderer.RenderAsync(request, CancellationToken.None));

    Assert.Equal("template.variable.missing", exception.Code);
    Assert.Equal("A required template variable is missing.", exception.Message);
    Assert.Null(exception.InnerException);
    Assert.DoesNotContain(privateMarker, exception.ToString(), StringComparison.Ordinal);
    Assert.DoesNotContain("missing.value", exception.ToString(), StringComparison.OrdinalIgnoreCase);
}
```

Also assert a credential-shaped context value is rejected by `TemplateRenderRequest.Create` before rendering and does not appear in any validation issue.

- [ ] **Step 2: Write failing node/depth/output/cancellation tests**

- Generate 10,001 `{{ value }}` nodes under the 1 MiB request limit and expect `template.policy.forbidden`.
- Generate 65 nested parenthesized Boolean conditions and expect `template.policy.forbidden`.
- Render a 64 KiB value 65 times and expect `template.output.too-large` with no partial output returned.
- Pass a pre-cancelled token and expect `OperationCanceledException`.
- Construct `BoundedTemplateOutput` through the Infrastructure friend boundary, write once, cancel, and assert the next synchronous and asynchronous writes throw `OperationCanceledException` without changing retained output.
- Run 32 concurrent failing renders and assert each exception contains only its fixed code/message.

- [ ] **Step 3: Run security RED**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~TemplateRendererSecurityTests
```

Expected: the focused suite fails on the first unimplemented exception-chain, limit, or cancellation assertion; the failure identifies the exact test method.

- [ ] **Step 4: Complete safe exception classification**

Traverse only exception types, never messages, to recognize internal markers:

```csharp
private static bool Contains<TException>(Exception? exception)
    where TException : Exception
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is TException)
        {
            return true;
        }
    }

    return false;
}
```

Map `ScriptAbortException` and direct `OperationCanceledException` to cancellation. Map marker chains before the generic `ScriptRuntimeException` catch. Do not persist, log, return, or attach the caught Scriban exception. Fatal runtime exceptions (`OutOfMemoryException`, `StackOverflowException`, `AccessViolationException`) remain outside broad catches.

- [ ] **Step 5: Run security and all renderer GREEN, then commit**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Templates
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Templates
```

Expected: both runs pass identically with zero failed and zero skipped.

```powershell
git add src/DevForge.Infrastructure/Templates tests/DevForge.IntegrationTests/Infrastructure/Templates/TemplateRendererSecurityTests.cs
git commit -m "test(infrastructure): harden template rendering boundaries"
```

## Task 6: Record the architecture decision and corrected M3 scope

**Files:**
- Create: `docs/decisions/0006-restricted-scriban-template-runtime.md`
- Modify: `docs/superpowers/specs/2026-08-10-m3-core-infrastructure-design.md`
- Modify: `docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md`
- Modify: `docs/implementation-plan.md`
- Modify: `docs/implementation-status.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Write ADR-0006**

Record status `Accepted`, the discovered M3 specification gap, exact Scriban 7.2.5 pin, Infrastructure-only ownership, closed AST grammar, empty built-ins, frozen string-only model, limits, cancellation, scrubbed errors, and consequences. State that M4 consumes the renderer port but does not widen its language.

- [ ] **Step 2: Correct M3 design and executed-plan counts**

Change references from five to six M3 production-backed ports and add `ITemplateRenderer` to the architecture flow, expected files, tests, and exit gate. Preserve the historical evidence for the original five-port checkpoint and append a dated closure section; do not rewrite old command results as though the renderer existed then.

- [ ] **Step 3: Keep implementation status honest before the final gate**

Set M3 to `Verification in progress` while code is present but the full exit gate is not yet fresh. Add the approved closure design, this plan, and ADR link. Do not mark M3 complete or recommend M4 until Task 7 succeeds.

- [ ] **Step 4: Commit documentation structure**

```powershell
git add docs/decisions/0006-restricted-scriban-template-runtime.md docs/superpowers/specs/2026-08-10-m3-core-infrastructure-design.md docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md docs/implementation-plan.md docs/implementation-status.md CHANGELOG.md
git commit -m "docs: record restricted template runtime decision"
```

## Task 7: Run the complete fresh M3 closure exit gate

**Files:**
- Modify: `docs/implementation-status.md`
- Modify: `docs/implementation-plan.md`
- Modify: `docs/superpowers/plans/2026-08-11-m3-restricted-template-renderer-closure.md`

- [ ] **Step 1: Format and inspect the exact diff**

```powershell
..\..\.tools\dotnet\dotnet.exe format DevForge.sln --no-restore --verbosity minimal
..\..\.tools\dotnet\dotnet.exe format DevForge.sln --verify-no-changes --no-restore --verbosity minimal
git diff --check
git status --short
git diff --name-only
```

Expected: format and diff checks exit 0; every changed path appears in the approved file map or is a restore-generated dependent lock file whose graph was inspected.

- [ ] **Step 2: Run locked restore, Release build, and the full solution**

```powershell
..\..\.tools\dotnet\dotnet.exe restore DevForge.sln --locked-mode --verbosity minimal
..\..\.tools\dotnet\dotnet.exe build DevForge.sln --configuration Release --no-restore --verbosity minimal
..\..\.tools\dotnet\dotnet.exe test DevForge.sln --configuration Release --no-build --no-restore
```

Expected: every command exits 0; Release build reports 0 warnings and 0 errors; every discovered test passes with 0 failed and 0 skipped renderer/M3 tests.

- [ ] **Step 3: Run focused contract, architecture, renderer, and M3 suites**

```powershell
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~TemplateRenderRequestTests|FullyQualifiedName~TemplateRendererDependencyTests|FullyQualifiedName~PrivacyTests"
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Templates
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Infrastructure|FullyQualifiedName~Architecture"
..\..\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Infrastructure
```

Expected: all four commands exit 0 with zero failed and zero skipped.

- [ ] **Step 4: Record exact evidence and mark M3 complete**

Copy the actual SDK version, command text, exit codes, project counts, test counts, warning/error counts, and skipped counts into `docs/implementation-status.md`. Mark the closure checklist and this plan complete only from those fresh results. Set the recommended next milestone to M4 only after every gate above is green.

- [ ] **Step 5: Verify documentation-only final diff and commit**

```powershell
git diff --check
git diff -- docs/implementation-status.md docs/implementation-plan.md docs/superpowers/plans/2026-08-11-m3-restricted-template-renderer-closure.md
git add docs/implementation-status.md docs/implementation-plan.md docs/superpowers/plans/2026-08-11-m3-restricted-template-renderer-closure.md
git commit -m "docs: complete M3 template renderer closure"
git status --short
```

Expected: commit succeeds and final `git status --short` is empty. Do not push.

## Exit gate

M3 closure passes only when `ITemplateRenderer` has a production Infrastructure implementation; the request is bounded and immutable; only the documented AST grammar executes; built-ins, .NET objects, functions, loops, assignment, loaders, and indexers are inaccessible; output and AST are bounded; cancellation is honored; concurrency and culture are deterministic; failures contain only stable scrubbed codes/messages; Scriban is centrally pinned and Infrastructure-owned; locked restore, format verification, Release build, full tests, focused contract/architecture/renderer/M3 suites all exit 0; Release build has zero warnings/errors; and no renderer/M3 test is skipped.

## Deferred after closure

- M4 catalog loading, compatibility evaluation, deterministic planning rules, and SHA-256 plan hashing.
- M5 handler orchestration, retries, staging, resume, and finalization.
- M6 WPF composition and workflow UI.
- M8 Git and M9 GitHub automation.
- M10 packaging, retention execution, and support bundles.
