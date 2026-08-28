# ADR-0026: Native Python Desktop as the second isolated M11 candidate

Date: 2026-08-27
Status: Accepted under the owner's instruction to use recommended safe defaults.

## Decision

Detailed baseline sections 17.2 and 18 permit independent V1 candidates without
an internal ordering requirement. After the WinForms checksum-tamper contract,
implement only `tool.python-desktop@1.0.0`, test-distributed alongside WinForms.
The shipped BuiltIn catalog and Desktop remain unchanged (three MVP roots, WPF).

Use standard-library Tkinter/ttk with a pure status model, a small view, and an
explicit application entrypoint. Reuse the reviewed Python 3.14/uv 0.12 pinned
development toolchain and src layout. Tk is an optional interpreter capability:
the native smoke must fail clearly if unavailable, never silently skip. No
runtime GUI dependency, web shell, interpreter installation, or cloud service.
Official API reference: https://docs.python.org/3.14/library/tkinter.html.

Alternatives: PySide6 adds a GUI dependency and packaging/licensing decisions;
Next/Nest requires a separate Node/SSR or database acceptance slice. Neither is
needed for this bounded desktop candidate. Python Desktop is not a new platform
for DevForge itself.

The desktop entrypoint exposes only normal launch and a fixed bounded native
smoke. Smoke opens the real view, invokes Refresh, verifies changed bound state
and keyboard focus, and closes through the event loop. Missing Tk or callback
failure exits nonzero. Add only that exact uv validation vector to the closed
handler vocabulary; evaluation, arbitrary module/entrypoint and extra arguments
remain rejected.

## Acceptance and boundaries

WinForms tamper tests operate on a guarded test-owned copy, verify the pristine
package first, mutate payload or checksum inventory, and require quarantine and
failed resolve. Never modify the shared candidate output or production source.

Python requires checksum/schema/planning/isolation contracts, generated model and
native tests, deterministic workflow/evidence, failure/occupied-target safety,
real frozen install/format/lint/typecheck/test/build/native smoke, and local Git
verification. Real toolchain evidence and mocked composition are labeled apart.
The Python CLI's .venv/cache/source boundary is not silently covered by ADR-0025's
.NET-only output policy. If the full production workflow exposes a further core
boundary, retain the candidate gate open and record evidence; do not delete or
ignore arbitrary files, increase limits without review, or waive secret scanning.

Windows 11, remote CI and release promotion remain Pending under ADR-0024. No
remote writes, commits, package upgrades, or additional V1 candidate in this task.

## Observed local checkpoint

WinForms four mutation cases passed before creating the Python payload. Four
Python mutations reuse the private-copy contract. The candidate's 32 checksum
entries match. Standalone production-runner commands with declared release-host
environment pass frozen sync, Ruff, mypy, 11 tests, wheel/sdist and native smoke.
The unchanged production command fails uv sync while fetching hatchling with
DNS os error 11003; ordinary host DNS lookup resolves the endpoint. This does not
yet isolate the exact environment/cache/network cause. Combined publication and
the later Python output boundary remain open, not certified by the standalone run.

Review's callback-after-state-change regression reproduced false success before
the report_callback_exception failure latch. Native smoke now fails closed and
does not print raw callback exceptions. Deterministic composition includes uv.lock.
