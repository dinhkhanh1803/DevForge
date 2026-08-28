# Required validators

## Independent checks

pnpm runs format:check, lint, typecheck, test, build and smoke as separate required
validators. The engine repeats postconditions. Smoke uses an ephemeral loopback
port, request deadlines, finally cleanup and a 30-second process deadline.
