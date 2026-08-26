# Testing

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
