# DevForge Studio Implementation Status

**Current milestone:** M1 - Domain & Contracts  
**Status:** In progress  
**Last updated:** 2026-07-31
## Active scope

M1 is limited to immutable domain models, blueprint manifest contracts, validation/error results, and the twelve core Application ports. Planner/rule/hash behavior, infrastructure implementations, persistence, blueprint catalog content, Git/GitHub automation, and UI expansion remain deferred.

The approved design is recorded in `docs/superpowers/specs/2026-07-31-m1-domain-contracts-design.md`; the task-level plan and exit gate are recorded in `docs/superpowers/plans/2026-07-31-m1-domain-contracts.md`.

## M1 baseline evidence

- Repository: branch `codex/m0-baseline`; there was no commit before the M1 design checkpoint and no remote is configured.
- Focused baseline test exited 0 with 31 passed, 0 failed, 0 skipped.
- A sandboxed full-solution test attempt produced no output and exited 1 after the host stalled. The focused test then succeeded outside the sandbox, isolating this as an environment/named-pipe limitation rather than a repository test failure.

## Scope delivered

M0 delivers the reproducible repository baseline: a .NET 10 solution with seven production projects and four test projects, Clean Architecture project references, shared compiler/analyzer policy, Central Package Management with lock files, repository hygiene, a minimal WPF shell and CLI host, executable architecture/quality tests, Windows CI configuration, ADRs, README, changelog, and milestone tracking.

Product behavior is intentionally deferred. M0 adds no domain workflows, persistence, guarded file/process implementations, orchestration, production blueprints, Git/GitHub automation, or completed UI.

## TDD evidence

- Initial RED: the first five architecture tests referenced the wished-for `RepositoryModel` test helper and failed with `CS0246` because the helper did not exist.
- Initial GREEN: implementing the test-only repository/project loader made all five architecture tests pass.
- Quality regression RED: tests reproduced acceptance of missing project-reference and solution paths, missed both `VersionOverride` XML forms, and proved the diagnostics/path-order APIs were absent.
- Quality regression GREEN: unresolved paths are rejected, both `VersionOverride` forms are detected, and discovery/diagnostics are deterministic.
- Holistic guard RED: wished-for solution-set and central-package diagnostics failed with `CS0117`; focused fixtures covered unexpected solution membership, disabled CPM, duplicate central names, floating/ranged/latest versions, uncovered package references, and missing lock files.
- Holistic guard GREEN: production and test reference allowlists, exact solution membership, CPM configuration, exact central versions, package coverage, and all 11 lock files are enforced.
- Final guard RED: discovered test-project access failed with `CS1061`; subsequent fixtures reproduced acceptance of duplicate/conditional CPM declarations and rejection of exact child `Version` metadata.
- Final guard GREEN: the discovered test-project set is exact regardless of naming or solution membership, CPM has one unconditional `true` declaration, and attribute/child central versions share exact-version validation.
- Diagnostic-label RED/GREEN: scoped overload calls failed with `CS1501`; parameterized diagnostics now accurately label production, test, and solution project failures. The focused suite contains 31 passing tests.

## Exit gate evidence

Fresh local verification on 2026-07-31 used SDK 10.0.302 and completed in this order:

| Gate | Command | Exact result |
| --- | --- | --- |
| Restore | `dotnet restore DevForge.sln --locked-mode` | Exit 0; all projects up-to-date (the preceding clean restore restored all 11 projects). |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no diagnostics or output. |
| Build | `dotnet build DevForge.sln --configuration Release --no-restore` | Exit 0; 11 projects built; 0 warnings, 0 errors. |
| Full test | `dotnet test DevForge.sln --configuration Release --no-build` | Exit 0; 31 passed, 0 failed, 0 skipped in `DevForge.UnitTests`; the three future-milestone test hosts contain no tests yet. |
| Focused architecture/unit test | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 31 passed, 0 failed, 0 skipped. |

## Environment

Verification used the workspace-local SDK at `.tools/dotnet/dotnet.exe` with `DOTNET_CLI_HOME=.tools/dotnet-cli-home`, `NUGET_PACKAGES=.tools/nuget-packages`, and telemetry, logo, and first-time experience disabled. No Administrator privileges or machine-wide SDK installation were required.

## Known limitations

- The Windows GitHub Actions workflow is present and mirrors the mandatory quality gates, but CI has not been run remotely in this task.
- Integration, blueprint, and end-to-end test projects are intentionally empty hosts until their owning milestones.
- Product behavior remains deferred to later milestones as listed under Scope delivered.

## Next milestone

M1 - Domain & Contracts may start. Its exit gate must prove domain value objects, contracts, validation, serialization behavior, and their focused tests before M1 is marked complete.
