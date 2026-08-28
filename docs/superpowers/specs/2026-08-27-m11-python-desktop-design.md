# M11 WinForms tamper closure and Python Desktop design

Scope and alternatives: ADR-0026. The owner has approved recommended defaults
and inline execution; no visual redesign of DevForge or approval pause is needed.

First close WinForms package-specific checksum coverage with pristine controls
and tamper/quarantine checks against private guarded copies. Include manifest,
source, lock template and checksum inventory mutations. Protect shared packages.

Then add one versioned Python Desktop candidate. The pure model owns refresh
state; Tk/ttk binds labels and commands on its UI thread. The application handles
configuration/logging, --help, and a bounded --smoke-test that exercises the real
view and exits nonzero on GUI failure. No file/process access in the view/model.
Reuse pinned Python CLI tool versions, safe template rendering and seven guides.

Closed execution extends only by the exact frozen uv desktop smoke vector.
Tests cover near-miss command rejection, deterministic plan/output, production
catalog isolation, checksum mutation, native refresh/close and failed targets.
Run real tools through IProcessRunner; never substitute test-only environment
values to label production execution certified. If production uv environment or
finalized .venv output is unsupported, report the precise open acceptance gate.

Exit: fresh root restore/format/build/tests, candidate contract checks, real
toolchain/native results and truthfully separated Git composition evidence.
Full candidate closure additionally requires combined real-toolchain publication.
Windows 11 and M9/M10 release evidence are separate, unchanged requirements.
