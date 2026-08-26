# Development

Use Node 22-24 and pnpm 10. Install with `pnpm install --frozen-lockfile --ignore-scripts`, then use `pnpm run dev`. Public client variables must use the `VITE_` prefix and be declared in `.env.example` without values.

## Prerequisites

Install a supported Node 22-24 release and pnpm 10; do not rely on globally installed project dependencies.

## Local setup

Run the frozen install from the repository root, optionally copy `.env.example` to untracked `.env`, and start Vite with `pnpm run dev`.

## Environment

Declare public client variables in `.env.example` with empty values and put local values only in ignored `.env` files. Never place credentials in a `VITE_` variable because client variables are bundled publicly.

## Database

No database is used by this blueprint.

## Debugging

Run `pnpm run dev`, reproduce the issue in the browser, and use browser developer tools with source maps. Run `pnpm run typecheck` and `pnpm run test` before changing behavior.

## Production output

Use only `pnpm run build` to regenerate `dist`. Review and commit the complete output with the source change that produced it; never hand-edit generated production files.
