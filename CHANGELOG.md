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
- Reparse-safe guarded Windows workspace operations with explicit cleanup intent and atomic no-overwrite moves.
- Trusted Windows process execution with separated arguments, bounded redacted streaming output, timeout/cancellation, and descendant-tree termination.
- Bounded workspace secret scanning, fixed environment probes, and trusted non-elevated IDE handoff.
- M3 adversarial coverage for real junctions, locked files, structured credentials, output-observer failures, and continuous-output cancellation.
- Restricted Scriban 7.2.5 template rendering with a closed conditional grammar, string-only isolated contexts, bounded AST/output, cancellation, deterministic concurrency/culture behavior, and scrubbed failures.
- M4 guarded blueprint package loading with bounded YAML/JSON parsing, complete checksums, trust/quarantine policy, and atomic catalog snapshots.
- Closed typed compatibility rules, deterministic input/default validation, single-pass planning variables, and privacy-safe tool/process preview evidence.
- Immutable ordered execution plans and validators with canonical UTF-8 JSON plus mutation-sensitive lowercase SHA-256 plan hashes.
- Recoverable M5 run checkpoints and run-owned staging with atomic ownership claims, canonical privacy-safe markers, cross-run leases, guarded resume validation, and cleanup race protection.
- Exact M5 blueprint reopening through the guarded M4 loader with current trust rechecks, immutable verified execution bytes, cancellation-safe publication, and privacy-safe failures.
- Closed trust-scoped M5 handler dispatch and bounded one-pass typed runtime placeholder materialization without reflection or direct process access.
- Guarded M5 create/render/overlay and JSON/YAML/XML handlers with plan-hashed renderer context, strict reparse-before-publish transforms, atomic file replacement, exact `.env` policy, and transient-only retries.
- Closed M5 process and validator handlers with separated trusted executables/arguments, bounded redacted progress and evidence digests, safe Node-backed package-manager resolution, lifecycle-script suppression, pre/postcondition probing, timeout/cancellation classification, and fresh-staging replay for opaque process mutations.
- Process-wide M5 checkpointed orchestration with plan-first state persistence, bounded retry/manual resume, durable cancellation, postcondition-driven skip/rerun, exact persisted-mode checks, recoverable staging replay swaps, and trusted handler retry policies included in canonical plan hashes.
- Validated M5 completion with ordered quality gates and secret scanning, bounded privacy-safe JSON/Markdown reports, marker-verified no-overwrite finalization, exact ordinal detached-copy verification, explicit payload bounds, and finalized staging cleanup.
- Authoritative M5 startup recovery with a shared process-wide execution gate, durable interruption normalization, explicit guarded resume/cleanup, stale-checkpoint protection, and real SQLite app-kill/marker/fingerprint recovery coverage.
