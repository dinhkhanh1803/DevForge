# M8 Git and GitHub Completion Design

## Scope

M8 turns an authoritative `LocalReady` checkpoint into a recoverable Git/GitHub completion. It enables reviewed Git intent in Create Project, initializes the finalized project with the Git CLI, creates the fixed initial commit, applies the `main` or `main + develop` policy, optionally publishes a private-by-default GitHub repository through `gh`, and exposes durable `PublishPending`/`Completed` evidence in the native WPF flow.

M8 does not add production blueprints, CI workflows, support bundles, arbitrary remotes, custom commit messages, force push, remote deletion, repository overwrite, token handling, shell commands, or an AI/cloud backend.

## Authoritative boundary

Git/GitHub completion is an Application-owned post-finalization workflow. It consumes the persisted checkpoint and its integrity-bound `PlanPreview.Git`; it never accepts mutable UI Git options after review. Infrastructure opens only the already-finalized target through the guarded workspace factory. Desktop receives immutable status, receipt, and remediation projections and never constructs a workspace or process request.

Blueprint actions do not dispatch Git/GitHub side effects in M8. The reserved built-in action identifiers remain closed policy vocabulary, while the completion workflow is the single production owner of post-quality-gate repository mutation. This prevents a package from reordering Git ahead of validators, finalization, secret scanning, or report persistence.

## Lifecycle and durable state

The checkpoint gains a bounded publication snapshot with these independent phases:

- Git: `NotRequested`, `IntentPersisted`, `RepositoryInitialized`, `Committed`, `Succeeded`, `Failed`.
- GitHub: `NotRequested`, `IntentPersisted`, `RemoteCreated`, `Succeeded`, `Failed`.
- Receipt: `NotRequested`, `IntentPersisted`, `Succeeded`, `Failed`.
- Safe evidence: final-tree digest captured by finalization, local initial commit SHA, exact branch policy, fixed `github.com` account/repository identity, validated HTTPS repository URL, canonical receipt reference, and receipt-body SHA-256. No token, credential, source content, raw process output, user home path, or environment value is persisted.

Rules:

1. Only a checkpoint with successful finalization, report persistence, and the exact final-tree digest returned by the finalizer may start completion. Legacy M7 checkpoints without that digest remain inspectable `LocalReady` and require a new reviewed run before publication.
2. If Git initialization was not reviewed, the run remains `LocalReady`; M8 does not invent `Completed` evidence.
3. If Git was reviewed, acquire a guarded cross-process publication lease, reload the authoritative checkpoint, and persist `LocalReady -> PublishPending` plus Git intent before invoking Git.
4. After verified clean repository/branch/commit evidence, either transition directly to `Completed` when GitHub publish was not reviewed, or persist GitHub intent and publish.
5. Before intent, require `.git` to be absent. Before every commit or push, re-enumerate the final workspace with the same bounded canonical project-tree algorithm, require the finalizer digest to match, and run a fresh bounded secret scan. After successful DevForge initialization, only that M8-owned root `.git` metadata directory is excluded from the project-tree digest and source scan; nested or pre-existing `.git` entries are rejected and every other file remains covered. Drift or a finding fails closed before Git/GitHub mutation.
6. GitHub failure, timeout, cancellation, or ambiguous recovery retains the local project and durable `PublishPending`. Retry resumes only the incomplete completion phase.
7. `PublishPending -> Completed` requires verified local Git receipt and, when requested, verified remote receipt.
8. Cancellation is persisted with `CancellationToken.None` before propagation. It never rolls back or deletes the finalized project.

The existing in-process execution activity gate is shared by generation, recovery, and publication. Infrastructure additionally acquires an OS-exclusive, guarded publication lease before checkpoint reload or target mutation. The lease is keyed by run ID under the run-artifact workspace, carries no source data, survives only through its open handle, and is released by process termination. Contention fails before mutation. Reacquisition always reloads the authoritative checkpoint, preventing stale writers across DevForge processes.

## Git CLI policy

All commands use `IProcessRunner` with `ExecutableIdentity("git")`, a guarded working workspace, and a separately constructed `ArgumentList`. No `cmd /c`, PowerShell, shell expansion, arbitrary executable, or administrator path is allowed.

Every Git invocation receives a fixed minimal environment that disables system/global configuration and terminal prompting. DevForge supplies an empty global/system config boundary, disables templates, hooks, fsmonitor, pagers, credential prompts, GPG signing, and automatic line-ending conversion, and supplies the fixed local author `DevForge Studio <devforge@localhost>`. The newly created repository is checked for unexpected local configuration before each mutation. With ambient config excluded and templates/hooks disabled, blueprint `.gitattributes` cannot activate an externally configured clean/smudge filter. Tests place malicious system/global/template/hook/filter settings in the parent environment and prove no indirect process is invoked.

The closed operation sequence is:

1. Preflight `git --version` through the trusted runner and isolated Git environment.
2. Refuse a pre-existing repository when no matching durable M8 receipt exists.
3. `git init --initial-branch=main`.
4. `git add --all`.
5. `git commit --message "chore: bootstrap project with DevForge"`.
6. Read the exact commit with `git rev-parse HEAD`, require a canonical 40/64-hex object ID, require `git status --porcelain=v1` to be empty, and bind that commit to the persisted final-tree digest.
7. For `main + develop`, create `develop` from the initial commit and restore `main`; verify the exact local branch set contains the required branches.

Raw output is bounded and redacted by the runner. Errors expose stable scrubbed codes only.

Local recovery is phase-specific and starts by matching the project tree/scan and the isolated Git configuration. After persisted Git intent it accepts only: no `.git`; or a pristine DevForge-initialized repository with no commit, tag, remote, unexpected config, or unexpected branch. A partially populated index is safe to replace by the deterministic `git add --all` only while the repository has no commit and source-tree integrity still matches. After commit may have completed, recovery accepts only one parentless commit whose tree, fixed message, fixed author, and committer policy match the reviewed bootstrap, with no tag or remote. During branch setup it accepts only a subset of the reviewed `main`/`develop` branches, all pointing to that same commit, and restores `main`. It persists each adopted phase before continuing. Any extra commit, branch, tag, remote, config, object identity, dirty tracked path, or missing expected repository fails closed; M8 never deletes or resets an ambiguous repository and never creates a second bootstrap commit. Tests terminate after init, add, commit, and each branch mutation but before checkpoint persistence and prove exact adoption.

## GitHub CLI policy

GitHub authentication is delegated to `gh`; DevForge never requests, reads, stores, logs, or invokes `gh auth token`. The automated boundary uses fixed host `github.com`, `gh auth status --active`, and the authenticated login identity for preflight. It reports remediation directing the user to the official `gh auth login`/`gh auth switch` flow when needed.

The reviewed plan captures a strict bounded GitHub account name and repository name; M8 supports personal-account publication only. The active `github.com` login must exactly match that reviewed account before create, recovery, or push. Organization publication is deferred rather than inferred from ambient account state. DevForge constructs the only accepted remote as `https://github.com/{account}/{repository}.git`; SSH and configured protocol selection are not accepted.

The `gh` runner environment is also minimal and typed: fixed `GH_HOST=github.com`, prompts/pagers/color disabled, and only the OS user-config location required for `gh` to locate its existing authentication file. The path is passed directly to the child process, is never persisted or logged, and its configuration content is never read by DevForge. No authentication material is placed in arguments or environment values.

Publication is private unless the reviewed plan explicitly selected public visibility. Repository names use a strict bounded GitHub-safe grammar derived from the reviewed project name. The service refuses a pre-existing unrelated `origin` and never force-pushes, deletes, transfers, renames, or changes visibility of a remote.

Before creation, the checkpoint stores a cryptographically random, bounded 128-bit ownership nonce. The first attempt creates the exact empty `account/repository` through `gh` without allowing `gh` to select a remote protocol and sets the repository description to the opaque `DevForge ownership <nonce>` marker in that same create operation. An already-existing repository is never modified to add the marker. DevForge then adds the constructed HTTPS `origin` and uses ordinary upstream pushes for required branches. The marker contains no run ID, local path, user identity, or source data and remains as the MVP repository description so clearing it cannot create a new recovery ambiguity.

Recovery reconciles the exact reviewed repository and requires the persisted ownership nonce to match the remote description before adoption or mutation. It accepts only: an exact nonce-owned empty remote; or a nonce-owned remote whose branch refs are a subset of the reviewed required branches and whose present refs all equal the persisted local initial commit; or the fully matched required branch set. It then adds/verifies the HTTPS origin and pushes only missing matching branches without force. Any missing/mismatched nonce, unexpected ref, commit, identity, visibility, protocol, or pre-existing repository before durable create intent fails closed in `PublishPending` with scrubbed manual remediation. This distinguishes a killed successful create from an unrelated pre-existing empty repository and covers termination after remote creation, after origin creation, and after any individual branch push.

## Persistence and reports

SQLite receives a versioned migration for the canonical publication snapshot and its SHA-256 body checksum. Mapping rejects undefined states, oversized JSON, non-canonical JSON, invalid tree/commit/receipt hashes or URLs, plan-intent mismatch, and `Completed` without the required receipts. Older M7 checkpoints decode as `NotRequested` and remain `LocalReady`; they are not silently completed.

The canonical JSON/Markdown generation reports remain immutable M5 artifacts. M8 deterministically creates the bounded canonical publication receipt body, then persists `Receipt.IntentPersisted` with its guarded relative reference and SHA-256 before any file write. It writes through atomic no-overwrite publication, re-reads the exact body, and finally persists `Receipt.Succeeded`. Recovery adopts an orphan written after intent only when its canonical bytes exactly match the persisted hash; an absent file is written, while a partial, replaced, non-canonical, or mismatched file fails closed without overwrite. Every load verifies the receipt before presenting `Completed`. The receipt contains run ID, plan hash, final-tree digest, Git state, branch policy, commit SHA, GitHub state, reviewed account/repository/visibility, and repository URL when available.

## Desktop behavior

Create Project exposes Git initialization (default on), branch policy, optional GitHub publish (default off), and private visibility (default on). Any change invalidates the reviewed plan. Plan Preview shows the exact immutable choices.

Execution Center continues from generation into completion. `PublishPending` presents Retry Publish and safe auth/remediation text; it never claims source was lost. `Completed` presents local target, plan/blueprint evidence, commit SHA, branches, exact receipt references, and repository URL when published. Safe mode disables every Git/GitHub mutation while retaining inspection.

## Security and failure matrix

Tests cover argument separation and injection strings; hostile global/system/local config, templates, hooks, filters and environment; deterministic author identity; pre-existing/nested `.git` refusal; exact final-tree drift and fresh secret scan; existing repo/origin/remote refusal; fixed account/host/HTTPS protocol; private default; branch policies; dirty-tree rejection; auth/network/timeout/cancellation; cross-process lease contention; app-kill after each durable phase; nonce-owned empty/partial/complete remote reconciliation; mismatched/missing nonce refusal; receipt-orphan adoption/mismatch; no force-push/delete/token command; redacted bounded output; checkpoint and receipt tampering; guarded target drift/reparse refusal; safe-mode refusal; and retry without duplicated commit or generation evidence.

## Exit gate

M8 exits only when locked restore, format verification, Release build, full solution tests, focused Git/GitHub/persistence/Desktop tests, migration consistency, architecture/privacy scans, and a real local Git integration fixture are green. GitHub network tests use a deterministic fake runner; no real remote is created or pushed by the test suite.
