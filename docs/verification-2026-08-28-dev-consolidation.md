# Dev branch consolidation — 2026-08-28

## Scope and safety

User requested consolidation of local milestone branches, cleanup, and publication
of all source changes to `origin/dev`. This is development integration, not release
promotion. M9/M10 external gates and the PostgreSQL prerequisite hold remain open.

- Preserve all existing implementation, candidate, test and documentation changes.
- Do not publish ignored tools, caches, generated projects, credentials or build outputs.
- Normalize only the two import-order failures identified by full-solution format
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
then full-solution verification both exited 0. Post-merge verification will rerun
the solution except the three expensive `Category=ReleaseAcceptance` tests, whose
runtime code and generated templates are unchanged and passed in the full run.
This is not Windows 11 or release certification. Remote publication is pending.

Git-index export verification (fresh archive, not the working-copy payloads):
6 packages, 208 declared SHA-256 payload hashes, zero mismatches, exit 0.
All 109 candidate files also match their raw Git index blobs before commit.
The exported snapshot is ignored and remains local.

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
