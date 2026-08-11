# M4 Planner, Rules, and Blueprint Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Load and quarantine guarded blueprint packages, evaluate compatibility deterministically, and create immutable previewable execution plans with stable SHA-256 hashes without executing any step.

**Architecture:** Blueprints.Abstractions owns dependency-free normalized package models and SemVer behavior; Application owns planning, rules, schema validation, previews, and canonical hashing; Infrastructure owns guarded discovery, bounded YAML/JSON/checksum parsing, trust assignment, and atomic catalog snapshots. Domain owns immutable plan values and execution-plan invariants. M4 adds no production blueprint and performs no generation action.

**Tech Stack:** .NET SDK 10.0.302, C# 14, YamlDotNet 18.1.0, System.Text.Json, SHA256, xUnit 2.9.3, guarded M3 workspace abstractions.

---

## Source, scope, and exit gate

- Source: `docs/DevForge_Studio_Codex_Implementation_Specification_V1.0.docx` and Markdown companion.
- Approved design: `docs/superpowers/specs/2026-08-10-m4-planner-rules-blueprint-catalog-design.md`.
- Decision: `docs/decisions/0007-deterministic-blueprint-planning.md`.
- Included: package discovery, checksums, trust/quarantine, manifest/schema/rules parsing, action/variable policy, exact resolution, rule evaluation, effective inputs, immutable preview, canonical serialization, and plan hashing.
- Excluded: step execution, staging, retry/resume, WPF screens, Git/GitHub behavior, and production blueprints.
- Exit gate: locked restore, format, Release build, full solution tests, Blueprint contract tests, M4 unit/integration/security tests, deterministic hash snapshots, and zero skipped M4 tests.

## File responsibility map

### Domain

- `src/DevForge.Domain/Execution/PlanValue.cs`: immutable bounded JSON-like value tree.
- `src/DevForge.Domain/Execution/ExecutionPlan.cs`: typed step payloads and enriched immutable plan metadata.
- `tests/DevForge.UnitTests/Domain/PlanValueTests.cs`: snapshots, depth, duplicate keys, privacy, and equality.

### Blueprint abstractions

- `src/DevForge.Blueprints.Abstractions/Models/SemanticVersion.cs`: SemVer 2.0 parse/order.
- `src/DevForge.Blueprints.Abstractions/Models/SemanticVersionRange.cs`: comparator range evaluation.
- `src/DevForge.Blueprints.Abstractions/Models/BlueprintPackageModels.cs`: features, actions, validators, artifacts, dependencies, rules, and schema subset.
- `src/DevForge.Blueprints.Abstractions/Models/BlueprintManifest*.cs`: guarded normalized manifest construction.
- `tests/DevForge.BlueprintTests/Contracts/*.cs`: dependency-free contract coverage.

### Application

- `src/DevForge.Application/Contracts/BlueprintContracts.cs`: source, fingerprint, inspection, refresh, exact resolution.
- `src/DevForge.Application/Contracts/PlanningContracts.cs`: `PlannedProject`, `PlanPreview`, and planner result.
- `src/DevForge.Application/Planning/CompatibilityRules/*.cs`: lexer, parser, typed AST, evaluator.
- `src/DevForge.Application/Planning/InputSchemaValidator.cs`: supported schema subset/defaults.
- `src/DevForge.Application/Planning/VariableTemplateResolver.cs`: single-pass known-variable substitution.
- `src/DevForge.Application/Planning/CanonicalPlanSerializer.cs`: fixed-order canonical UTF-8 JSON.
- `src/DevForge.Application/Planning/PlanHasher.cs`: lowercase `sha256:` digest.
- `src/DevForge.Application/Planning/ProjectPlanner.cs`: bounded recipe-to-plan orchestration.
- `tests/DevForge.UnitTests/Application/Planning/*.cs`: rule, schema, variable, planner, preview, and hash tests.

### Infrastructure

- `src/DevForge.Infrastructure/Blueprints/BlueprintPackageLoader.cs`: bounded package verification/parsing.
- `src/DevForge.Infrastructure/Blueprints/BlueprintYamlReader.cs`: closed duplicate-safe YAML DTO reader.
- `src/DevForge.Infrastructure/Blueprints/BlueprintJsonSchemaReader.cs`: closed JSON Schema subset.
- `src/DevForge.Infrastructure/Blueprints/BlueprintChecksumVerifier.cs`: per-file and aggregate SHA-256.
- `src/DevForge.Infrastructure/Blueprints/BlueprintActionPolicy.cs`: handler/trust/path/payload policy.
- `src/DevForge.Infrastructure/Blueprints/BlueprintCatalog.cs`: atomic refresh, trust reconciliation, conflict quarantine.
- `src/DevForge.Application/Contracts/FileSystemContracts.cs` and `src/DevForge.Infrastructure/FileSystem/WindowsWorkspaceFileSystem.cs`: guarded immediate-child directory enumeration.
- `tests/DevForge.IntegrationTests/Infrastructure/Blueprints/*.cs`: real guarded fixture packages.

## Stable limits and errors

- 256 packages/source; 2,048 files/package; 32 MiB declared content/package.
- 256 KiB/control file; 16,384 characters/scalar; 128 YAML/JSON levels.
- `DF-BP-001` malformed structure/parser/schema; `DF-BP-002` checksum/trust; `DF-BP-003` forbidden handler/path/variable; `DF-BP-004` bounds; `DF-BP-005` identity conflict.
- `DF-PLAN-001` recipe/engine/tool/rule incompatibility; `DF-PLAN-002` guarded canonicalization/hash failure.
- Public issues contain no raw YAML/JSON, absolute path, secret-shaped value, parser message, stack trace, or inner exception.

## Task 1: Add immutable typed plan values

**Files:** Domain plan files and `PlanValueTests.cs`.

Required public shape:

```csharp
public enum PlanValueKind { Text = 1, Boolean = 2, WholeNumber = 3, Sequence = 4, Map = 5 }

public sealed class PlanValue
{
    public const int MaximumDepth = 64;
    public PlanValueKind Kind { get; }
    public string? StringValue { get; }
    public bool BooleanValue { get; }
    public long IntegerValue { get; }
    public ImmutableArray<PlanValue> ArrayValue { get; }
    public ImmutableDictionary<string, PlanValue> ObjectValue { get; }

    public static ValidationResult<PlanValue> FromString(string? value);
    public static PlanValue FromBoolean(bool value);
    public static PlanValue FromInteger(long value);
    public static ValidationResult<PlanValue> FromArray(IEnumerable<PlanValue?>? values);
    public static ValidationResult<PlanValue> FromObject(IEnumerable<KeyValuePair<string, PlanValue?>>? values);
}
```

- [x] **Step 1: Write RED tests** for string/Boolean/Int64/array/object creation, single enumeration, ordinal object keys, depth 64/65, duplicate keys, null nodes, undefined enum/numeric values, secret-shaped keys, credential-shaped strings, and caller mutation.
- [x] **Step 2: Run RED:**

```powershell
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj -c Release --filter FullyQualifiedName~PlanValueTests
```

Expected: compile failure because `PlanValue` and typed step payload APIs do not exist.

- [x] **Step 3: Implement** a closed `PlanValueKind` and private-constructor `PlanValue` with guarded factories. Arrays snapshot once; objects snapshot once and reject ordinal duplicates. No implicit conversion or public raw-object constructor exists.
- [x] **Step 4: Change `ExecutionStep.Inputs`** from `ImmutableDictionary<string,string>` to `ImmutableDictionary<string,PlanValue>`, preserve guarded creation, and add migrations only to test builders—no persistence schema changes in M4.
- [x] **Step 5: GREEN**, full Domain tests, format, and commit `feat(domain): add immutable typed plan values`.

## Task 2: Complete SemVer and normalized blueprint contracts

**Files:** Blueprint model files and BlueprintTests.

Required normalized shapes:

```csharp
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion? version);
    public int CompareTo(SemanticVersion? other);
}

public enum CompatibilityRuleSeverity { Blocking = 1, Warning = 2 }
public enum CompatibilityRuleOverride { None = 1 }
public sealed record BlueprintFeatureDefinition(string Id, bool DefaultEnabled);
public sealed record BlueprintDependency(string Id, string Version);
public sealed record BlueprintArtifact(string Path);
public sealed record BlueprintActionDefinition(
    string Id,
    string HandlerId,
    ImmutableDictionary<string, BlueprintValue> Parameters,
    TimeSpan Timeout);
```

- [x] **Step 1: Write RED tests** proving SemVer numeric ordering, prerelease precedence, build-metadata equality for precedence, comparator AND/OR ranges, invalid leading zeroes, and exact normalization.
- [x] **Step 2: Write RED contract tests** for features, required tools, input constraints/defaults, rule IDs/severity/remediation/override-none, typed action payloads, validators, dependencies, artifacts, uniqueness, immutable snapshots, and trust provenance.
- [x] **Step 3: Run RED:** `dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj -c Release` and require missing-type/behavior failures.
- [x] **Step 4: Implement `SemanticVersion`** and make `SemanticVersionRange.Contains` compare SemVer 2.0 values without culture or build metadata precedence.
- [x] **Step 5: Implement normalized package models** with explicit nonzero enums, bounded identifiers/text/collections, stable validation issues, and no Application/Infrastructure dependency.
- [x] **Step 6: GREEN twice**, verify project references unchanged, format, commit `feat(blueprints): define normalized M4 package contracts`.

## Task 3: Evolve catalog, filesystem, and planner contracts

**Files:** Application contracts, Windows workspace, contract tests.

- [x] **Step 1: Write RED tests** for `EnumerateDirectoriesAsync`, `BlueprintPackageSource`, provenance, `BlueprintFingerprint`, `BlueprintInspection`, immutable `BlueprintCatalogSnapshot`, `ResolvedBlueprint`, exact `RefreshAsync/InspectAsync/ListAsync/FindAsync`, `PlanPreview`, and `PlannedProject`.
- [x] **Step 2: Specify exact APIs:**

```csharp
public interface IBlueprintCatalog
{
    Task RefreshAsync(CancellationToken cancellationToken);
    Task<BlueprintCatalogSnapshot> InspectAsync(CancellationToken cancellationToken);
    Task<ImmutableArray<ResolvedBlueprint>> ListAsync(CancellationToken cancellationToken);
    Task<ResolvedBlueprint?> FindAsync(BlueprintReference reference, CancellationToken cancellationToken);
}

public interface IProjectPlanner
{
    Task<ValidationResult<PlannedProject>> CreatePlanAsync(
        ProjectRecipe recipe,
        CancellationToken cancellationToken);
}
```

- [x] **Step 3: Run RED** focused on Application/FileSystem contract tests.
- [x] **Step 4: Implement guarded factories and immutable snapshots**; catalog paths remain opaque and never expose absolute values.
- [x] **Step 5: Implement immediate-child directory enumeration** in `WindowsWorkspaceFileSystem` using the existing canonical/reparse guard; update all test stubs.
- [x] **Step 6: GREEN** contracts and real filesystem tests; commit `feat(application): define M4 catalog and planning contracts`.

## Task 4: Pin YAML and guard the parsing boundary

**Files:** central packages, Infrastructure csproj/locks, architecture tests, YAML/JSON readers.

The readers expose closed results rather than serializer DTOs:

```csharp
internal interface IBlueprintControlReader<T>
{
    ValueTask<BlueprintLoadResult<T>> ReadAsync(
        Stream content,
        CancellationToken cancellationToken);
}

internal sealed record BlueprintLoadResult<T>(
    T? Value,
    ImmutableArray<BlueprintInspectionIssue> Issues)
{
    public bool IsValid => Value is not null && Issues.IsEmpty;
}
```

- [x] **Step 1: Write RED architecture tests** requiring exact YamlDotNet 18.1.0, Infrastructure-only ownership, and absence of YAML/filesystem/process/reflection APIs from Application and Blueprint abstractions.
- [x] **Step 2: Add central pin/reference**, force-evaluate restore, inspect all changed lock files, then locked restore.
- [x] **Step 3: Write parser RED fixtures** for unknown/duplicate fields, anchors, aliases, merge keys, tags, non-scalar keys, scalar/depth/control-file limits, unsupported JSON Schema keywords, duplicate JSON properties, and remote/reference keywords.
- [x] **Step 4: Implement closed readers** over bounded streams and exact DTO/property allowlists. Map every nonfatal parser exception to a stable issue without retaining caught exceptions.
- [x] **Step 5: GREEN** parser and architecture tests; commit `feat(infrastructure): parse bounded blueprint controls`.

## Task 5: Verify checksums, paths, variables, and action policy

**Files:** Infrastructure checksum/action/loader files and integration tests.

The loader boundary is package-relative and opaque:

```csharp
internal interface IBlueprintPackageLoader
{
    Task<BlueprintPackageLoadResult> LoadAsync(
        BlueprintPackageSource source,
        WorkspaceRelativePath packageDirectory,
        CancellationToken cancellationToken);
}

internal sealed record BlueprintPackageLoadResult(
    ResolvedBlueprint? Blueprint,
    BlueprintInspection Inspection);
```

- [x] **Step 1: Write RED real-workspace fixtures** covering mandatory layout, missing/extra/duplicate/self-declared checksum entries, hash mismatch, 2,048/32 MiB bounds, traversal/rooted/device/forward-slash canonicalization, `.env` target rejection with `.env.example` acceptance, and junction escape.
- [x] **Step 2: Write RED policy matrix** for every allowed handler, missing/unknown parameters, combined command line, shell mode, unsafe paths, untrusted/built-in restrictions, typed arguments, validators, and malformed/recursive/unknown variable references.
- [x] **Step 3: Implement checksum verification** before package-controlled parsing. Aggregate hash input is ordinal `path + NUL + lowercase hash + LF`; `checksums.json` cannot declare itself.
- [x] **Step 4: Implement single-pass variable tokenizer and action policy** with closed handler descriptors; never activate handlers or execute commands.
- [x] **Step 5: Implement `BlueprintPackageLoader`** enforcing all limits and producing either normalized package data or one scrubbed quarantine inspection.
- [x] **Step 6: GREEN twice**, security scan test, format, commit `feat(infrastructure): validate guarded blueprint packages`.

## Task 6: Publish an atomic executable catalog

**Files:** `BlueprintCatalog.cs` and catalog integration tests.

Publication uses one immutable holder:

```csharp
internal sealed record CatalogState(
    ImmutableArray<ResolvedBlueprint> Executable,
    BlueprintCatalogSnapshot Inspection);

public sealed class BlueprintCatalog : IBlueprintCatalog
{
    private CatalogState _state = CatalogState.Empty;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var candidate = await BuildCandidateAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _state, candidate);
    }
}
```

- [x] **Step 1: Write RED tests** for built-in trust, local untrusted default, persisted exact-checksum TrustedLocal, changed-checksum downgrade, disabled state, malformed inspect-only entries, conflicts, deterministic order, exact lookup, cancellation/source failure/metadata failure retaining the prior snapshot, and 32 concurrent readers during refresh.
- [x] **Step 2: Implement refresh pipeline** in the documented 1–9 order. Build a complete candidate snapshot locally and publish with one atomic reference exchange only after all sources finish.
- [x] **Step 3: Reconcile metadata read-only**; discovery never writes trust. Local packages never become BuiltIn. Untrusted/quarantined/disabled/conflicting entries never appear in executable list/find.
- [x] **Step 4: GREEN twice**, run persistence metadata regressions, commit `feat(infrastructure): load atomic blueprint catalog snapshots`.

## Task 7: Parse and evaluate the closed compatibility grammar

**Files:** Application compatibility-rule files and tests.

Required typed core:

```csharp
internal abstract record RuleExpression;
internal sealed record RuleBinary(
    RuleExpression Left,
    RuleBinaryOperator Operator,
    RuleExpression Right) : RuleExpression;
internal sealed record RuleIdentifier(string Value) : RuleExpression;
internal sealed record RuleLiteral(PlanValue Value) : RuleExpression;

internal interface ICompatibilityRuleEngine
{
    ValidationResult<RuleEvaluation> Evaluate(
        string expression,
        PlanningRuleContext context,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write lexer/parser RED tests** for precedence, parentheses, `==`, `!=`, `in`, `not-in`, `satisfies`, string/Boolean/Int64/list literals, fixed identifiers, EOF, token/depth/input limits, and forbidden calls/member reflection/assignment/interpolation/regex.
- [ ] **Step 2: Write evaluator RED tests** for exact typed comparison, list membership, SemVer ranges, missing known context, unknown identifier, type mismatch, blocking aggregation, warning order, and cancellation.
- [ ] **Step 3: Implement immutable typed AST**, bounded hand-written lexer/parser, and evaluator over a closed `PlanningRuleContext`; no reflection, dynamic, regex, environment, file, process, or network access.
- [ ] **Step 4: GREEN twice**, format, commit `feat(application): evaluate closed compatibility rules`.

## Task 8: Validate effective recipe inputs and planning variables

**Files:** `InputSchemaValidator.cs`, `VariableTemplateResolver.cs`, tests.

The validators return guarded immutable values only:

```csharp
internal interface IInputSchemaValidator
{
    ValidationResult<ImmutableDictionary<string, PlanValue>> Validate(
        BlueprintInputSchema schema,
        ProjectRecipe recipe);
}

internal interface IVariableTemplateResolver
{
    ValidationResult<PlanValue> Resolve(
        PlanValue template,
        PlanningVariableContext context);
}
```

- [ ] **Step 1: Write RED tests** for defaults only when absent, required, additionalProperties false, string/Boolean/integer parsing, enum, min/max length/value, unknown inputs/features, credential-shaped values, deterministic effective ordering, known planning variables, typed M5 placeholders, malformed delimiters, functions/filters, unknown/secret-shaped references, and single-pass non-recursion.
- [ ] **Step 2: Implement schema validation** returning an immutable ordinal map of typed `PlanValue`; aggregate `DF-PLAN-001` issues without coercing invalid values.
- [ ] **Step 3: Implement variable resolution** over the documented fixed catalog; replacement values are not reparsed.
- [ ] **Step 4: GREEN**, full Application tests, commit `feat(application): validate blueprint inputs and variables`.

## Task 9: Build deterministic previews and plan hashes

**Files:** planner, serializer, hasher, tests.

Required deterministic boundaries:

```csharp
internal interface ICanonicalPlanSerializer
{
    byte[] Serialize(PlanHashInput input);
}

internal sealed class PlanHasher(ICanonicalPlanSerializer serializer)
{
    public string Compute(PlanHashInput input)
    {
        var digest = SHA256.HashData(serializer.Serialize(input));
        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }
}
```

- [ ] **Step 1: Write planner RED tests** for exact catalog resolution, engine/tool checks, features, blocking/warning rules, ordered steps/validators/requirements/dependencies/artifacts, cancellation between stages, aggregation, and no direct I/O/process behavior.
- [ ] **Step 2: Write canonical hash RED snapshots**: identical normalized inputs across culture/enumeration/machine paths yield identical bytes/hash; every effect-bearing mutation changes hash; warning/tool-detection/timestamp/absolute-root mutations do not; ordered step/argument reordering changes hash.
- [ ] **Step 3: Implement `CanonicalPlanSerializer`** with fixed property order and ordinal object keys using `Utf8JsonWriter`; no timestamp, random ID, detected path/version, indentation, or BOM.
- [ ] **Step 4: Implement `PlanHasher`** using `SHA256.HashData` and lowercase `sha256:` output.
- [ ] **Step 5: Implement `ProjectPlanner`** with injected catalog, environment doctor, engine version provider, schema/rule/variable services; construct `PlannedProject` only when no blocking issue exists.
- [ ] **Step 6: GREEN twice**, culture/concurrency tests, architecture tests, commit `feat(application): create deterministic hashed execution plans`.

## Task 10: Record and verify M4 completion

**Files:** ADR-0007, implementation plan/status, M4 plan, CHANGELOG.

- [ ] **Step 1: Update docs** with exact delivered scope and retain M3 historical evidence.
- [ ] **Step 2: Run fresh exit gate:**

```powershell
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe restore DevForge.sln --locked-mode --verbosity minimal
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe format DevForge.sln --verify-no-changes --no-restore --verbosity minimal
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe build DevForge.sln -c Release --no-restore --verbosity minimal
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test DevForge.sln -c Release --no-build --no-restore
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test tests\DevForge.BlueprintTests\DevForge.BlueprintTests.csproj -c Release --no-build --no-restore
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Planning|FullyQualifiedName~Blueprint|FullyQualifiedName~Architecture"
E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~Blueprints
```

- [ ] **Step 3: Record exact counts/results**, mark M4 complete only when all commands exit 0 with 0 warning/error and 0 skipped M4 tests.
- [ ] **Step 4: Commit** `docs: complete M4 planner and blueprint catalog milestone`; require clean worktree and no push.

## Deferred after M4

- M5 executes typed actions and owns staging/retry/resume/finalization.
- M6-M7 compose WPF/MVVM and dynamic workflows.
- M8 implements Git/GitHub operations.
- M9 adds the three MVP production blueprints.
- M10 hardens security/diagnostics/packaging/release.
- M11 expands the V1 catalog only after M10 gates pass.
