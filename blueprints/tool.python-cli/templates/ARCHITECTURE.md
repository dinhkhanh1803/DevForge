# Architecture

`src/team_tool/cli.py` owns argument parsing, `config.py` validates public process configuration, and `logging_config.py` owns standard-library logging setup. The package exposes the fixed `team-tool` console entrypoint. Tests mirror behavior without importing from repository-relative paths because uv installs the package into the project environment.

## Boundaries

The CLI delegates configuration validation and logging setup to their modules; package code reads only documented process environment and does not load repository secret files.

## Repository layout

Installable code uses the `src` layout, tests are under `tests`, and `pyproject.toml` plus `uv.lock` define the exact build and development environment.

## Decision records

No project-specific ADRs exist at generation time. Future accepted ADRs must use repository-relative links to records stored under `docs/decisions/`.
