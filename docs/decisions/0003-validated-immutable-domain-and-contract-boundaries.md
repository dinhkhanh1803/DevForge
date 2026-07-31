# ADR 0003: Validated Immutable Domain and Contract Boundaries

**Status:** Accepted  
**Date:** 2026-07-31

## Context

M1 must define recipes, blueprint manifests, execution/run snapshots, errors, and application ports without implementing infrastructure or weakening the M0 project graph. UI and CLI callers also need multiple actionable validation messages rather than the first constructor exception.

The security baseline requires separated process arguments, guarded file operations, and no secret-bearing persistence or logging contracts.

## Decision

- Domain aggregates and blueprint manifests use guarded factories that return immutable validation results.
- Successful construction snapshots caller-owned collections into immutable collections.
- Domain and Blueprint Abstractions keep independent, small validation primitives so both assemblies remain dependency-free.
- Application owns ports that coordinate Domain and Blueprint Abstractions.
- `CommandSpec` represents the executable and arguments separately and carries explicit redaction values.
- File operations are exposed through a root-scoped `IWorkspaceFileSystem`; operations accept only validated relative paths.
- Models reject secret-shaped recipe input names and expose only redacted diagnostic context.
- M1 defines planner, rule, retry, and orchestration contracts only. Their behavior remains in the milestones assigned by the specification.

## Consequences

- Invalid aggregate instances cannot be obtained through supported factories.
- UI and CLI layers can display all validation issues deterministically.
- Collection snapshots cannot be changed by mutating caller-owned lists or dictionaries.
- Infrastructure implementations must satisfy security-shaped interfaces rather than accepting arbitrary shell or path strings.
- Blueprint and domain validation primitives have intentional small duplication in exchange for preserving the dependency graph.
- Canonical path/link enforcement and secret content scanning remain implementation obligations for later milestones.

