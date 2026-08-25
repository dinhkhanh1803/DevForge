# ADR-0014: Recoverable evidence-backed Git and GitHub completion

**Status:** Accepted

**Date:** 2026-08-25

## Context

M7 deliberately ended generation at `LocalReady`. M8 must turn the exact reviewed and finalized project into a clean local repository and, when explicitly selected, a personal GitHub repository without rerunning generation, trusting mutable UI state, exposing credentials, or adopting unrelated local/remote state. Publication can be interrupted after any irreversible Git, GitHub, checkpoint, or receipt effect, so an in-memory workflow cannot establish terminal completion safely.

## Decision

- The persisted reviewed plan hash and `PlanPreview.Git` remain authoritative. Desktop passes only the run identity and mutation mode to the Application publication workflow.
- Successful finalization persists a canonical SHA-256 digest of the exact project path/byte tree before `LocalReady`. Publication reopens guarded workspaces, rescans secrets, and requires the same digest before Git mutation.
- Local Git supports only the closed init/add/fixed parentless commit/status/ref/branch/origin vocabulary through `IProcessRunner`. Executable and arguments remain separate; system/global config, hooks, templates, filters, pagers, prompts, credentials, signing, and line-ending mutation are isolated.
- GitHub publication is fixed to the exact reviewed personal `github.com` account and repository. It defaults private, requires an explicit reviewed public choice, binds repository creation to a persisted 128-bit ownership nonce, and uses typed `gh`/Git operations only.
- The Application coordinator holds the shared activity gate and a guarded OS-exclusive per-run lease. It persists intent and every irreversible phase with cancellation-independent writes, normalizes recoverable interruption to `PublishPending`, and retries publication without invoking generation.
- Recovery adopts only exact evidence: the single expected local commit and branch set; the nonce-owned empty, partial, or complete remote with the exact commit; and a byte-identical atomic publication receipt. Drift, unrelated state, or receipt mismatch fails closed without overwrite, force push, remote deletion, or repository adoption.
- `Completed` requires persisted and revalidated Git evidence, optional GitHub evidence, and an integrity-bound receipt. Safe-read-only mode refuses publication before mutation.
- Automated GitHub coverage is deterministic and local; it never creates or contacts a real GitHub repository. Real-account acceptance remains an explicit manual activity outside the automated suite.

## Consequences

Publication is restartable across application/process termination and produces durable evidence that can be projected by Create Project and Run History. Local Git completion works without a terminal, while optional GitHub publication uses the user's existing verified `gh` session without storing or logging a token. Recovery is intentionally conservative: unexpected repository or remote state requires manual inspection instead of automatic repair.

The closed vocabulary does not support arbitrary remotes, organization repositories, forks, repository adoption, force push, remote deletion, custom commit identity/message, custom hooks, or custom branch policies. Production blueprint delivery remains M9; CI, packaging, support bundles, and release hardening remain M10.

## Rejected alternatives

- Running Git or GitHub commands from Desktop, through a shell string, or with Administrator privileges.
- Reading `gh auth token`, persisting credentials, or inheriting ambient credential/configuration state.
- Treating `LocalReady`, a successful process exit, or remote existence alone as proof of completion.
- Replaying generation during publication retry.
- Adopting an existing repository/remote without the exact commit, branch, origin, account, visibility, nonce, and receipt evidence.
- Force-pushing, deleting, overwriting, or auto-repairing unexpected state.
