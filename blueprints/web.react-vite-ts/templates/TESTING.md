# Testing

## Test levels

Vitest unit and component tests run with `pnpm run test`. No dedicated integration test suite exists yet. `pnpm run build` is a production build smoke check; lint and type checking are required static quality checks, not test levels.

## Release gate

Run the release gate in order:

```text
pnpm install --frozen-lockfile --ignore-scripts
pnpm run lint
pnpm run typecheck
pnpm run test
pnpm run build
```
