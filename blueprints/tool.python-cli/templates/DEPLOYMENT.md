# Deployment

`uv run --frozen --no-sync --no-config pyproject-build --no-isolation` emits a source distribution and wheel under `dist`. Inspect both artifacts, their provenance, supported Python range, rollback plan, and organizational publishing policy before release. This project does not publish packages, create credentials, or provision external infrastructure automatically.

## Release preparation

Run the complete release gate, inspect both distributions, and verify version, provenance, supported Python range, and approved publishing credentials outside the repository.

## Rollback

Retain the previously verified distribution files and release metadata so consumers can be directed back to a known version according to registry policy.
