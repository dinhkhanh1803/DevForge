# ADR-0005: Guard Windows infrastructure boundaries

**Status:** Accepted

**Date:** 2026-08-10

## Context

M3 must execute trusted development tools, manipulate generated workspaces, scan files for secrets, inspect the local environment, and launch trusted IDEs. These are the first DevForge features that cross operating-system trust boundaries. Raw command strings, string-prefix-only path checks, link following, unbounded output, and secret-bearing diagnostics would make later generation and publication workflows unsafe by construction.

## Decision

- Only Infrastructure may start processes or implement workspace file mutations.
- External tools are selected by `ExecutableIdentity`; each argument is added separately through `ProcessStartInfo.ArgumentList`.
- Shell execution, elevation, `cmd /c`, arbitrary PowerShell, and raw evaluation modes are forbidden.
- Process output is drained asynchronously, redacted before progress/retention, and bounded by the existing Application contracts.
- Timeout and cancellation terminate the entire owned process tree and await stream completion.
- Workspace operations start from an opaque validated local-drive root and a `WorkspaceRelativePath`.
- Every operation proves canonical root containment and rejects reparse-point roots/components; M3 follows no links.
- Writes, recursive cleanup, and directory finalization require explicit intent and do not overwrite existing destinations by default.
- Secret scans return only guarded relative location, optional line, and category-only redacted descriptions.
- Environment probes and IDE launches use closed typed catalogs; they accept no arbitrary flags or executable paths.
- The BCL and Windows APIs are preferred. Any new package requires a failing test demonstrating necessity and an exact central version pin.

## Consequences

The Infrastructure code is more explicit and Windows-specific, but the security rules become independently testable and Application/Desktop stay free of OS effects. Reparse points are rejected even when they target another location inside the workspace; this intentionally trades flexibility for deterministic containment. Interactive IDE launch needs a bounded handoff path rather than pretending a long-lived IDE is a completed command.

## Rejected alternatives

- Raw executable names or command strings supplied by UI, recipes, blueprints, or persistence.
- `cmd.exe`, PowerShell, shell association, or Administrator elevation for normal execution.
- Quoting and concatenating one command-line string.
- Accepting path containment based only on textual prefix comparison.
- Following junctions or symbolic links after resolving their target.
- Returning raw stdout, stderr, exception messages, matched secret text, or environment values.
- A monolithic infrastructure service combining process, file, scan, environment, and IDE responsibilities.
