# Contributing

Create a focused branch, keep dependencies pointing inward, add a regression test with each reproducible bug fix, and run every command in `TESTING.md` before review. Do not commit credentials, `.env`, generated build output, or machine-specific settings.

## Workflow

Restore in locked mode, make one focused change, add or update tests, and run the documented quality gate.

## Branches and commits

Create a short-lived branch from the reviewed primary branch. Keep each focused commit buildable, use an imperative summary, and do not mix dependency or generated-file changes with unrelated behavior.

## Review

Review dependency direction, test evidence, lockfile changes, configuration safety, and release impact.

## Quality gates

Before requesting review, run every command in `TESTING.md` and include the exact results. Merge only after review approval and all required quality gates pass.
