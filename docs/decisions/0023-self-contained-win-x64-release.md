# ADR-0023: Self-contained win-x64 release directory

- Status: Accepted
- Date: 2026-08-26

## Context

The MVP requires a Windows-native package that runs without a repo-local SDK, preserves the local SQLite upgrade/recovery contract, carries exactly the reviewed production blueprint catalog, and can be audited before distribution. A single-file bundle, installer, updater, signing workflow, or additional RID would expand the release and recovery surface beyond M10.

## Decision

The first release artifact is a self-contained, non-single-file, non-ReadyToRun `win-x64` directory. The publish profile pins version 1.0.0, assembly/file version 1.0.0.0, and embedded debug metadata. `win-x64` is declared in shared build policy and represented in every checked-in NuGet lock graph, allowing both normal solution restore and release-RID restore to remain locked.

Blueprint publish content consists of one explicit catalog README and three root-bounded globs: `desktop.csharp-wpf-tool`, `web.react-vite-ts`, and `tool.python-cli`. Desktop adds only exact README and CHANGELOG release documents. The audit accepts only the canonical repository artifact directory, requires the app/runtime/config/docs and blueprint manifests/checksums, rejects database, credential/key, shell-script, and support-bundle payloads, and writes a deterministic receipt before upload.

Packaged startup accepts either no arguments or exactly `--local-data-root` plus one absolute descendant of the test-owned `%TEMP%\DevForge-ReleasePackageTests` boundary. The latter remains path-validated by Desktop composition and exists only to prove fresh/upgrade/recovery behavior in an isolated root; drive roots and unrelated absolute paths fail before host construction. Package E2E starts the EXE directly, waits for a responsive main window, and closes normally. It verifies fresh creation, historical-data-preserving migration with backup, and injected migration failure with restoration and visible safe mode.

## Consequences

- Release output is larger than framework-dependent or single-file deployment but has no installed-runtime dependency.
- Runtime/native transitive assets are now explicitly locked for `win-x64`.
- Adding a RID or blueprint root requires an explicit build-policy, lock, audit, test, and ADR change.
- Signing, installer/updater, single-file, trimming, and ReadyToRun remain deferred.
- Local Windows 10 evidence does not satisfy the required Windows 11 release matrix; remote CI and Windows 11 results remain Task 6 gates.
