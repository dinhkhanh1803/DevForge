# Deployment

`uv run --frozen --no-sync --no-config pyproject-build --no-isolation` emits a source distribution and wheel under `dist`. Inspect both artifacts, their provenance, supported Python range, rollback plan, and organizational publishing policy before release. This project does not publish packages, create credentials, or provision external infrastructure automatically.
