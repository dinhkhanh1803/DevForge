# ADR-0022: Authoritative Desktop diagnostics actions

- Status: Accepted
- Date: 2026-08-26

## Context

M10 must let a user create support evidence without a terminal while preserving checkpoint authority, privacy, safe mode, and Clean Architecture. The Desktop must not receive a raw staging path, open arbitrary folders, invoke a shell, or treat display text as cleanup authority.

## Decision

Desktop diagnostics use a composed `DesktopDiagnosticsCoordinator`. It accepts only a canonical run identifier from an authoritative execution snapshot or persisted Run History item and delegates export to `ISupportBundleCoordinator`. Successful UI state retains the typed `SupportBundleReceipt`; copy exposes only its owned relative path, SHA-256, and byte length. Notifications are fixed and redacted. Cleanup, when requested through the coordinator, delegates the typed receipt to `ISupportBundleCleanupService` and is refused in safe read-only mode.

Execution Center and Run History use cancellation-aware asynchronous commands with busy-state single-flight guards. They never receive an absolute filesystem path and contain no filesystem, process, or shell operations. `Open staging` remains disabled until a dedicated typed launcher can verify an owned folder target; the existing IDE launcher is not broadened. Failed-step selection and XAML automation/scaling behavior remain presentation concerns.

## Consequences

- Support bundles can be created and their integrity receipt copied without a terminal.
- Bundle cleanup authority remains marker-and-digest bound below the UI.
- Safe mode may export verified diagnostic evidence but cannot delete it.
- No arbitrary folder launch is available in M10 Task 4; this is an intentional fail-closed limitation.
- Real-display DPI certification remains separate release-host evidence and cannot be replaced by measure/arrange tests.
