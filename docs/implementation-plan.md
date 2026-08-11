# Milestone M3 Core Infrastructure Implementation Plan

**Goal:** Deliver the Windows-native Infrastructure implementations required before planning and generation workflows.

**Status:** Complete; full six-boundary verification passed on 2026-08-11.

**Architecture:** Application retains its validated contracts. Infrastructure owns Windows process/file/security/environment/IDE effects and returns only contained, bounded, redacted results. Domain and Desktop remain free of OS implementation details.

**Tech stack:** .NET SDK 10.0.302, C# 14, Windows BCL APIs, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-10-m3-core-infrastructure-design.md`
- Task plan: `docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md`
- Renderer closure design: `docs/superpowers/specs/2026-08-10-m3-restricted-template-renderer-closure-design.md`
- Renderer closure plan: `docs/superpowers/plans/2026-08-11-m3-restricted-template-renderer-closure.md`
- Decision: `docs/decisions/0005-guarded-windows-infrastructure-boundaries.md`
- Renderer decision: `docs/decisions/0006-restricted-scriban-template-runtime.md`

## Scope

M3 includes `IProcessRunner`, `IFileSystem`/`IWorkspaceFileSystem`, `ISecretScanner`, `IEnvironmentDoctor`, `IIdeLauncher`, and the restricted `ITemplateRenderer` implementation plus real Windows integration/security tests. The first five boundaries passed their 2026-08-10 checkpoint; the renderer is the active closure item discovered during the full DOCX audit.

M3 excludes blueprint catalog/planner work, orchestration, WPF composition/UI, project templates, Git, GitHub, packaging, and cloud/AI integrations.

## Planned files

- Add process components under `src/DevForge.Infrastructure/Processes/`.
- Add guarded workspace components under `src/DevForge.Infrastructure/FileSystem/`.
- Add secret scanning under `src/DevForge.Infrastructure/Security/`.
- Add environment and IDE components under `src/DevForge.Infrastructure/Environment/` and `src/DevForge.Infrastructure/Ide/`.
- Add the restricted Scriban renderer under `src/DevForge.Infrastructure/Templates/` and harden its Application request contract.
- Add source-policy architecture tests under `tests/DevForge.UnitTests/` without adding an Infrastructure reference.
- Add real Windows tests and a deterministic helper under `tests/DevForge.IntegrationTests/` and `tests/DevForge.ProcessTestHelper/` if required by RED tests.
- Update ADR-0005/ADR-0006, status, plan, and changelog only with verified evidence.

## Tasks

- [x] Protect the OS-effect architecture boundary.
- [x] Implement canonical, reparse-safe guarded workspace operations.
- [x] Implement redacted bounded output and the trusted Windows process runner.
- [x] Implement bounded workspace secret scanning.
- [x] Implement typed environment probes and trusted IDE handoff.
- [x] Harden injection, traversal, link-race, cancellation, locked-file, and privacy behavior.
- [x] Run locked restore, format, Release build, full tests, and focused M3 security suites.
- [x] Record exact evidence and mark M3 complete only after every gate is green.
- [x] Implement the restricted template renderer closure through the approved TDD plan.
- [x] Rerun the full M3 exit gate and replace the earlier five-port completion claim with fresh six-port evidence.

## Exit gate

M3 passes only when all six ports are production-backed; process, filesystem, scanner, environment, IDE, and restricted-renderer security tests are genuinely executed with no skipped security test; full locked restore/format/build/test is green; Release build has zero warnings/errors; and exact results are recorded in `docs/implementation-status.md`.
