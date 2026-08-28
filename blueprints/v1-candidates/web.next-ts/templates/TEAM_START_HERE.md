# TEAM START HERE

## First session

Read README.md and ARCHITECTURE.md. Install the pinned toolchain, run frozen install
and every gate in TESTING.md, then start the local dev server.

## Handoff

The handed-off repository contains source, exact dependencies and engine evidence.
Tooling caches and production server output were intentionally not transferred.
Check generation-report.json for the actual run; do not infer success from files
being present. Do not edit engine-owned evidence to bypass a failed check.

## Next feature

Choose one reviewed route or domain behavior, add a test and re-run all gates.
Auth, database and hosting remain explicit future decisions, not implied features.
