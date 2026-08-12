# Milestone M6 Desktop Shell Implementation Plan

**Goal:** Compose M0-M5 into a production-quality native WPF shell with settings onboarding, cached Environment Doctor, Dashboard, theming, and safe startup.

**Status:** M6 complete and verified locally; M7 is recommended next.

**Architecture:** `DevForge.Desktop` is the WPF/Generic Host composition root. ViewModels depend on contracts and Desktop presentation services; all file, process, persistence, migration, and recovery effects remain behind existing abstractions.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.10, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-12-m6-desktop-shell-settings-environment-doctor-design.md`
- Task plan: `docs/superpowers/plans/2026-08-12-m6-desktop-shell-settings-environment-doctor.md`
- Decisions: `docs/decisions/0010-native-wpf-host-and-theme.md`, `docs/decisions/0011-desktop-startup-safe-mode.md`

## Delivered scope

- [x] Activate the native WPF and Generic Host dependency boundary with exact central package pins.
- [x] Add the persistent closed-route shell navigation and bounded notifications.
- [x] Persist validated typed settings and Settings-based first-run onboarding.
- [x] Implement System/Light/Dark semantic resource themes with Windows theme observation.
- [x] Add cached, cancellation-aware, non-concurrent Environment Doctor orchestration with a 15-minute TTL.
- [x] Aggregate read-only Dashboard recent projects, presets, actionable runs, and environment state.
- [x] Sequence migration, recovery, settings, theme, environment, Dashboard, and initial navigation through Generic Host.
- [x] Provide read-only safe mode after migration/recovery failure without writes or scans.
- [x] Build functional Dashboard, Settings, and Environment Doctor views; keep M7 routes visibly disabled.
- [x] Add architecture, behavior, composition, SQLite startup, privacy, accessibility, and WPF scaling tests.
- [x] Record the fresh full serialized exit gate and commit milestone closure.

## Exit gate

M6 is complete. SDK, locked restore, format, Release build, full serialized solution tests, focused Desktop tests, EF model consistency, and clean-diff checks succeeded with zero failed or skipped tests. The recommended next milestone is M7 - Dynamic Form Engine and Project Creation Wizard.
