# Milestone M3 Core Infrastructure Implementation Plan

**Goal:** Deliver the Windows-native Infrastructure implementations required before planning and generation workflows.

**Status:** Complete on 2026-08-10; all implementation tasks and the fresh exit gate passed.

**Architecture:** Application retains its validated contracts. Infrastructure owns Windows process/file/security/environment/IDE effects and returns only contained, bounded, redacted results. Domain and Desktop remain free of OS implementation details.

**Tech stack:** .NET SDK 10.0.302, C# 14, Windows BCL APIs, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-10-m3-core-infrastructure-design.md`
- Task plan: `docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md`
- Decision: `docs/decisions/0005-guarded-windows-infrastructure-boundaries.md`

## Scope

M3 includes `IProcessRunner`, `IFileSystem`/`IWorkspaceFileSystem`, `ISecretScanner`, `IEnvironmentDoctor`, and `IIdeLauncher` implementations plus real Windows integration/security tests.

M3 excludes blueprint catalog/planner work, orchestration, WPF composition/UI, project templates, Git, GitHub, packaging, and cloud/AI integrations.

## Planned files

- Add process components under `src/DevForge.Infrastructure/Processes/`.
- Add guarded workspace components under `src/DevForge.Infrastructure/FileSystem/`.
- Add secret scanning under `src/DevForge.Infrastructure/Security/`.
- Add environment and IDE components under `src/DevForge.Infrastructure/Environment/` and `src/DevForge.Infrastructure/Ide/`.
- Add source-policy architecture tests under `tests/DevForge.UnitTests/` without adding an Infrastructure reference.
- Add real Windows tests and a deterministic helper under `tests/DevForge.IntegrationTests/` and `tests/DevForge.ProcessTestHelper/` if required by RED tests.
- Update ADR-0005, status, plan, and changelog only with verified evidence.

## Tasks

- [x] Protect the OS-effect architecture boundary.
- [x] Implement canonical, reparse-safe guarded workspace operations.
- [x] Implement redacted bounded output and the trusted Windows process runner.
- [x] Implement bounded workspace secret scanning.
- [x] Implement typed environment probes and trusted IDE handoff.
- [x] Harden injection, traversal, link-race, cancellation, locked-file, and privacy behavior.
- [x] Run locked restore, format, Release build, full tests, and focused M3 security suites.
- [x] Record exact evidence and mark M3 complete only after every gate is green.

## Exit gate

M3 passes only when all five ports are production-backed; process, filesystem, scanner, environment, and IDE security tests are genuinely executed with no skipped escape test; full locked restore/format/build/test is green; Release build has zero warnings/errors; and exact results are recorded in `docs/implementation-status.md`.
