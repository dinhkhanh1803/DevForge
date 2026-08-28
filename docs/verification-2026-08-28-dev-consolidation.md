# Dev branch consolidation — 2026-08-28

## Scope and safety

User requested consolidation of local milestone branches, cleanup, and publication
of all source changes to `origin/dev`. This is development integration, not release
promotion. M9/M10 external gates and the PostgreSQL prerequisite hold remain open.

- Preserve all existing implementation, candidate, test and documentation changes.
- Do not publish ignored tools, caches, generated projects, credentials or build outputs.
- Normalize the two import-order failures identified by full-solution format
  verification: `ExecutionCenterViewModelTests.cs` and `RunHistoryViewModelTests.cs`.
- M0, M2, M3 core and M3 renderer branch tips are ancestors of `cd44154`.
- The separate M4 design commit `2b72b3d` is patch-equivalent to `4bd87a3`;
  its initial document blob is identical. Preserve the later reviewed document.
- The apparent three modified files in the root/M2 checkouts have Git-normalized
  blob hashes identical to HEAD. Refreshing their index leaves both checkouts clean.
- Remote inspection initially found only `origin/codex/m0-baseline`; no remote
  `dev` exists. Do not force-push, delete remote branches or change the default branch.
- Remove local branch pointers only after ancestry and remote publication checks.
  Preserve worktree contents in an ignored local backup before unregistering them;
  never force-remove worktrees containing ignored data.

## Verification plan and exit gate

Run locked restore, complete format verification, Release build and the full test
suite using the already provisioned local runtimes. Review the staged file inventory
for unwanted artifacts and credential-bearing files. Commit the tested source, merge
all branch ancestry into `dev`, verify the merged tree, and push without force.
Confirm local `dev` and `origin/dev` match before cleanup. Record exact results below.

## Results

Initial full-format verification exited 1 for import ordering in the two test
files above; scoped formatter and repeated full verification exited 0. Release
build exited 0 with zero warnings/errors. No runtime logic changed.

Staging exposed six Next candidate metadata/document files in CRLF although
`.gitattributes` requires LF. Their raw hashes would fail on a fresh clone.
`CandidatePayloadUsesGitCanonicalLfBytes` first failed 1/3 (Next) and passed 2/3.
Normalize only those six files to LF, regenerate their checksum entries, and align
the editor's blueprint defaults. The complete Blueprint suite then passed 155/155.
Existing checksums and tamper tests remain enforced. No binary/runtime code changed.

Source inventory: 767 tracked/untracked source paths at initial review, no forbidden
credential/artifact filenames, no file over 5 MiB. Seventeen credential-shaped
matches were test fixtures or documented test examples; matched values were never
printed. Ignored tooling and outputs are excluded from staging.

A trial `git merge-tree --write-tree` found only the expected M4 add/add document
conflict. Its older branch content is byte-identical to the original cherry-pick;
retain the later reviewed document and verify that the ancestry-only merge has
the exact same tree as its first parent.

Initial full-solution test command exited 0: Unit 651/651 (609 ms), Integration
724/724 (31 s), Blueprint 152/152 (2 s), E2E 235/235 (14 min 03 s), for a total of
1,762/1,762 with zero failures/skips. Command (with the already provisioned Node,
uv and Python directories prepended to PATH):

`dotnet test DevForge.sln -c Release --no-build --no-restore -m:1`

That full run used the original copied E2E payloads; the subsequent checksum-only
normalization is independently covered by the 155/155 Blueprint run and Git export
verification below. The three new LF regression cases raise the complete suite to
1,765 tests. Formatter initially caught LF lines in the new C# test; scoped format
then full-solution verification both exited 0. Post-merge verification reran the
solution except four `Category=ReleaseAcceptance` cases (three test methods), whose
runtime code and generated templates are unchanged and passed in the full run.
This is not Windows 11 or release certification.

Git-index export verification (fresh archive, not the working-copy payloads):
6 packages, 208 declared SHA-256 payload hashes, zero mismatches, exit 0.
All 109 candidate files also match their raw Git index blobs before commit.
The exported snapshot is ignored and remains local.

## Post-merge verification and publication

Source commit: `d82a63c2160efc138f6a4e2fca61555bd82152f9` (181 changed files).
Merged history: `0b0e6636fb9864a1a6944f47f887b491e0219a05`.
`git diff --exit-code HEAD^1 HEAD` exited 0: the M4 ancestry merge did not change
the tested source tree. All six local branch tips were verified as ancestors.

Commands below ran in `E:/MyProjects/DevForge` on `dev`, using the portable SDK at
`E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe`.

| Command | Exact result |
| --- | --- |
| `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0, all 12 projects restored. |
| `dotnet format DevForge.sln --verify-no-changes --no-restore` | Final exit 0, no changes or workspace warning after Debug refresh below. |
| `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false` | Exit 0, 0 warnings/errors, 27.75 s. |
| `dotnet build DevForge.sln -c Debug --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false` | Exit 0, 0 warnings/errors, 32.56 s. |
| `dotnet test DevForge.sln -c Release --no-build --no-restore -m:1 --filter 'Category!=ReleaseAcceptance'` | Exit 0: Unit 651 (716 ms), Integration 724 (34 s), Blueprint 155 (3 s), E2E 231 (2 min 13 s); 1,761 passed, 0 failed/skipped. Four acceptance cases intentionally excluded, not represented as rerun. |
| Root-checkout SHA-256 inventory | 6 packages, 208 payload hashes, 0 mismatches. |
| `git push --set-upstream origin dev` | Exit 0; created `origin/dev` and tracking. |
| `git ls-remote --heads origin refs/heads/dev` | Remote exactly matched `0b0e6636fb9864a1a6944f47f887b491e0219a05`. |

The first root format runs exited 0 but reported a design-time warning: the Debug
WPF workspace could not resolve `EnvironmentToolStatus` from the old checkout's
assemblies. Diagnostic output identified the Debug path. A complete Debug build
refreshed those artifacts, and the subsequent full format check exited 0 without
that warning. No application code workaround or suppressed diagnostic was added.

All runtime PATH additions were process-local. The provisioned Node/uv/Python
directories were under the M4–M11 worktree during verification and are now preserved
under the backup described below. No machine-wide tool or environment installation.

## Cleanup and continuation

After remote confirmation, all three clean worktrees were moved intact into the
new, previously absent directory
`E:/MyProjects/DevForge/.artifacts/worktree-backups/2026-08-28-dev-consolidation`.
Resolved source/destination boundaries and reparse attributes were checked before
moving; no occupied directory was overwritten. A dry-run prune listed only the
three missing old worktree locations, then `git worktree prune --verbose --expire now`
removed those registrations. `git branch -d` removed the six ancestor-verified local
`codex/*` pointers, without force. Their commits and all ignored working data remain
recoverable. A README in the ignored backup explains recovery and runtime locations.

The only active worktree and local branch are now the main checkout and `dev`.
The existing remote default branch `codex/m0-baseline` and `origin/HEAD` were left
unchanged. No remote branch was deleted, no force-push or release tag was made.
This evidence update is documentation-only and is published as a descendant of the
verified merge. Windows 11, UX/DPI, packaging, observed remote-CI and PostgreSQL
bootstrap/ownership/auth/recovery release holds remain open.

## Original local branch tips

These commits remain recoverable from the consolidated history after local branch
cleanup; removal of a branch name does not discard their commits.

| Branch | Original tip |
| --- | --- |
| `codex/m0-baseline` | `a7fe3ae3a5cea12ec054bebf683c22c4e15c599a` |
| `codex/m2-persistence` | `eae67b5237bd8e913f79fae4c2e5370b58c0399d` |
| `codex/m3-core-infrastructure` | `abc1fae` |
| `codex/m3-template-renderer-closure` | `9b2b4ca5e6813377c31a13e16245855673d04dee` |
| `codex/m4-planner-rules-catalog` | `2b72b3dfbe5a7f0ec65b8cc941de6e5ed69da7a9` |
| `codex/m4-m11-completion` | `cd44154d54aa85249e8569be3ade6faa27d6025e` |
| `dev` | `0575ec6e2aac34815d593a3ee68003cc3fa4a8fe` |
