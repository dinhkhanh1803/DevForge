# ADR-0001: Native WPF and Clean Architecture Boundaries

- Status: Accepted
- Date: 2026-07-31
- Milestone: M0

## Context

DevForge Studio must be a native Windows desktop application on .NET 10. The specification prohibits web shells, Electron, Tauri, Blazor Hybrid, embedded browsers, AI APIs, and cloud backends. M0 must establish project boundaries before product behavior is introduced.

## Decision

Use one native WPF executable project targeting `net10.0-windows` and six platform-neutral production projects targeting `net10.0`.

The allowed production dependency graph is:

```text
Application -> Domain, Blueprints.Abstractions
Infrastructure -> Application, Domain, Blueprints.Abstractions
Blueprints.BuiltIn -> Blueprints.Abstractions
Desktop -> Application, Infrastructure
Cli -> Application, Infrastructure
Domain -> none
Blueprints.Abstractions -> none
```

Desktop and CLI are composition roots. Direct access to process and file-system APIs from presentation code remains prohibited; those abstractions and guarded implementations belong to M3.

## Alternatives considered

1. A single WPF project would scaffold faster but could not protect domain and infrastructure boundaries.
2. A separate bootstrapper/composition-root project would isolate Infrastructure from Desktop more strictly, but it is not part of the required solution structure and adds M0 complexity without product value.
3. The chosen full solution skeleton matches the specification and allows executable dependency tests immediately.

## Consequences

- Project-reference rules can fail fast in tests and CI.
- Desktop and CLI may reference Infrastructure only to compose implementations; business workflows remain in Application.
- M0 assemblies intentionally contain no product contracts or behavior, preventing accidental overlap with M1.

