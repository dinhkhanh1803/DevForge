# Testing

## Test levels

Vitest unit and component tests run with `pnpm run test`. No dedicated integration test suite exists yet. `pnpm run build` is a production build smoke check; lint and type checking are required static quality checks, not test levels.

## Release gate

Run the release gate in order:

```text
pnpm install --frozen-lockfile --ignore-scripts
pnpm run format:check
pnpm run lint
pnpm run typecheck
pnpm run test
pnpm run build
```

The build must reproduce the complete committed `dist` directory without a repository change. Prettier excludes only dependency/build outputs and DevForge-owned evidence; source and configuration remain checked.
