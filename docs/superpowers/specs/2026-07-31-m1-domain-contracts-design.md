# M1 Domain & Contracts Design

**Date:** 2026-07-31  
**Status:** Approved by the instruction to continue with the recommended milestone and safe defaults  
**Source of truth:** `docs/DevForge_Studio_Codex_Implementation_Specification_V1.0.docx`

## Goal

Milestone M1 establishes the immutable business vocabulary and application ports that later milestones implement. It must make invalid recipes and manifests diagnosable, preserve the M0 dependency graph, and prevent infrastructure concerns from leaking into Domain, Blueprint Abstractions, Desktop, or CLI.

## Scope

M1 includes:

- Immutable project recipe, team profile, execution, run, environment, report, retry, and error models.
- Immutable blueprint manifest, tool, input, compatibility, step, validator, and trust models.
- Validation results with stable error codes and field locations.
- The twelve core application interfaces named by the specification.
- A process contract that separates the executable from `ArgumentList`.
- A root-scoped file-system contract whose operations accept only validated relative paths.
- Unit, blueprint-contract, and architecture regression tests.

M1 excludes:

- Planner algorithms, rule evaluation, plan hashing, orchestration, or retry execution.
- File-system, process, persistence, template, Git, GitHub, secret scanner, environment doctor, or IDE implementations.
- EF Core, SQLite, blueprint catalog content, large UI work, and cloud services.

## Approaches considered

1. **Public records with external validators.** This is compact, but invalid objects can circulate and collections can remain mutable.
2. **Constructors that throw on invalid input.** This guarantees valid objects, but it prevents UI and CLI callers from presenting all validation issues in one response.
3. **Guarded factories returning immutable validation results.** This guarantees valid constructed aggregates, aggregates multiple diagnostics, and keeps expected user-input errors out of exception control flow.

M1 uses approach 3. Exceptions remain reserved for programming errors such as reading the value of a failed validation result.

## Architecture

### DevForge.Domain

Domain contains platform-neutral business types and validation primitives. It has no project references and does not know WPF, EF Core, operating-system processes, or concrete file systems.

`ProjectRecipe` is created from a draft through a factory. The factory trims and validates identity fields, requires an absolute target path, snapshots inputs/features into immutable collections, and rejects secret-shaped input names. The input-name policy is defense in depth; content scanning remains the responsibility of `ISecretScanner` in a later milestone.

Execution and run models are immutable snapshots. `ProjectRun` exposes explicit lifecycle transitions and rejects transitions not in the documented state machine. M1 models retry policy but does not execute retries.

`DevForgeError` carries stable code, user-facing summary, technical detail, phase, optional step identifier, retryability, suggested actions, and redacted context. The model has no exception, credential, or arbitrary environment-object field.

### DevForge.Blueprints.Abstractions

Blueprint Abstractions remains dependency-free. It owns the public manifest vocabulary and a small blueprint-specific validation result rather than referencing Domain and weakening the M0 graph.

Manifest construction snapshots every collection and validates identifier, semantic version text, engine range, unique input/step identifiers, positive timeouts, and trust state. M1 describes handler identifiers but does not load or execute handlers.

### DevForge.Application

Application references Domain and Blueprint Abstractions and owns use-case ports:

- `IProjectPlanner`
- `IExecutionOrchestrator`
- `IProcessRunner`
- `IFileSystem`
- `ITemplateRenderer`
- `IBlueprintCatalog`
- `IEnvironmentDoctor`
- `IRunJournalStore`
- `IGitService`
- `IGitHubService`
- `ISecretScanner`
- `IIdeLauncher`

`CommandSpec` separates `FileName` from an immutable argument collection. No shell command string exists. Redaction values are supplied separately, and `ProcessResult` does not retain a single unbounded raw-output blob.

`IFileSystem` opens an allowed root and returns an `IWorkspaceFileSystem`. Every operation on that scoped interface requires a validated `WorkspaceRelativePath`, which rejects rooted paths, empty segments, and `.` or `..`. M3 will add canonicalization, link/reparse-point containment, atomic staging, and the concrete implementation.

## Data flow

1. A UI or CLI adapter creates a recipe draft.
2. Domain validation returns either an immutable recipe or stable validation issues.
3. A future planner receives that recipe and blueprint catalog data through Application contracts.
4. It will produce an immutable execution plan.
5. A future orchestrator will invoke only application ports and record immutable run/error/report snapshots.

M1 stops after defining and testing these boundaries.

## Error handling

- Expected invalid user or manifest data returns validation issues.
- Invalid run-state transitions return a domain validation issue.
- Cancellation is represented by `CancellationToken` on asynchronous ports and by `RunStatus.Cancelled` in persisted snapshots.
- External-process failure details enter the domain only as redacted `DevForgeError` data.
- No contract contains token, password, connection-string, `.env` content, or GitHub auth-token output fields.

## Testing

- Domain tests verify validation aggregation, secret-shaped key rejection, immutable snapshots, retry-policy invariants, run transitions, and error redaction snapshots.
- Blueprint tests verify manifest validation, uniqueness, semantic version checks, and immutable snapshots.
- Contract tests verify all required ports exist, `CommandSpec` has separate executable/arguments, file operations require scoped relative paths, and no M1 assembly references infrastructure/UI packages.
- Existing M0 architecture tests remain green.

## Exit gate

M1 is complete only when:

1. The required domain and blueprint types and all twelve ports exist.
2. Invalid input cannot produce a valid aggregate.
3. Collections are immutable snapshots.
4. Process and file contracts encode the security boundaries above.
5. Domain and Blueprint Abstractions still have no project dependencies.
6. Format, locked restore, Release build, full tests, and focused M1 tests pass with zero warnings and zero errors.

