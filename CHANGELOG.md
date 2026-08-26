# Changelog

All notable DevForge Studio changes are documented here.

## [Unreleased]

### Added

- M10 canonical bounded JSONL diagnostics with guarded atomic daily/run streams, credential revalidation at serialization, exact ownership markers, a shared bounded cross-process writer/retention lease, validated 30-day/256-MiB defaults, typed partial cleanup results, and normal-startup lifecycle composition.
- Deterministic privacy-safe support bundles with a closed evidence catalog, normalized bounded UTF-8, per-entry SHA-256 inventory, marker-owned staging and atomic publication, exact kill-window recovery, and marker-plus-digest-authorized idempotent cleanup.

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
- Native M6 WPF/Generic Host shell with a persistent closed navigation rail, functional Dashboard, Settings onboarding, Environment Doctor, System/Light/Dark themes, and read-only startup safe mode.
- Typed M6 settings validation, 15-minute non-concurrent environment cache, startup migration/recovery sequencing, bounded notifications, architecture/privacy gates, and WPF scaling smoke coverage.
- M7 reviewed-plan project creation with schema-driven Text/Choice/Boolean/WholeNumber controls, exact plan invalidation, guarded absent-target preflight, canonical privacy-safe presets, and immutable run/recipe identity binding.
- Native M7 Plan Preview, Execution Center, Blueprint Catalog, Run History, and LocalReady evidence with exact recovery eligibility, bounded redacted progress, accessible virtualized collections, and safe IDE handoff.
- Real M4/M5 desktop composition with safe-mode write refusal, fingerprint-bound recovery, checksum-bound persisted previews, and Application-owned run continuation/cleanup.
- Test-only TrustedLocal M7 E2E generation covering guarded create/render/copy, file/content validators, secret scan, canonical JSON/Markdown reports, no-overwrite finalization, cancellation, and duplicate-free resume to `LocalReady` without terminal execution.
- M8 reviewed Git intent bound to presets, recipe, preview, canonical plan hash, durable checkpoint, and native WPF completion controls with Git-on, publish-off, private-on safe defaults.
- Production local Git completion through a closed `IProcessRunner` vocabulary with isolated config/hooks/credentials, exact final-tree verification, fixed parentless bootstrap commit, clean-tree enforcement, and precise init/add/commit/branch kill-window recovery.
- Fixed-account `github.com` publication through typed `gh`/Git operations with private-by-default creation, ownership nonce, exact origin/commit/branch verification, and nonce-owned empty/partial/complete remote recovery without token observation.
- Cross-process publication lease, cancellation-independent durable phase checkpoints, recoverable `PublishPending`, atomic integrity-bound receipts, exact orphan adoption, and retry without duplicate generation or commit.
- Native Desktop one-button completion, bounded remediation and Retry Publish, Run History recovery, evidence-backed `Completed`, and safe-mode publication refusal.
- Composed M8 E2E coverage for trusted generation and validation through real local Git completion, deterministic private fake-GitHub interruption/retry, durable receipts, clean re-verification, and read-only Git-object cleanup in guarded temporary fixtures.
- Exactly three checksummed M9 production blueprints for native .NET 10 WPF, React/Vite/TypeScript, and Python CLI with pinned locks, deterministic plans and trees, truthful seven-document team handoff, and certified closed tool vocabularies.
- Engine-owned canonical recipe, lock, generation report, and policy snapshot evidence with integrity binding, tamper refusal, atomic recovery, and exact final-tree/publication digest preservation.
- Consolidated cross-blueprint release contracts for actual Desktop discovery, changed reviewed inputs, deterministic output snapshots, failure recovery, no-overwrite targets, forbidden execution surfaces, and production local-Git verification without remote contact.
- React production-output policy that commits integrity-bound deterministic `dist` artifacts, excludes only engine-owned evidence from Prettier, and remains clean after a repeated real Vite build.
- M10 hostile-input release matrix covering traversal/device/UNC/GLOBALROOT/reserved paths, shell/download/installer identities, privileged handler intent, non-executable trust, and secret-shaped nested keys.
- Guarded local-data root provisioning through `IFileSystem` with pre-mutation and post-creation ancestor reparse checks.

### Fixed

- Desktop Release startup no longer lets a built-in blueprint `App.xaml` shadow DevForge's WPF application resource, and read-only Settings checklist indicators now use explicit one-way bindings.
- Blueprint inspection now rejects executable and package-manager identities outside the typed trusted tool catalog before planning or execution.
- Architecture discovery ignores transient WPF compiler `_wpftmp` projects so release build/test sequencing cannot report a generated project as production source.

### Known release gates

- M9 implementation and the real Windows 10 matrix are green. The approved Windows 11 WPF/React/Python release matrix is accepted carry-forward environmental debt while M10 is implemented, but both milestone release gates remain open until it passes.
