# DEPLOYMENT

## Local production run

Run frozen install and all TESTING.md gates at the final project path, then use
pnpm run start. It binds to 127.0.0.1; stop it with Ctrl+C. Do not copy a staging
node_modules or .next tree into production.

## Release responsibility

This candidate does not provision hosting, TLS, authentication or secrets. Review
network exposure and deployment configuration separately before public hosting.
Windows 11, package and remote CI release gates remain required; local smoke is
not evidence that those gates passed.
