# M6 Desktop Shell, Settings, and Environment Doctor Design

**Status:** Approved

**Date:** 2026-08-12

## Objective

M6 composes the existing M0-M5 capabilities into a native Windows WPF application shell on .NET 10. It delivers production-quality application lifecycle, MVVM navigation, first-run Settings onboarding, Dashboard, Environment Doctor, theme persistence, and a read-only startup safe mode. It does not implement the dynamic Create Project or execution workflow assigned to M7.

## Source and fixed choices

This design follows the complete `DevForge_Studio_Codex_Implementation_Specification_V1.0.docx`, its Markdown companion, existing ADRs, and the verified M0-M5 implementation.

The user approved these M6 choices:

- A persistent left navigation rail is the shell layout for M6-M11.
- First run opens Settings with an onboarding checklist instead of a separate wizard.
- Theme defaults to the current Windows theme and supports persisted Light or Dark overrides.
- Environment Doctor cache is valid for 15 minutes. Startup scans only when stale, and the user can always request a rescan.
- Desktop remains a single feature-organized project. A separate presentation assembly is not added in M6.

## Scope

M6 includes:

- .NET Generic Host bootstrap, dependency injection, startup sequencing, graceful shutdown, and cancellation.
- WPF MVVM shell with persistent left rail, page title/focus management, navigation, notification, and dialog boundaries.
- Dashboard with Create New Project entry point, recent-project summary, runs needing action, and environment health.
- Settings with first-run checklist, default project root, default IDE, default team profile, culture, and theme.
- Environment Doctor with cached snapshots, stale-on-startup scanning, explicit rescan, per-tool status, timestamps, and remediation.
- Read-only safe mode when startup migration cannot complete safely.
- Light and Dark WPF resource dictionaries with System theme tracking.
- Desktop/ViewModel, composition, accessibility, architecture, security, and selected UI automation tests.

M6 excludes:

- Dynamic blueprint input forms, project recipe authoring, plan preview, Execution Center, Completed, and functional Run History; these are M7.
- Git or GitHub behavior; this is M8.
- Production blueprints; these are M9.
- Support bundles, packaging, installers, and release hardening; these are M10.
- Any web shell, embedded browser, AI API, cloud backend, telemetry, arbitrary shell string, or direct Desktop file/process execution.

## Architecture

`DevForge.Desktop` remains the WPF composition and presentation project. It references Application and Infrastructure because the composition root must bind Application ports to Infrastructure implementations, but presentation classes depend only on Application/Domain contracts and Desktop-owned presentation services.

The Desktop project is organized by responsibility:

- `Hosting`: host construction, registrations, startup coordinator, lifecycle, safe-mode state.
- `Navigation`: navigation route model, service, shell destination descriptors, focus target contract.
- `Services`: theme, notifications, dialogs, dispatcher/throttling, and Windows theme observation.
- `Features/Shell`: `MainWindow`, persistent rail, shell ViewModel.
- `Features/Dashboard`: Dashboard View and ViewModel.
- `Features/Settings`: Settings View, ViewModel, typed settings mapping, onboarding checklist.
- `Features/Environment`: Doctor View, ViewModel, cached scan coordinator, tool rows.
- `Themes`: shared tokens, Light, Dark, controls, and typography ResourceDictionaries.

No View or ViewModel resolves services through a global service locator. Views receive ViewModels through host composition. ViewModels use injected interfaces and expose observable state and commands through CommunityToolkit.Mvvm.

## Application lifecycle and startup transaction

`App.xaml` has no `StartupUri`. `App.OnStartup` creates a cancellation source, builds the Generic Host, starts it asynchronously, runs the startup coordinator, resolves and shows `MainWindow`, and reports only scrubbed failures. `App.OnExit` cancels outstanding work, awaits host shutdown, disposes the host, and never blocks through `.Result` or `.Wait()`.

Startup order is fixed:

1. Build and start the host.
2. Run the existing recoverable database migration coordinator.
3. If migration succeeds, run interrupted-run recovery from M5.
4. Load typed application settings and apply the effective theme/culture.
5. Load cached environment snapshots and start a background scan only when the newest relevant snapshot is older than 15 minutes or absent.
6. Navigate to Settings onboarding when required configuration is incomplete; otherwise navigate to Dashboard.
7. Show the shell only after its initial route and safe-mode state are coherent.

An interrupted or failed migration enters read-only safe mode. Safe mode exposes scrubbed startup error details, theme/culture settings that do not require database mutation, and diagnostics guidance. It disables generation, resume, cleanup, persistence writes, and any navigation whose correctness depends on migrated storage. It never deletes or rewrites the database outside the existing migration recovery abstraction.

## Shell and navigation

The shell uses the approved persistent left rail. Each destination includes an icon, text label, accessibility name, route, enabled state, and disabled reason. Status is never communicated by color alone.

M6 functional destinations are Dashboard, Environment Doctor, and Settings. Create Project, Execution Center, and Run History may appear as future destinations only when they are visibly disabled and labeled as unavailable until M7. They cannot invoke placeholder business behavior.

Navigation is a Desktop-owned service with a closed route set. It prevents overlapping transitions, updates the selected rail item and page title atomically, and moves keyboard focus to the new page heading after navigation. It does not serialize Views, access Infrastructure, or accept arbitrary type names.

The window supports keyboard traversal, access keys, high-DPI scaling, minimum usable dimensions, and 100%, 125%, and 150% display scaling. Layout uses WPF grid/star sizing rather than fixed pixel positioning for content regions.

## Design system and themes

The UI follows the specification's 4 px spacing grid, 16-24 px card padding, readable 10-11 pt body typography, 14-20 pt headings, and monospace resources for future logs/code. Shared controls include status badges, navigation rows, cards, field messages, tool status rows, path display, notifications, and empty states.

All colors and brushes live in WPF ResourceDictionaries. Views use semantic dynamic resources such as background, surface, text, muted text, border, primary, success, warning, error, info, focus, and selection. No View hard-codes theme colors.

Theme preference values are `System`, `Light`, and `Dark`. `System` is the default and follows Windows theme changes while the application runs. Light or Dark overrides remain stable until changed. The selected preference is persisted through typed settings.

## Settings and onboarding

Settings owns the first-run experience. Its onboarding checklist clearly distinguishes required and optional configuration. Required M6 configuration is:

- A validated default project root represented through the existing guarded database/path contracts rather than an arbitrary path used for operations.
- A supported default IDE selection or an explicit `None` selection.
- A valid culture selection from the closed supported set.
- A valid theme preference.

The default team profile is optional until M7 consumes it. GitHub login, Git configuration, and blueprint-specific settings remain outside M6.

Settings loads immutable snapshots, edits a ViewModel draft, validates inline, and writes only after explicit Save. Cancel restores the last persisted snapshot. Save is disabled while invalid or already saving. Successful first-run Save marks onboarding complete only when every required item is valid, then navigates to Dashboard.

Settings never stores tokens, passwords, connection strings, `.env` content, raw exceptions, or tool command output. Paths are not written to logs or UI diagnostics beyond the values the user explicitly sees in their settings fields.

## Environment Doctor

Environment Doctor uses the existing `IEnvironmentDoctor` and environment-tool persistence contracts. Desktop adds only orchestration for cache freshness, presentation state, cancellation, and throttled UI updates.

At startup, cached results are shown immediately. If all required cached entries are fresh within 15 minutes, no automatic scan runs. Missing or stale required entries trigger one background scan. Explicit Rescan cancels and awaits the prior scan before starting a replacement, and repeated clicks cannot create concurrent scans.

Each tool row includes tool name, icon and text status, detected version, compatibility summary, last scanned timestamp, and scrubbed remediation. Supported presentation statuses are Installed/Compatible, Missing, Outdated, Conflicting, and Unknown, mapped from the existing domain snapshot without inventing executable paths or raw output.

A failure to inspect one tool does not crash the page or erase other cached results. That row becomes Unknown with a stable scrubbed error/remediation. Whole-scan cancellation preserves the previous durable cache and returns the UI to a non-busy state.

M6 never installs or upgrades tools automatically. Remediation is guidance only. Opening official download pages is deferred unless already represented by a safe typed port; Desktop does not launch arbitrary URLs or processes directly.

## Dashboard

Dashboard presents:

- A primary Create New Project entry point that is disabled with a clear M7 availability label in M6.
- Environment health summary with scan timestamp and navigation to Environment Doctor.
- Recent-project summaries from the existing store, including a clear empty state.
- Runs requiring action, including interrupted, validation-failed, failed, and publish-pending summaries supported by persisted status.
- Saved-preset summary only when real persisted presets exist; it does not fabricate sample content.

Dashboard aggregation is read-only and cancellation-aware. A recent project whose path no longer exists is presented as unavailable. M6 does not probe the file system directly from Desktop, automatically remove the record, or crash.

## Error, notification, and safe-state policy

User-facing failures use scrubbed `DevForgeError` content: code, summary, and suggested actions. Raw exception text and stack traces are not primary UI messages. Unexpected exceptions are captured at the Desktop boundary, passed to scrubbed local logging when configured, and shown as a generic actionable notification.

Notifications are bounded, deduplicated, and announced to accessibility clients without flooding the dispatcher. Dialogs are reserved for decisions with meaningful consequences. Ordinary navigation, scan, and save operations do not use confirmation dialogs.

Busy state disables only conflicting actions. Cancellation remains available for Environment Doctor scans and application shutdown. Page ViewModels do not retain stale cancellation sources after completion.

## Responsiveness and threading

No Desktop code uses `.Result`, `.Wait()`, synchronous process reads, or long-running work on the dispatcher. All startup, database, recovery, and doctor work is awaited. Observable progress is throttled before dispatcher publication. Commands prevent reentrancy and expose deterministic busy/cancel state.

Shutdown cancels startup or scans, waits for bounded cooperative completion, then stops the host. M6 adds no independent process or file APIs; existing Infrastructure implementations remain responsible for their own bounded cancellation.

## Package and dependency policy

M6 adds exact centrally pinned package versions for:

- `CommunityToolkit.Mvvm`.
- `Microsoft.Extensions.Hosting` and only the minimum related Microsoft.Extensions packages required by the host composition.

Desktop does not adopt a third-party navigation framework, control suite, web renderer, or DI container. Package versions use `Directory.Packages.props` and locked restore.

## Testing strategy

Unit tests cover:

- Startup ordering and routing to onboarding, Dashboard, or safe mode.
- Navigation selection, disabled reasons, reentrancy, and focus requests.
- Settings load/edit/validate/save/cancel and onboarding completion.
- System/Light/Dark preference restoration and Windows-theme change response.
- Environment cache TTL at exact boundaries, stale startup scan, explicit rescan, cancellation, per-tool failure, and concurrency refusal.
- Dashboard empty, populated, unavailable-project, run-action, and safe-mode states.
- Notification bounds/deduplication and scrubbed error projection.

Composition and integration tests cover:

- Generic Host resolves every M6 View, ViewModel, navigation service, theme service, startup coordinator, and existing Infrastructure binding.
- Host start/stop and cancellation complete without deadlock.
- Real SQLite migration/settings/environment-cache round trips through the startup pipeline.
- M5 interrupted recovery executes before initial navigation.
- Migration failure selects read-only safe mode without unauthorized writes.

Architecture tests cover:

- Desktop Views/ViewModels do not use `System.Diagnostics.Process`, `System.IO.File`, `System.IO.Directory`, EF Core, or arbitrary Infrastructure implementations.
- Infrastructure construction is limited to the Desktop composition root.
- Views contain no business orchestration and ViewModels have no WPF control dependencies.
- All new package versions are centrally pinned and locked.

Selected WPF UI automation/smoke tests cover:

- Shell launch, left-rail keyboard navigation, focus order, access keys, onboarding Save, Environment Doctor Rescan, and clean shutdown.
- 100%, 125%, and 150% scaling without clipped navigation, fields, status text, or primary actions.
- Light and Dark resource application and icon-plus-text status representation.

Security/privacy tests scan settings storage, notifications, and logs for credential-shaped values, `.env` content, connection strings, raw exceptions, and unredacted command output.

## Exit gate

M6 is complete only when:

- SDK pin, locked restore, format verification, Release build, full solution tests, focused Desktop/ViewModel/architecture/integration tests, and selected UI smoke tests pass with zero skipped M6 tests.
- Generic Host startup and shutdown are correct and cancellation-safe.
- Startup migration/recovery/settings/theme/doctor routing is verified in its fixed order.
- Migration failure produces read-only safe mode without data loss.
- The dispatcher remains responsive during startup and scans.
- Keyboard navigation, focus order, icon-plus-text status, Light/Dark themes, and 100/125/150% scaling pass.
- Desktop performs no direct file or external-process operation.
- No secret, `.env`, connection string, raw exception, or unredacted output is stored or displayed.
- `docs/implementation-plan.md`, `docs/implementation-status.md`, ADR, and changelog contain exact command evidence.

## Deferred work

M7 will activate Create Project, Plan Preview, Execution Center, Completed, and Run History workflows using this shell and navigation boundary. M8 adds Git/GitHub. M9 adds production blueprints. M10 owns support bundles, logging retention, packaging, and release hardening. M11 expands the catalog only after M10 gates pass.
