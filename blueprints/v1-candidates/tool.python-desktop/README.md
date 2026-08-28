# Python Desktop candidate 1.0.0

## Scope
tool.python-desktop is an isolated M11 candidate, not shipped by BuiltIn.
Python 3.14, uv 0.12, native Tk/ttk and a pure refresh model; no runtime dependencies.
Pinned Python CLI development dependencies and lock graph are retained.

## Acceptance
Contract, checksum, deterministic workflow and real install/format/lint/typecheck/
test/build/native smoke are required. Real-toolchain publication and Windows 11
certification must be recorded separately; neither is implied by candidate presence.
DevForge itself remains C# WPF. See ADR-0026 for scope and release holds.

## Local checkpoint 2026-08-27
Windows 10 / CPython 3.14.6 / Tk 8.6 / uv 0.12.1: the standalone explicitly
configured host passes all eight tool commands, 11 generated tests and native
Refresh/focus/close smoke. The unchanged production uv workflow fails frozen sync
with DNS os error 11003. Its later environment-output/Git boundary is unverified.
No Windows 11 or combined production-publication certification is claimed.
