# ADR-0010: Native WPF host, shell, and theme boundary

**Status:** Accepted

**Date:** 2026-08-12

## Context

M6 must expose the verified M0-M5 capabilities through a native Windows desktop shell while preserving MVVM, Clean Architecture, deterministic startup, accessibility, and the prohibition on web-based presentation technologies.

## Decision

- `DevForge.Desktop` is a native WPF application targeting `net10.0-windows`; Electron, Tauri, Blazor Hybrid, embedded browsers, and web shells remain prohibited.
- .NET Generic Host is the composition and lifecycle boundary. Views and ViewModels never use a service locator and do not directly access files, processes, EF Core, or Infrastructure concrete types.
- The shell uses a persistent left navigation rail with a closed route set. Dashboard, Environment Doctor, and Settings are active in M6; M7 routes remain visibly disabled with an availability reason.
- First-run onboarding lives in Settings as a concrete checklist. It is not a separate wizard or placeholder workflow.
- Theme preference is `System`, `Light`, or `Dark`. System follows the current Windows application theme; explicit Light or Dark choices remain stable.
- Semantic WPF resource dictionaries are replaced atomically on the dispatcher. Views use dynamic semantic brushes instead of hard-coded colors.
- The shell maintains keyboard navigation, named accessibility targets, icon-plus-text status, a 960x640 minimum usable layout, and scalable grid/scroll layouts verified at 100%, 125%, and 150% equivalents.

## Consequences

M6 provides a testable native shell without weakening project dependency rules. Future workflow pages plug into the closed navigation and host composition boundaries, while presentation remains independent of direct operating-system effects.

## Rejected alternatives

- A web or embedded-browser desktop shell.
- A third-party navigation framework or control suite.
- Global service location from Views or ViewModels.
- A separate onboarding wizard with duplicated settings state.
- Hard-coded per-View colors or process/file access from Desktop presentation code.
