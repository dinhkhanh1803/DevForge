# Contributing

Use focused changes, keep all dependencies exact, update `pnpm-lock.yaml` intentionally, and add a regression test for reproducible defects. Never commit `.env`, credentials, coverage, or `node_modules`. Commit the reviewed `dist` production output because this blueprint integrity-binds it as a release artifact.

## Workflow

Use the frozen script-disabled install, make focused changes within the documented boundaries, and update tests and lockfiles intentionally.

## Branches and commits

Create a short-lived branch from the reviewed primary branch. Keep each focused commit buildable, use an imperative summary, and do not mix dependency or generated-file changes with unrelated behavior.

## Review

Review public-environment exposure, dependency and lock changes, lint/type/test/build evidence, accessibility, and the complete `dist` production output. Do not hand-edit `dist`; regenerate it with the exact build command.

## Quality gates

Before requesting review, run every command in `TESTING.md` and include the exact results. Merge only after review approval and all required quality gates pass.
