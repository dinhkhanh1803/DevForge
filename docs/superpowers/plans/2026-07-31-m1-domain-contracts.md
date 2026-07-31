# M1 Domain & Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build immutable, validated M1 models and secure application ports without implementing later-milestone behavior.

**Architecture:** Domain and Blueprint Abstractions remain independent leaves. Application references both and defines ports; Infrastructure remains untouched.

**Tech Stack:** C# 14, .NET 10, `System.Collections.Immutable`, xUnit.

---

## Task 1: Domain models

**Files:** create `src/DevForge.Domain/Validation/*`, `Projects/*`, `Execution/*`, `Runs/*`, `Diagnostics/*`, `Environment/*`, `Reports/*`; test in `tests/DevForge.UnitTests/Domain/*`.

- [ ] Write failing tests against `ProjectRecipe.Create(ProjectRecipeDraft)`. Prove validation aggregation, absolute target path, blueprint identity, rejection of secret-shaped input names, and immutable snapshots.
- [ ] Run `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DevForge.UnitTests.Domain"`; expect compile failure from missing M1 types.
- [ ] Implement immutable validation, recipe, profile, completion, Git option, execution plan/step, retry, run, attempt, environment, report, and error types.
- [ ] Test exact statuses `Draft`, `Planning`, `PreflightFailed`, `Executing`, `ValidationFailed`, `LocalReady`, `PublishPending`, `Completed`, `Cancelled`, `Failed`; explicit lifecycle transitions; retry invariants; immutable plan/error collections.
- [ ] Re-run focused and complete UnitTests; expect all pass. Implement no planner, retry execution, hashing, persistence, orchestration, or I/O.

Expected recipe snapshot assertion:

```csharp
var inputs = new Dictionary<string, string> { ["framework"] = "net10.0" };
var result = ProjectRecipe.Create(ValidDraft(inputs));
Assert.True(result.IsValid);
inputs["framework"] = "changed";
Assert.Equal("net10.0", result.Value.Inputs["framework"]);
```

## Task 2: Blueprint manifest contracts

**Files:** create `src/DevForge.Blueprints.Abstractions/Validation/*`, `Models/*`; test `tests/DevForge.BlueprintTests/Contracts/BlueprintManifestTests.cs`.

- [ ] Write failing tests against `BlueprintManifest.Create(BlueprintManifestDraft)` for immutable snapshots, identifier/SemVer/engine validation, duplicate input/step IDs, positive timeouts, and trust.
- [ ] Run BlueprintTests; expect compile failure caused by missing manifest types.
- [ ] Implement dependency-free immutable models and local validation. Trust values are exactly `BuiltIn`, `TrustedLocal`, `Untrusted`, `Quarantined`.
- [ ] Run BlueprintTests and UnitTests; expect all pass and no new `ProjectReference` in Blueprint Abstractions.

## Task 3: Application ports

**Files:** create `src/DevForge.Application/Contracts/{Planning,Execution,Process,FileSystem,Blueprint,Environment,Journal,Git,GitHub,SecretScanner,Ide}Contracts.cs`; test `tests/DevForge.UnitTests/Application/*`.

- [ ] Write failing presence tests for `IProjectPlanner`, `IExecutionOrchestrator`, `IProcessRunner`, `IFileSystem`, `ITemplateRenderer`, `IBlueprintCatalog`, `IEnvironmentDoctor`, `IRunJournalStore`, `IGitService`, `IGitHubService`, `ISecretScanner`, `IIdeLauncher`.
- [ ] Write failing security tests for executable/argument separation, immutable redaction values, no credential-shaped properties, root-scoped file operations, and validated relative paths.
- [ ] Run focused Application tests; expect compile failure caused by missing contracts.
- [ ] Implement ports and request/results only. Every async operation accepts `CancellationToken`; no arbitrary shell command or credential field is allowed.
- [ ] Run focused Application, complete UnitTests, and BlueprintTests; expect all pass.

Required process shape:

```csharp
var command = new CommandSpec(
    "dotnet",
    ["build", "--configuration", "Release"],
    "C:\\work",
    environmentVariables: null,
    TimeSpan.FromMinutes(5),
    allowedExitCodes: [0],
    redactedValues: ["sensitive"]);
```

## Task 4: Exit gate and documentation

**Files:** modify `docs/implementation-plan.md`, `docs/implementation-status.md`, `CHANGELOG.md`, and `README.md` only if commands or milestone state change.

- [ ] Run format, locked restore, and format verification.
- [ ] Run Release build, full solution tests, focused UnitTests, and focused BlueprintTests.
- [ ] Require exit 0, zero warnings/errors, and zero failed/skipped M1 tests.
- [ ] Record exact command results and totals; never mark M1 complete after an unrun or failed command.