# Contributing

Keep runtime dependencies minimal, pin every development dependency exactly, and update `uv.lock` intentionally. Add a regression test for reproducible defects. Never commit `.env`, credentials, `.venv`, caches, build output, or generated package metadata.

## Workflow

Synchronize from the frozen lock, make a focused change, add tests, and run every config-isolated command in the release gate.

## Branches and commits

Create a short-lived branch from the reviewed primary branch. Keep each focused commit buildable, use an imperative summary, and do not mix dependency or generated-file changes with unrelated behavior.

## Review

Review package boundaries, exact dependency changes, typed behavior, test/build evidence, CLI compatibility, and secret/output exclusions.

## Quality gates

Before requesting review, run every command in `TESTING.md` and include the exact results. Merge only after review approval and all required quality gates pass.
