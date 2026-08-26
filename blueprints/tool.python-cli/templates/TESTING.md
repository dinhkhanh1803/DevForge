# Testing

## Test levels

Pytest behavior and unit tests run with `uv run --frozen --no-sync --no-config pytest`. No dedicated integration test suite exists yet. The package build is a packaging smoke check, and `uv run --frozen --no-sync --no-config team-tool --help` is an installed-CLI smoke check.

## Release gate

Run the release gate in order:

```text
uv sync --frozen --no-config
uv run --frozen --no-sync --no-config ruff format --check .
uv run --frozen --no-sync --no-config ruff check .
uv run --frozen --no-sync --no-config mypy src tests
uv run --frozen --no-sync --no-config pytest
uv run --frozen --no-sync --no-config pyproject-build --no-isolation
uv run --frozen --no-sync --no-config team-tool --help
```
