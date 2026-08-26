# Deployment

`pnpm run build` emits static production assets to `dist`. Review hosting headers, cache policy, rollback, public environment values, and organizational security requirements before deployment. This project does not deploy automatically or create credentials.

## Release preparation

Run the full release gate, inspect `dist`, confirm public environment values, and configure hosting security and cache headers in the approved platform.

## Rollback

Retain the previously verified static artifact and hosting configuration so traffic can be returned to that immutable release.
