# Architecture

`src/app` owns composition and UI, `src/config` validates public build-time configuration, and `src/services` owns external API transport. UI components consume typed service functions rather than reading environment variables or calling `fetch` directly. The `@` alias always resolves to `src` in TypeScript, Vite, Vitest, and ESLint.

## Boundaries

UI consumes typed configuration and service functions; only the configuration boundary reads `import.meta.env`, and only the service boundary performs external transport.

## Repository layout

Application code is under `src`, public static inputs are under `public` when present, and root configuration defines the shared TypeScript, Vite, lint, test, and package policy.

## Decision records

No project-specific ADRs exist at generation time. Future accepted ADRs must use repository-relative links to records stored under `docs/decisions/`.
