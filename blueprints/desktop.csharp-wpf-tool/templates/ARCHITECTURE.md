# Architecture

Dependencies point inward:

```text
TeamTool.Desktop -> TeamTool.Infrastructure -> TeamTool.Application -> TeamTool.Domain
TeamTool.Desktop ---------------------------> TeamTool.Application
```

`Domain` contains business values. `Application` contains use-case contracts. `Infrastructure` implements operating-system concerns. `Desktop` is the native WPF composition and presentation layer. View models use CommunityToolkit.Mvvm and do not perform direct filesystem or process operations.

## Boundaries

Domain has no outward project dependency; Application depends only on Domain; Infrastructure implements Application contracts; Desktop is the composition root and native UI.

## Repository layout

Production projects are under `src`, tests are under `tests`, and deterministic SDK/package policy remains in root-level files.

## Decision records

No project-specific ADRs exist at generation time. Future accepted ADRs must use repository-relative links to records stored under `docs/decisions/`.
