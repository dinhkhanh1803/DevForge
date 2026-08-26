# {{ project.name }}

A strict React + Vite + TypeScript frontend with a validated public environment boundary, typed API client, ESLint, Prettier, Vitest, and exact pnpm lockfile.

## Start here

Read `TEAM_START_HERE.md`, copy `.env.example` to an untracked `.env` only when a public API base URL is needed, then run `pnpm install --frozen-lockfile --ignore-scripts` and the four checks in `TESTING.md`.

## Repository layout

`src/app` contains composition and UI, `src/config` owns public environment validation, `src/services` owns transport, and tests stay beside the behavior they cover.

## Local setup

Use the supported Node and pnpm versions, keep lifecycle scripts disabled during the frozen install, and configure only documented public `VITE_` values.

## Quality gates

Run the ordered commands in `TESTING.md`; the production output is the generated `dist` directory.
