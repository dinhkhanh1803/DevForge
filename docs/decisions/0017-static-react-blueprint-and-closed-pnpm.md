# ADR-0017: Static React blueprint and closed pnpm vocabulary

**Status:** Accepted

**Date:** 2026-08-25

## Context

The production React blueprint must generate a reproducible Vite/React/TypeScript project and prove its toolchain matrix without allowing a trusted manifest to become an arbitrary package-manager command channel. Online scaffolders, lifecycle scripts, registry overrides, configuration mutation, credentials, and unreviewed scripts would make generation depend on mutable remote behavior or ambient machine state.

## Decision

- Ship `web.react-vite-ts@1.0.0` as reviewed, checksummed static templates and overlays. Do not invoke `create-vite`, `pnpm dlx`, `pnpm exec`, or another online scaffolder.
- Certify Node `>=22 <25` and pnpm `>=10 <11`; pin `pnpm@10.24.0` and every direct package to an exact version in `package.json`, with a committed pnpm lockfile.
- Admit package installation only as the separated argument list `pnpm install --frozen-lockfile --ignore-scripts` through `IProcessRunner`.
- Admit validation only as `pnpm run lint`, `pnpm run typecheck`, `pnpm run test`, or `pnpm run build`. Reject extra arguments, inline evaluation, deployment, config mutation, registry overrides, and alternate pnpm verbs before invoking the runner.
- Generate strict TypeScript, a matching `@` source alias in TypeScript and Vite, ESLint, Prettier, Vitest/jsdom, an explicit API service boundary, and Zod validation for public `VITE_` configuration. Ignore all `.env*` files except the empty-value `.env.example` handoff.
- Separate deterministic generation evidence from real toolchain evidence. Composed E2E runs the production catalog, planner, staging, handlers, and finalizer with a recording process boundary; an independently located generated tree runs the real frozen install, format check, lint, typecheck, tests, and Vite production build.

## Consequences

The generated project is reproducible, reviewable, and usable without trusting scaffolder code or package lifecycle scripts. Adding another script, package-manager operation, registry, or credential flow requires an explicit policy change and regression tests. Direct dependency upgrades require coordinated manifest, package, lockfile, checksum, contract, and real-matrix updates.

`jsdom` is pinned to the newest reviewed release compatible with the certified Node 22 runtime rather than blindly selecting a newer release whose engine floor excludes the available certified runtime.

## Rejected alternatives

- Running `create-vite` or another remote scaffolder during project creation.
- Allowing arbitrary `pnpm run <script>` or passthrough arguments.
- Enabling lifecycle scripts or accepting registry/config overrides.
- Floating direct dependencies with ranges, wildcards, or `latest`.
- Treating `.env` files as generated artifacts or storing configuration values in the blueprint.
- Relying only on a mocked process runner or only on a standalone package test.
