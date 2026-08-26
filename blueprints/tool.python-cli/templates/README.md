# {{ project.name }}

A packaged Python CLI with a `src` layout, typed environment boundary, standard logging, Ruff, strict mypy, pytest, Hatchling, and an exact uv lockfile.

## Start here

Read `TEAM_START_HERE.md`, then run `uv sync --frozen --no-config` and every check in `TESTING.md`. Copy `.env.example` to an untracked `.env` only as documentation for local tooling; the application reads process environment variables directly and never loads secret files.

## Repository layout

`src/team_tool` contains the installed package and CLI entrypoint, `tests` mirrors observable behavior, and root configuration owns the exact environment, quality, test, and build policy.

## Local setup

Use CPython 3.14 and uv 0.12, synchronize the exact lock without ambient configuration, and run the installed `team-tool` entrypoint through uv.

## Quality gates

Run every command in `TESTING.md`, including formatting, lint, typing, tests, package build, and CLI help.
