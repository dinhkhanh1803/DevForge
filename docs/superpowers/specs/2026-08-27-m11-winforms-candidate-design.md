# M11 first slice: isolated C# WinForms candidate

## Authority and release boundary

The owner's 2026-08-27 instruction moves development to M11 under detailed specification section 18.1. ADR-0024 records accepted M9/M10 external evidence debt; no old gate changes to Pass. The standing instruction to choose safe recommended defaults permits inline execution without another design-approval round.

## Scope

Deliver one real `desktop.csharp-winforms-tool@1.0.0` candidate. No Next/Nest/FullStack/Python Desktop/Pixi/Phaser/Unity assets are created in this slice. No installer, collaborator automation, pack importer, or remote mutation is included. M11's eight blueprints release independently; development order begins with WinForms, then Next, Nest, FullStack, Python Desktop, Pixi, Phaser, and Unity 2D, with a separate design and tested version matrix for each.

## Architecture

Candidate source lives below `blueprints/v1-candidates/desktop.csharp-winforms-tool`. Test projects explicitly copy that root to `blueprints/candidates/desktop.csharp-winforms-tool`. Production BuiltIn distribution remains the three reviewed MVP roots. The test-only candidate source uses the real catalog loader, checksum verifier, planner, guarded workspaces, orchestration and evidence writer; it does not grant candidate trust to Desktop.

Generated output retains `TeamTool.slnx`, Domain, Application, Infrastructure, Desktop, and UnitTests. Desktop uses `UseWindowsForms`, MVVM Toolkit, Generic Host, dependency injection and configuration. The main form uses a DPI-aware auto-sized layout, accessible labels and a Refresh action bound to the ViewModel. Domain/Application have no WinForms dependency. Dependencies use the existing exact central versions and checked-in per-project locks. No new DevForge package is needed.

## Execution and failures

The manifest has only copy-overlay, render-template, package-install, and validate-command actions. Restore is locked; format, Release build, tests, and the existing fixed WindowsSmoke publish command are mandatory validators. Work occurs in owned staging, finalization is non-overwriting, and optional local Git publication uses the existing coordinator. Validation failure retains recoverable staging and cannot produce a final target. No shell, SDK installer, cloud service, database credential, or remote publication is introduced.

## Handoff

Ship seven substantive handoff documents using the shared section contract, exact commands, pinned SDK guidance, local-only config, debugging, test boundaries and manual deployment guidance. Documentation distinguishes application tests from host UI smoke and describes no unimplemented integration suite.

## Test and exit gates

1. RED candidate identity/contract test before creating candidate bytes.
2. Production loader checks complete checksums, exact identity/tools/validator vocabulary and no runtime source registration.
3. Deterministic plan/generation across independent destinations, engine-owned evidence, failure/no-overwrite and local Git coverage.
4. Real locked restore, format, Release build, generated unit tests and publish; responsive native WinForms window, named Refresh control and clean exit.
5. Root solution restore, scoped format, Release build, all four test projects and unchanged three-root package contracts.
6. Record exact host and counts. Windows 11 and candidate release promotion remain Pending if only the current Windows 10 host is available.
