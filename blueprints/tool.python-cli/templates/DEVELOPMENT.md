# Development

Use CPython 3.14 and uv 0.12. Synchronize only with `uv sync --frozen --no-config`. Run the CLI with `uv run --frozen --no-sync --no-config team-tool --help`. Configuration is read from validated process environment variables; no dotenv loader is included.

## Prerequisites

Install CPython 3.14 and uv 0.12; the lockfile supplies the exact development and build tools.

## Local setup

Run the frozen config-isolated sync, keep `.env` untracked, and invoke the installed console entrypoint with the documented uv command.

## Environment

Document supported process variables in `.env.example` with empty values. Supply local values through the process environment; the application does not load `.env` files or credentials.

## Database

No database is used by this blueprint.

## Debugging

Run `uv run --frozen --no-sync --no-config team-tool --help` to reproduce CLI startup and `uv run --frozen --no-sync --no-config pytest` for regressions. Use the installed entrypoint rather than raw Python evaluation or module execution.
