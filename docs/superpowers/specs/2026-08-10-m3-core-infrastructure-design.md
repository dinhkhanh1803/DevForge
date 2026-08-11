# M3 Core Infrastructure Design

**Status:** Approved; renderer closure verification in progress
**Date:** 2026-08-10
**Milestone:** M3 - Core Infrastructure

## Purpose

Implement the Windows-native infrastructure boundary required by the DevForge Studio specification: safe external-process execution, a workspace-scoped guarded file system, secret scanning, environment inspection, trusted IDE launch, and restricted template rendering. M3 turns the Application contracts delivered in M1 into production implementations without adding planning, orchestration, Git/GitHub automation, blueprint catalog behavior, or significant WPF UI.

## Source requirements

This design implements the infrastructure and security requirements in `DevForge_Studio_Codex_Implementation_Specification_V1.0`:

- DevForge remains a C# and WPF Windows desktop application on .NET 10; no web host, embedded browser, Electron, Tauri, Blazor Hybrid, cloud backend, or AI API is introduced.
- Desktop and Application code never start external processes directly.
- Every external command crosses `IProcessRunner`, uses a trusted executable identity and a separate `ProcessStartInfo.ArgumentList`, and never uses `cmd /c`, arbitrary PowerShell text, or `UseShellExecute=true`.
- Process execution supports redirected asynchronous output, bounded retention, progress, redaction, per-command timeout, cancellation, and full child-process-tree termination.
- Every workspace file operation crosses `IFileSystem`/`IWorkspaceFileSystem` and remains contained by a validated local Windows root.
- Traversal, rooted/device/UNC paths, alternate data streams, reserved names, symbolic-link and junction escapes, unsafe overwrite, and deletion outside a run-owned scope fail closed.
- Secret scanning never returns or persists the detected secret value.
- Template rendering uses the specification-selected Scriban engine through a closed, bounded, string-only runtime with no file, process, network, environment, reflection, or loader access.
- The happy path does not require Administrator privileges.
- Unit, integration, contract, architecture, and security tests accompany the implementations.

## Chosen approach

Build six focused Infrastructure components behind the existing Application contracts:

1. `WindowsProcessRunner` for process creation, monitoring, output redaction, and termination.
2. `WindowsWorkspaceFileSystem` plus its factory for guarded workspace operations.
3. `WorkspaceSecretScanner` for bounded, content-aware secret detection through the guarded file-system port.
4. `WindowsEnvironmentDoctor` for fixed typed tool probes.
5. `WindowsIdeLauncher` for trusted non-elevated IDE handoff.
6. `RestrictedScribanTemplateRenderer` for deterministic string-only variable and conditional rendering.

This is preferred over a monolithic infrastructure service because process trust, path containment, content privacy, environment discovery, and template-language isolation need separate invariants and focused tests. Implementing only the process and file-system portions would leave the milestone incomplete and force later features to duplicate unsafe probing behavior.

## Architecture

### Dependency direction

- `DevForge.Application` continues to own `IProcessRunner`, `IFileSystem`, `IWorkspaceFileSystem`, `ISecretScanner`, `IEnvironmentDoctor`, `IIdeLauncher`, `ITemplateRenderer`, and their immutable request/result contracts.
- `DevForge.Infrastructure` implements those ports under focused `Processes`, `FileSystem`, `Security`, `Environment`, `Ide`, and `Templates` namespaces.
- `DevForge.Domain` remains independent of Infrastructure and operating-system APIs.
- `DevForge.Desktop` will eventually compose these implementations through the Generic Host in M6; M3 does not add direct process or file-system access to Desktop.
- Real Windows behavior is verified in `DevForge.IntegrationTests`; contract/value behavior remains in `DevForge.UnitTests`.

No Infrastructure implementation type is returned through an Application contract except as an opaque interface. Existing M1 contracts remain unchanged unless a failing security/contract test proves a minimal correction is required.

### Component flow

```text
Application request
    -> validated Application contract
    -> Infrastructure implementation
       -> guarded workspace/executable resolution
       -> Windows API or System.IO/System.Diagnostics
       -> bounded and redacted result
    -> Domain/Application snapshot
```

Infrastructure exceptions do not cross the boundary with raw paths, command text, environment data, output, or secrets. Expected invalid input is rejected by guarded factories; operating-system failures are mapped to stable, scrubbed diagnostics at the Infrastructure boundary.

Template requests follow the same boundary but remain pure: after guarded construction, Infrastructure parses and validates a closed AST, renders through a fresh empty runtime into bounded output, and returns no engine diagnostic or partial result on failure.

## Process runner

### Executable resolution

`ExecutableIdentity` is the only executable selector accepted by `WindowsProcessRunner`. A dedicated resolver maps its `ExecutableTool` value to a discovered, validated executable path. The runner never accepts a raw executable string from a recipe, blueprint, UI field, or persisted record.

Resolution follows explicit trusted candidates and the current non-elevated environment. The resolved target must be a regular local file, not a directory or reparse-point escape. Resolution failures return a stable failure rather than falling back to shell search or interpreting user text.

### Process start

For every command the runner:

1. Resolves the command's opaque workspace root and guarded relative working directory.
2. Revalidates containment immediately before process start.
3. Creates `ProcessStartInfo` with `UseShellExecute=false`, redirected standard output/error, no window, and a separate `ArgumentList` entry for each argument.
4. Applies only validated environment entries. Sensitive values are revealed at the final process-start boundary and are never copied into logs or exception messages.
5. Starts the executable without elevation and asynchronously drains stdout and stderr from the beginning of execution.

No command line is assembled for execution. `cmd.exe`, `powershell.exe`, and `pwsh.exe` are not added to the trusted executable catalog in M3.

### Output and privacy

Output is streamed through a bounded line accumulator. Before progress notification or retention, each line is scrubbed using all supplied sensitive redaction needles and the shared credential-shape defense. Redaction happens before constructing `RedactedText`.

- Each retained line respects `ProcessOutputLine.MaxTextLength`.
- Total retained lines and characters respect `ProcessResult` limits.
- Long physical lines are read without unbounded buffering and are truncated before retention.
- Retention truncation is explicit through `IsOutputTruncated`.
- Progress receives only already-redacted output.
- Raw stdout, stderr, arguments, environment values, and caught exception text are never logged or persisted.

The process may continue after retained-output limits are reached, but both streams continue to be drained to avoid deadlock.

### Completion, timeout, and cancellation

Normal completion returns `Exited` and the actual exit code. A disallowed exit code remains observable as a process result for the later step handler to interpret; it is not converted into a thrown exception by the transport.

Timeout and caller cancellation race through one coordinated completion path. The runner calls `Kill(entireProcessTree: true)`, awaits process exit and both output pumps, then returns `TimedOut` or `Cancelled` with no exit code. Cancellation that occurs after a confirmed normal exit does not rewrite the result. Cleanup failures are scrubbed and do not expose process data.

## Guarded workspace file system

### Opening a workspace

`IFileSystem.OpenWorkspaceAsync` accepts only a validated `WorkspaceRoot`. It canonicalizes the root, verifies it is a local directory on a fixed Windows drive, rejects a reparse-point root, and returns an opaque workspace object. Opening does not create an arbitrary missing root; callers that own target creation must do so through a separately approved higher-level workflow in M5.

The workspace stores its canonical root privately. Every operation combines that root with a `WorkspaceRelativePath`, resolves the candidate, and proves the result remains under the root using ordinal-ignore-case Windows path semantics.

### Reparse-point defense

String-prefix containment is insufficient. Before an operation, the implementation walks existing path components from the root and rejects symbolic links, mount points, junctions, and other reparse points. For create operations, all existing ancestors are checked before creating the final entry. After creation/open, containment and attributes are checked again where the Windows API permits it, closing common time-of-check/time-of-use gaps.

If a component changes during validation or an operation encounters an unexpected reparse point, the operation fails closed. M3 does not follow links even when their apparent target is inside the workspace; this simpler policy is deterministic and safer for generation.

### Operation rules

- Reads open existing regular files only and reject reparse points and directories.
- Writes use explicit create modes. `overwrite=false` is the default safe path and cannot replace an existing entry. `overwrite=true` is honored only for a regular contained file and never replaces a link or directory.
- Enumeration returns immutable, normalized relative paths, never absolute paths. Recursive enumeration does not traverse reparse-point directories.
- File deletion rejects directories and reparse points.
- Recursive directory deletion requires `DirectoryCleanupIntent.RecursiveRunOwned`, rejects the workspace root itself, walks without following reparse points, and is used only for a directory already established as run-owned by a later orchestration workflow.
- Atomic finalization requires `WorkspaceMoveIntent.AtomicNoOverwriteFinalize`, contained sibling paths on the same volume, a missing destination, and no reparse points in either tree. It never merges into or overwrites an existing destination.

All methods honor cancellation between bounded operations. No implementation invokes an external deletion/copy/move command.

## Secret scanner

`WorkspaceSecretScanner` operates only through the supplied `IWorkspaceFileSystem`. It supports the existing whole-workspace and explicit-path scopes.

### Scan policy

- Whole-workspace scans enumerate guarded files recursively without following links.
- Explicit scans preserve the validated unique path list and reject missing/non-file targets deterministically.
- Known source/configuration text types are scanned. Binary detection rejects files containing null-heavy/binary prefixes.
- Per-file size and per-line limits prevent unbounded memory use. Oversized/binary/unreadable inputs produce scrubbed scan diagnostics or a fail-closed result according to whether completeness can be guaranteed; they are never silently reported as clean.
- Credential patterns cover private-key blocks, bearer/JWT forms, GitHub/OpenAI/AWS token shapes, secret-shaped assignments, connection strings, and `.env` content structures, with safe false-positive fixtures retained from M1 policy work.

Every `SecretFinding` contains only the guarded relative path, optional positive line number, and a generic redacted description such as the finding category. Matches and surrounding source text are never returned, logged, or persisted.

## Environment and IDE infrastructure

### Environment doctor

`WindowsEnvironmentDoctor` inspects only the tools needed by the current product scope. It resolves trusted executable identities and runs typed, fixed version probes through `IProcessRunner`; it does not compose arbitrary commands. Probe timeouts are short and cancellation is propagated.

The resulting `EnvironmentSnapshot` contains normalized tool identity, availability/status, version metadata, and already-redacted detail. Missing optional tools are reported as environment facts, not exceptions. Raw probe output and executable paths are not placed in diagnostics.

M2 environment-tool persistence can cache snapshots when composition is introduced, but M3's doctor remains independently correct without a cache. Cache policy and startup scheduling belong to M6 composition.

### IDE discovery and launch

M3 supports discovery/launch only for trusted IDE identities already represented by the executable catalog, initially Visual Studio Code and Visual Studio. Discovery validates local executable candidates without shelling out. `WindowsIdeLauncher` accepts an `IdeLaunchRequest`, maps `IdeId` through a closed allowlist, revalidates the workspace root, and launches it through a typed safe operation.

The workspace path is passed as one argument. No workspace content becomes command text, no user-supplied flags are accepted, and launch never requests Administrator privileges. A successful start is sufficient; DevForge does not own or kill the interactive IDE process after handoff.

If the current `IProcessRunner` lifecycle contract is unsuitable for long-lived detached IDE handoff, M3 must first capture that mismatch with a contract test and add the smallest dedicated Infrastructure-safe launch abstraction or result semantic. It must not fake launch success or force the bounded runner to retain an IDE for its full lifetime.

## Restricted template renderer

`RestrictedScribanTemplateRenderer` is the sole production implementation of `ITemplateRenderer`. Scriban 7.2.5 is pinned centrally and referenced directly only by Infrastructure. Every render uses a fresh context with empty built-ins, strict lookup, all relaxed access disabled, no loader, and a frozen ordinal-sorted graph containing strings and nested `ScriptObject` values only.

Before evaluation, `RestrictedTemplatePolicy` accepts only raw text, scalar/dotted output, string/Boolean literals, `if`/`else if`/`else`, `==`, `!=`, `&&`, `||`, `!`, and parentheses. It rejects assignment, loops, calls, built-ins, pipes, eval, includes/imports, loaders, arrays/objects, indexers, optional access, arithmetic, alternate escape modes, and every unrecognized semantic node.

The Application request bounds template/context dimensions and rejects secret-shaped names or credential-shaped values. Infrastructure bounds semantic traversal to 10,000 visits, depth to 64, and output to 4 MiB. Cancellation is checked across parse, policy, Scriban context, output writes, and return. Failures expose fixed code/message pairs only, attach no engine exception, and never contain template fragments, context data, source spans, or partial output. ADR-0006 records the complete decision.

## Error handling

- Contract preconditions remain guarded by `ValidationResult` factories.
- `ArgumentNullException` may protect programmer-only null violations at implementation entry points, but expected environmental failures do not use exceptions as normal control flow across the port.
- Access denied, not found, sharing violations, invalid handles, process-start failure, and cancellation are mapped to stable categories without raw `Exception.Message` content.
- Operations fail closed when path containment, reparse state, scan completeness, redaction, or executable trust cannot be proven.
- Cleanup never broadens its target after failure and never retries through a shell command.

## Testing strategy

### Unit and contract tests

- Architecture tests prove only Infrastructure references process-start, unguarded System.IO mutation, and relevant Windows APIs.
- Process construction tests prove executable/arguments remain separated and forbidden raw modes cannot be introduced.
- Output accumulator tests cover line/character limits, mixed stdout/stderr ordering guarantees, truncation, redaction needles, credential shapes, and safe false positives.
- Path-guard tests cover traversal, rooted/UNC/device paths, alternate data streams, reserved names, case-insensitive containment, root targeting, and overwrite intent.
- Secret-scanner classifier tests cover credential families, `.env` content, binary/oversized inputs, and descriptions that cannot contain the secret.
- Environment/IDE tests cover fixed probes, missing tools, unsupported IDs, cancellation, and no-elevation launch settings.
- Renderer tests cover the closed grammar, every forbidden AST family, request/AST/output bounds, cancellation, concurrency, culture determinism, and privacy-safe failures.

### Windows integration tests

- Run a deterministic test helper executable and prove argument boundaries preserve spaces, quotes, metacharacters, and injection-shaped text as inert arguments.
- Capture interleaved stdout/stderr, very long lines, large output, and progress while proving output remains bounded and redacted.
- Prove timeout and cancellation terminate a spawned descendant process and leave no owned process alive.
- Perform real create/read/write/enumerate/delete/move operations in test-owned temporary roots.
- Create supported symbolic-link/junction fixtures and prove root, ancestor, leaf, enumeration, move, and deletion escapes are rejected. If the host cannot create a fixture without elevation, the test records an explicit environment prerequisite rather than treating an untested escape as passing.
- Race reparse/entry changes where deterministically reproducible and add regression tests for every observed defect.
- Scan real text/binary/oversized files and assert test credentials never appear in returned objects, test output, logs, or persisted metadata.
- Probe the workspace-local .NET SDK through the real process runner and validate a guarded IDE-launch test double/helper rather than opening a user IDE during automated tests.

Test roots are explicit, resolved paths created by test fixtures. Cleanup verifies the resolved target remains inside the fixture root before recursive deletion.

## Expected implementation files

- Add process execution/resolution/redaction components under `src/DevForge.Infrastructure/Processes/`.
- Add guarded workspace components under `src/DevForge.Infrastructure/FileSystem/`.
- Add the scanner under `src/DevForge.Infrastructure/Security/`.
- Add environment and IDE components under `src/DevForge.Infrastructure/Environment/` and `src/DevForge.Infrastructure/Ide/`.
- Add the restricted renderer under `src/DevForge.Infrastructure/Templates/` and pin Scriban exactly in central package management.
- Add focused contract/unit tests under `tests/DevForge.UnitTests/Infrastructure/` and architecture tests under the existing architecture suite.
- Add real Windows tests and a deterministic process helper under `tests/DevForge.IntegrationTests/Infrastructure/` and a test-helper project only if required.
- Add ADR-0005 for guarded Windows infrastructure boundaries and ADR-0006 for the restricted Scriban runtime.
- Update `docs/implementation-plan.md`, `docs/implementation-status.md`, and `CHANGELOG.md` only with evidence actually produced.

Scriban 7.2.5 is the specification-selected renderer dependency and is centrally pinned with consistent lock files. No other package is added unless the implementation plan proves the BCL/Windows APIs are insufficient; any future package must also use an exact central pin.

## Exit gate

M3 is complete only when:

1. The production implementations of all six M3 boundaries exist and the Application/Desktop layers contain no external-process or unguarded workspace I/O implementation.
2. Process tests prove argument separation, non-shell execution, output bounds/redaction, timeout, cancellation, and descendant-tree termination.
3. File-system tests prove canonical containment, no-overwrite behavior, reparse-point escape rejection, safe enumeration, run-owned cleanup guards, and atomic no-overwrite finalization.
4. Secret-scanner tests prove supported detection, bounded scanning, fail-closed completeness, and absence of secret values in findings/output/persistence.
5. Environment and IDE tests prove only typed trusted probes/launches, guarded workspace arguments, cancellation, and non-elevated behavior.
6. Renderer tests prove the closed language, empty runtime, bounded input/AST/output, cancellation, deterministic concurrency/culture behavior, and scrubbed failures with no skipped security case.
7. Locked restore, formatting verification, Release build, full tests, and focused unit/integration/security suites pass with zero warnings and zero errors.
8. ADR-0005, ADR-0006, `docs/implementation-plan.md`, `docs/implementation-status.md`, and `CHANGELOG.md` record exact final scope and command evidence.

## Deferred scope

- M4 blueprint catalog loading, tool compatibility evaluation, planner rules, plan hashing, and deterministic plan generation.
- M5 orchestration, staging ownership, retry execution, resume/recovery decisions, and target finalization workflow.
- M6 WPF Generic Host composition, startup environment-doctor scheduling/cache policy, settings UI, and interactive safe-mode/error UX.
- M7 project template generation beyond Infrastructure primitives.
- M8 Git operations and repository workflow.
- M9 GitHub authentication and publication.
- M10 retention execution, support bundles, packaging, release signing, and operational hardening.
