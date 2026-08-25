# Testing

Run the release gate in order:

```text
pnpm install --frozen-lockfile --ignore-scripts
pnpm run lint
pnpm run typecheck
pnpm run test
pnpm run build
```
