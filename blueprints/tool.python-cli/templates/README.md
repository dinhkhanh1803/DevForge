# {{ project.name }}

A packaged Python CLI with a `src` layout, typed environment boundary, standard logging, Ruff, strict mypy, pytest, Hatchling, and an exact uv lockfile.

## Start here

Read `TEAM_START_HERE.md`, then run `uv sync --frozen --no-config` and every check in `TESTING.md`. Copy `.env.example` to an untracked `.env` only as documentation for local tooling; the application reads process environment variables directly and never loads secret files.
