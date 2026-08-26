# DevForge Studio Troubleshooting

## First response

Preserve the project target, DevForge local-data directory, staging marker, database backups, and publication receipts. Do not run as Administrator, delete markers, edit checkpoints, force-push, or paste credentials into issue reports. Open Run History, note the redacted error code, and create a Support bundle when the action is available.

## Remediation codes

| Code | Meaning | Safe action |
|---|---|---|
| `DF-FS-001` | A local-data or project-root path could not be opened safely. | Verify the path is a canonical local-drive directory, remove no markers, and retry without elevation. |
| `DF-FS-002` | A workspace-relative path or owned operation was rejected. | Inspect blueprint paths/checksums and use a new absent target if ownership is uncertain. |
| `DF-PROC-001` | A trusted executable was unavailable or could not start. | Run Environment Doctor and install/repair the documented tool outside DevForge. |
| `DF-PLAN-001` | Inputs, compatibility, or deterministic rules are invalid. | Return to Create Project, correct the highlighted field, and review a new plan. |
| `DF-BP-002` | Blueprint checksum or action policy failed. | Do not trust or execute the package; restore it from a reviewed source. |
| `DF-EXEC-003` | Execution was interrupted or durable recovery is required. | Use Run History Resume/Retry only when enabled; otherwise preserve evidence. |
| `DF-SECRET-001` | Secret scanning found prohibited content. | Remove the credential from inputs/generated content, rotate it externally, and retry from a clean run. |
| `DF-SUPPORT-001` | The support-bundle request or evidence could not be verified. | Refresh Run History and export from the authoritative saved run. |
| `DF-GIT-004` | The final Git tree does not match verified project evidence. | Do not publish; inspect local changes and create a new reviewed run if necessary. |
| `DF-GH-001` | GitHub CLI account/authentication verification failed. | Use `gh auth login/status/switch` directly, confirm the reviewed account, then retry publication. |
| `DF-PUB-READONLY` | Publication was requested in safe mode. | Resolve startup recovery first; keep the local project and receipt unchanged. |

## Recovery

For an interrupted run, reopen DevForge and let startup recovery finish. Use only enabled Run History actions. `PublishPending` means the local project remains available; retry publication does not regenerate files. If safe mode appears after database migration, retain every `devforge.backup-upgrade-*.db` file and collect support evidence before manual repair.

## Package startup

Keep the full self-contained directory together. Missing `coreclr.dll`, `hostfxr.dll`, runtime JSON, `docs`, or `blueprints\built-in` means the package is incomplete. Do not copy individual DLLs from another .NET installation. Reacquire the complete audited directory.

## Privacy during support

Do not attach a database, customer project tree, `.env`, private key, token output, or raw terminal transcript. Use the generated bundle plus its copied SHA-256 receipt. See [Privacy and support bundles](privacy-and-support-bundles.md).
