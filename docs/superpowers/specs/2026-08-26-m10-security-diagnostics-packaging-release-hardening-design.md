# M10 Security, Diagnostics, Packaging, and Release Hardening Design

**Status:** Approved through the owner's standing instruction to use the recommended safe direction without further confirmation.

**Milestone boundary:** M10 only. M11 catalog expansion, automatic updates, cloud telemetry, deployment, arbitrary script execution, real GitHub mutation, and support for additional blueprints remain excluded.

## Entry condition and carried debt

M9 implementation and the complete available-host Windows 10 matrix are green. The specification requires the three production blueprint matrices on Windows 11 before the M9 release gate can be called complete. No suitable Windows 11 runner or VM is available on this host. The owner explicitly requested M10 after that limitation was recorded, so M10 may proceed with the Windows 11 certification as accepted environmental technical debt under section 18.1 of the detailed baseline. M9 remains open and no Windows 11 evidence is inferred.

M10 cannot close until both the outstanding M9 Windows 11 matrix and every M10 Must release gate have exact evidence.

## Chosen approach

Implement M10 as six independently reviewable, fail-closed slices:

1. Security closure and hostile fixtures.
2. Structured local logging and bounded retention.
3. Privacy-safe support bundles and owned cleanup.
4. Desktop diagnostics, accessibility, and scaling closure.
5. Self-contained `win-x64` packaging and upgrade verification.
6. Cross-cutting release checklist and milestone closure.

This sequence puts the security and privacy boundaries in place before diagnostics expose artefacts and before packaging freezes the release image. Packaging-first and one-shot hardening were rejected because they would either distribute known incomplete behavior or produce a review surface too broad to verify precisely.

## Architecture and ownership

### Application contracts

Application owns immutable, bounded requests and results for diagnostics, retention, support-bundle export, and release evidence. Contracts carry typed identifiers and guarded workspace capabilities rather than arbitrary paths. They expose only redacted text and explicit allowlisted artefact categories.

The Application layer coordinates policy but never calls `System.IO`, `Process`, WPF, EF Core, or platform shell APIs. Safe mode permits read-only diagnostics export only when its input can be verified without mutation; cleanup and other mutation remain refused.

### Infrastructure

Infrastructure implements:

- a structured JSON-lines log sink with fixed fields, normalized control characters, redaction before persistence, bounded message/event sizes, daily and run-specific files, and atomic writes where a complete artefact is required;
- deterministic retention over the local-data workspace, constrained by age and total-byte ceilings, with ownership verification and no traversal/reparse escape;
- a support-bundle writer that builds a deterministic ZIP from an explicit allowlist of scrubbed recipe/plan/journal/result/log/tool/manifest/report data and a generated inventory; source files, `.env`, credentials, raw environment values, database files, and auth-token output are forbidden;
- packaging/release verification that uses MSBuild and existing typed process boundaries, not shell strings or runtime downloads.

All product file operations go through guarded filesystem abstractions. Any newly discovered direct production file operation in the M10 scope must be migrated behind those abstractions or recorded as a blocking defect; it cannot be normalized as technical debt. External commands continue through `IProcessRunner` with a closed executable identity and separate `ArgumentList`.

### Desktop

Execution Center and Run History receive presentation-safe diagnostics snapshots and capabilities. “Open Staging”, “Open Folder”, “Copy Log”, and “Export Support Bundle” are enabled only when the corresponding typed action is available. Cleanup remains an owned recovery operation and never accepts a path from a ViewModel.

The UI keeps status icon plus text, deterministic focus on failures, keyboard navigation, automation names, and readable layouts at 100%, 125%, and 150%. No embedded browser, terminal window, or direct process/file access is introduced.

### Packaging and release

The supported first package is framework-dependent development output plus a self-contained `win-x64` release directory. Single-file, signing, installer/updater, and automatic update checks are deferred unless the specification's self-contained package cannot otherwise run on a clean supported machine. Blueprint content, native dependencies, configuration defaults, licenses/notices, version metadata, and release documentation must be present and validated from the published directory.

An upgrade scenario starts from a prior local database fixture, takes the existing migration backup path, launches the new package, verifies schema/data preservation, and proves safe-mode behavior on injected migration failure. Package tests must run from an isolated publish directory so repository-local SDK/configuration cannot mask missing release files.

## Data flow

1. A run emits bounded structured events containing only typed metadata and `RedactedText`.
2. The logging boundary normalizes and redacts each event before writing the daily sink and, when a run ID exists, the run sink.
3. Retention enumerates only known local-data subtrees, calculates deterministic candidates, verifies ownership, and deletes oldest eligible artefacts until both configured limits are satisfied.
4. Support export resolves a run identifier to authoritative persisted snapshots, reads only allowlisted diagnostic artefacts, scans every candidate for forbidden names/content, writes an inventory with hashes, and atomically publishes one ZIP under `support-bundles`.
5. Desktop receives only the export receipt or a redacted `DevForgeError`; it does not receive raw archive contents or filesystem handles.
6. Release automation publishes the Desktop project self-contained for `win-x64`, audits the output, runs packaged startup/upgrade smoke checks, and records exact checklist evidence.

## Failure and recovery rules

- A log write failure never exposes the rejected payload. The app reports one bounded diagnostic and continues only if durable logging is not required for an active release gate.
- Retention cancellation stops between candidates. A failed delete leaves other artefacts untouched and returns a redacted partial result; finalized customer projects are never candidates.
- Support-bundle creation uses a run-owned temporary directory and atomic final publish. Cancellation or failure leaves no claimed bundle and cleanup is limited to the owned temporary directory.
- A candidate containing forbidden names, secret-shaped values, invalid UTF-8 where text is expected, excessive size, or an out-of-root/reparse path is omitted or blocks export according to severity; the inventory records only a safe reason code.
- Packaging fails on missing blueprint content, unexpected executables/scripts, mutable/wildcard package references, missing version metadata, architecture violations, or a failed startup/upgrade smoke.
- No release status can be manually forced to green. Every Must item references a command, test, or reviewed artefact.

## Security and privacy invariants

- No token, password, connection string, private key, `.env` content, raw environment value, `gh auth token` output, or customer source enters logs, SQLite, support bundles, reports, command previews, or UI notifications.
- Malicious blueprint fixtures cover traversal, rooted/device/UNC paths, symlink/junction escape, checksum changes, unsupported actions, arbitrary PowerShell/shell, executable download, admin/registry/firewall/service intent, oversized controls, duplicate fields, log injection, and secret leakage.
- Destructive cleanup requires both a guarded local-data root and a matching ownership marker. UI strings are never deletion authority.
- The release package has no outbound telemetry, AI/cloud backend, embedded browser, or hidden updater.

## Testing strategy

Every task follows RED -> smallest GREEN -> focused regression -> full gate.

- **Unit:** policy validation, retention ordering/limits, support inventory canonicalization, log normalization/redaction, release checklist state.
- **Integration:** guarded daily/run log writes, concurrency, rotation/retention, support ZIP bytes and atomic recovery, ownership/reparse refusal, migration upgrade/backup/safe mode, packaged content audit.
- **Security:** injection/traversal/junction/malicious packs/log injection/secret corpus, archive slip, oversized/binary input, no source/database/token leakage.
- **Desktop/E2E:** capability enablement, safe-mode refusal, keyboard/automation/focus contracts, scale-safe XAML constraints, packaged startup from an isolated publish directory.
- **Release:** locked restore, format, Release build, all four test projects, EF pending-model check, self-contained publish, package audit, clean-machine-equivalent startup and upgrade smoke, static forbidden-surface scan, and checklist completeness.

## Task exits

### Task 1 — Security closure

All mandatory hostile fixtures fail before unsafe file/process execution, the secret corpus is redacted across every diagnostic surface, and the scoped static audit finds no direct shell/admin/AI/cloud/unguarded-file escape.

### Task 2 — Structured logging and retention

Daily and run-specific JSONL logs contain every required field, remain bounded, survive concurrent writers, and retention enforces configured age/size limits without touching unowned/finalized data.

### Task 3 — Support bundle and cleanup

A deterministic, integrity-inventoried, scrubbed bundle can be exported and recovered atomically; forbidden data and archive/path attacks are rejected; cleanup is marker-authorized and idempotent.

### Task 4 — Desktop diagnostics and accessibility

Happy-path diagnostics require no terminal, actions are capability/safe-mode correct, failure focus and remediation work, and automated contracts plus manual smoke evidence cover keyboard and 100/125/150-percent scaling.

### Task 5 — Packaging and upgrade

An isolated self-contained `win-x64` package contains exactly the required runtime/blueprint/docs assets, starts without a repo-local SDK dependency, and passes fresh/upgrade/migration-failure smoke tests.

### Task 6 — Release closure

Every MVP release-checklist Must item has exact evidence; all tests have zero failure and zero silent skip; documentation is complete; the outstanding Windows 11 M9/M10 matrix is executed and green. Only then may M10 be marked complete and M11 recommended.

## Documentation deliverables

- `docs/implementation-plan.md` and `docs/implementation-status.md`
- a detailed M10 implementation plan under `docs/superpowers/plans`
- ADRs for diagnostics/privacy retention and release packaging
- user guide, maintainer guide, blueprint author guide, troubleshooting guide, privacy/support-bundle guide, and release checklist
- `CHANGELOG.md` entries containing only observed behavior and gates

