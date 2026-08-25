# Architecture

`src/app` owns composition and UI, `src/config` validates public build-time configuration, and `src/services` owns external API transport. UI components consume typed service functions rather than reading environment variables or calling `fetch` directly. The `@` alias always resolves to `src` in TypeScript, Vite, Vitest, and ESLint.
