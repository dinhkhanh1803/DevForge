# ADR-0018: Static Python CLI blueprint and closed uv vocabulary

**Status:** Accepted

**Date:** 2026-08-26

## Context

The production Python CLI blueprint must create a reproducible typed package and prove its Windows toolchain matrix without turning Python or uv into arbitrary evaluation, dependency mutation, index, configuration, credential, or install-hook channels. The newest Python patch is not sufficient by itself: the certified uv release must also provide and resolve that interpreter deterministically.

## Decision

- Ship `tool.python-cli@1.0.0` as reviewed, checksummed static templates and overlays with a committed `uv.lock`; do not invoke an online project generator.
- Certify CPython `>=3.14 <3.15` with the concrete tested pair CPython 3.14.6 and uv 0.12.1. uv 0.12.1's managed-interpreter catalog is authoritative for this pair.
- Admit installation only as the separated argument list `uv sync --frozen --no-config` through `IProcessRunner`.
- Admit validation only through six exact argument lists: Ruff format check, Ruff lint, strict mypy, pytest, `pyproject-build --no-isolation`, and the generated `team-tool --help`, all with frozen/no-sync/no-config uv execution.
- Reject arbitrary Python `-c`/`-m`, uv dependency mutation, config/index/credential overrides, unreviewed entrypoints, fix mode, path escapes, and passthrough arguments before invoking the runner.
- Pin Hatchling, build, Ruff, mypy, and pytest exactly in `pyproject.toml` and the lockfile. Generate the `src` layout, typed immutable environment configuration, standard-library logging, tests, package metadata, empty-value `.env.example`, and production handoff documents.
- Keep deterministic generation evidence separate from real toolchain evidence. Composed E2E uses the production catalog/planner/staging/handlers/finalizer with a recording runner; a standalone generated tree runs the exact real uv matrix.

## Consequences

The generated Python CLI is deterministic, typed, buildable, and reviewable without shell activation or arbitrary interpreter execution. Adding an entrypoint, uv verb, dependency, custom index, or credential flow requires a deliberate policy update, regression tests, a regenerated lockfile/checksum set, and the real toolchain matrix.

The package build uses the fixed `pyproject-build` console entrypoint through `uv run`. Direct `uv build --no-build-isolation` does not select the project virtual environment and therefore cannot see the locked Hatchling backend on the certified Windows pair.

## Rejected alternatives

- Running Cookiecutter, Copier, or another remote scaffolder during project creation.
- Allowing `python -c`, `python -m`, arbitrary `uv run`, or arbitrary scripts/modules.
- Allowing `uv add`, mutable sync, custom indexes/configuration, credentials, or install hooks.
- Floating dependency versions, omitting `uv.lock`, or selecting an interpreter absent from uv's certified download catalog.
- Treating `.env` values as generated artifacts or persisted blueprint inputs.
- Relying only on mocked execution or only on a standalone package check.
