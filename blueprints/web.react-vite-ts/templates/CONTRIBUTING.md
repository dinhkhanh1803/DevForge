# Contributing

Use focused changes, keep all dependencies exact, update `pnpm-lock.yaml` intentionally, and add a regression test for reproducible defects. Never commit `.env`, credentials, generated `dist`, coverage, or `node_modules`.

## Workflow

Use the frozen script-disabled install, make focused changes within the documented boundaries, and update tests and lockfiles intentionally.

## Branches and commits

Create a short-lived branch from the reviewed primary branch. Keep each focused commit buildable, use an imperative summary, and do not mix dependency or generated-file changes with unrelated behavior.

## Review

Review public-environment exposure, dependency and lock changes, lint/type/test/build evidence, accessibility, and generated-output exclusions.

## Quality gates

Before requesting review, run every command in `TESTING.md` and include the exact results. Merge only after review approval and all required quality gates pass.
