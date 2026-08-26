# DevForge Studio M10 Release Checklist

**Milestone state:** Open. M11 remains blocked.

Evidence timestamp for this local checkpoint: `2026-08-26T16:30:00+07:00`. Local host: Microsoft Windows 10.0.19045 x64; SDK 10.0.302; runtime 10.0.10. A Pass row is backed by the exact linked command/test evidence. A Pending row cannot be changed without new observed evidence.

| Gate | Requirement | Status | Evidence | Host | Timestamp | Blocker |
|---|---|---|---|---|---|---|
| Build | Locked restore, scoped format, and all 12 Release projects with zero warnings/errors. | Pass | `dotnet restore DevForge.sln --locked-mode`; `dotnet format DevForge.sln --verify-no-changes --no-restore`; `dotnet build DevForge.sln -c Release --no-restore`; [implementation status](implementation-status.md#m10-entry-and-current-scope) | Windows 10.0.19045 x64 | 2026-08-26T16:30:00+07:00 | None for local gate. |
| Recovery | Interrupted execution/publication and packaged database upgrade failure recover from durable evidence without overwrite. | Pass | `RunRecovery*`, `M8*`, `MigrationRecoveryTests`, and `ReleaseUpgradeTests`; [ADR-0023](decisions/0023-self-contained-win-x64-release.md) | Windows 10.0.19045 x64 | 2026-08-26T16:30:00+07:00 | None for local gate. |
| Security | Hostile input, path/reparse, command, secret, archive, auth, ownership, and no-admin matrices fail closed. | Pass | `M10HostileInputMatrixTests`, Infrastructure boundary/security/privacy suites; [ADR-0020](decisions/0020-owned-local-diagnostics-and-retention.md) | Windows 10.0.19045 x64 | 2026-08-26T16:30:00+07:00 | None for local gate. |
| Blueprints | Exactly three production packages pass contracts, deterministic composition, real toolchains, and Git-clean matrix on every supported release host. | Pending | Local WPF/React/Python matrix in [implementation status](implementation-status.md); `ProductionBlueprintReleaseMatrixE2ETests` | Windows 11 release host (not available) | 2026-08-26T16:30:00+07:00 | Required Windows 11 WPF/React/Python matrix not executed. |
| UX | Native diagnostics need no terminal; keyboard, failure focus, and 100/125/150% scaling pass on supported release host. | Pending | `DesktopDiagnosticsTests`, `DesktopAccessibilityTests`, `WpfResourceSmokeTests`; [ADR-0022](decisions/0022-authoritative-desktop-diagnostics-actions.md) | Windows 11 release host (not available) | 2026-08-26T16:30:00+07:00 | Real-display Windows 11 keyboard/DPI smoke not executed. |
| Data | EF model matches tracked migrations; fresh/upgrade/restore preserve integrity and privacy. | Pass | `SqliteSchemaTests`, `SqliteMigrationUpgradeTests`, `MigrationRecoveryTests`, `PersistencePrivacyTests`, packaged `ReleaseUpgradeTests` | Windows 10.0.19045 x64 | 2026-08-26T16:30:00+07:00 | None for local gate. |
| Documentation | User, maintainer, blueprint author, troubleshooting, privacy, ADR, status, and checklist contracts contain observed behavior only. | Pass | `ReleaseDocumentationContractTests`; [documentation index](../README.md#documentation) | Repository artifact | 2026-08-26T16:30:00+07:00 | None for local artifact gate. |
| Packaging | Audited self-contained `win-x64` directory starts fresh, upgrades, restores failure, and passes pinned remote CI/Windows 11 release run. | Pending | Local `dotnet publish`; `Test-ReleasePackage.ps1` = 565 files/3 roots; 9 Release tests; [ADR-0023](decisions/0023-self-contained-win-x64-release.md) | Windows 11 and GitHub Actions (not observed) | 2026-08-26T16:30:00+07:00 | Pinned CI job and Windows 11 package matrix not yet observed. |

## Local test evidence

- Unit: 651 passed, 0 failed, 0 skipped.
- Integration: 601 passed, 0 failed, 0 skipped.
- Blueprint: 127 passed, 0 failed, 0 skipped.
- E2E: 216 passed, 0 failed, 0 skipped.
- Package audit: 565 files, exactly 3 blueprint roots.

## Remaining release-host commands

On a real Windows 11 x64 release host, execute the locked restore/format/build/four-suite gate, `ProductionBlueprintReleaseMatrixE2ETests`, real keyboard and 100/125/150% Desktop smoke, fixed-profile publish/audit, and `ReleaseUpgradeTests`. Separately observe the pinned `release-package` GitHub Actions job. Record exact host/build, timestamps, commands, counts, and artifact digest before changing any Pending row.

## External evidence audit

At `2026-08-26T17:11:08+07:00`, the release controller performed a read-only external-closure audit from commit `10087b023bc47a4b9e86af594ea22f3f58d25046` on branch `codex/m4-m11-completion`:

- the branch has no configured upstream;
- `origin` is `https://github.com/dinhkhanh1803/DevForge.git`;
- `git ls-remote --heads` advertised no matching `codex/m4-m11-completion`, `main`, or `master` head;
- GitHub CLI 2.97.0 reported the active `dinhkhanh1803` credential invalid, so no authenticated workflow evidence could be read;
- the available host remained Microsoft Windows 10.0.19045 and exposed no Hyper-V, VirtualBox, or VMware CLI with which to run the required Windows 11 matrix.

This audit changes no gate to Pass. Remote publication is not part of this closure attempt, and no branch or repository was pushed or created. External closure still requires a real Windows 11 x64 release host plus an authenticated, observed pinned `release-package` workflow run for the exact reviewed commit.
