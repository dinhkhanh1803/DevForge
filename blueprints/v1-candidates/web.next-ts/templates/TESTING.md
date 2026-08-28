# TESTING

## Required quality gates

Run from the project root after frozen installation:

```text
pnpm run format:check
pnpm run lint
pnpm run typecheck
pnpm run test
pnpm run build
pnpm run smoke
```

Typecheck first generates Next route types. Tests use Node's test runner and cover
configuration defaults, trimming and invalid input. Three subprocess lifecycle
tests also run the unchanged smoke script with a test-only Next substitute:
HTTP assertion failure, hung shutdown reaching the 30-second deadline, and
process cancellation. Each requires a nonzero exit and connection refusal on
the previously observed loopback port. The suite takes at least 30 seconds.
These failure tests do not replace the separate real Next production smoke.
Build does not replace lint.
Smoke starts the production server on an ephemeral loopback port, checks HTML and
JSON health, enforces request/process deadlines, and closes resources on failure.

## Evidence

DevForge records executed command results, source/artifact digests and final-tree
publication evidence. Re-run gates after any source or toolchain change.
