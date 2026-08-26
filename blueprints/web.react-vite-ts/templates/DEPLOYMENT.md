# Deployment

`pnpm run build` emits static production assets to `dist`. Review hosting headers, cache policy, rollback, public environment values, and organizational security requirements before deployment. This project does not deploy automatically or create credentials.

## Release preparation

Run the full release gate, inspect `dist`, confirm public environment values, and commit the complete reviewed `dist` output before configuring hosting security and cache headers in the approved platform. Rerun `pnpm run build` and require a clean repository to prove the committed artifact is reproducible.

## Rollback

Retain the previously verified static artifact and hosting configuration so traffic can be returned to that immutable release.
