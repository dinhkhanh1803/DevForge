# Changelog

All notable DevForge Studio changes are documented here.

## [Unreleased]

### Added

- Native WPF/.NET 10 solution with seven production projects and four test projects.
- Clean Architecture project-reference graph and executable architecture tests.
- Central package management, NuGet lock files, deterministic compiler policy, editor configuration, and repository ignore rules.
- Windows CI for locked restore, formatting, Release build, tests, and TRX artifact retention.
- Milestone M0 implementation plan, status tracking, ADRs, and maintainer README.
- Immutable validated M1 domain models for recipes, execution plans, run lifecycles, diagnostics, environment snapshots, reports, and privacy-safe values.
- Dependency-free blueprint manifests with identifier, semantic-version, engine-range, timeout, uniqueness, and trust validation.
- Twelve Application ports with guarded process, workspace, secret-scanning, Git, GitHub, journal, IDE, planning, execution, environment, rendering, and blueprint contracts.
- Security hardening for opaque workspace roots, canonical relative Windows paths, bounded process inputs, and Infrastructure-only access to sensitive process values.
- M2 EF Core SQLite persistence with two tracked migrations, centrally pinned 10.0.10 packages, and real fresh/upgrade integration tests.
- Privacy-safe typed settings and metadata repositories for IDEs, environment tools, blueprints, team profiles, presets, and recent projects.
- Atomic run journal persistence with guarded Domain rehydration, scrubbed corruption failures, and redacted diagnostics.
- Recoverable migration coordination using guarded SQLite online backup, integrity verification, restoration, and retained recovery artifacts.
- Raw-database privacy, cancellation, detached-snapshot, deterministic concurrency, and invalid-row regression coverage.
