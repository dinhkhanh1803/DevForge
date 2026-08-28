# CONTRIBUTING

## Change policy

Add one behavior and its test together. Keep exact package versions and the frozen
pnpm lock synchronized; review dependency/security changes before updating them.
Never commit .env files, credentials, node_modules, .next or caches.

## Review gate

Run all commands in TESTING.md. Source, lock, documentation and public API changes
need review. Do not treat an old generation report as evidence for modified source.
