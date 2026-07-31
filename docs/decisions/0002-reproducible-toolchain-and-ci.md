# ADR-0002: Reproducible .NET Toolchain and Windows CI

- Status: Accepted
- Date: 2026-07-31
- Milestone: M0

## Context

The baseline requires .NET 10, centrally pinned packages, clean builds, locked restore, and Windows CI. The development machine initially has no .NET SDK installed.

## Decision

- Pin SDK `10.0.302` in `global.json`; this is the latest supported .NET 10 SDK available on 2026-07-31 and includes the .NET Desktop runtime.
- Use a workspace-local SDK for this execution so the happy path requires neither Administrator privileges nor machine-wide changes.
- Enable NuGet Central Package Management and commit per-project lock files.
- Treat compiler and analyzer warnings as errors for production and test source.
- Run CI on `windows-latest` with locked restore, format verification, Release build, tests, and TRX artifact upload.
- Pin official actions to reviewed release commit SHAs: `actions/checkout` v6.0.2, `actions/setup-dotnet` v6.0.0, and `actions/upload-artifact` v7.0.1.
- Limit the uploaded artifact to generated `*.trx` files, fail when expected results are absent, and retain them for 14 days.

## Alternatives considered

1. Pinning only `10.0.100` maximizes early-SDK compatibility but omits later security and servicing fixes.
2. Allowing any `10.0.x` SDK weakens reproducibility.
3. The chosen current servicing SDK plus `latestPatch` roll-forward balances reproducibility with patch servicing inside the selected feature band.

## Consequences

- Contributors need a compatible .NET 10 SDK.
- Package upgrades are explicit changes to `Directory.Packages.props` and lock files.
- CI and local verification use the same commands and fail on drift.
