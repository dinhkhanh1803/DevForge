# DevForge Studio - Codex Implementation Specification V1.0

**Owner:** Trần Đình Khánh  
**Date:** 31/07/2026  
**Status:** Implementation Baseline  
**Target:** Windows desktop native, C# + WPF, .NET 10

> This Markdown file is the Codex-friendly companion to the full DOCX specification. The DOCX remains the detailed baseline; this file contains the executable implementation rules and scope summary.

## 1. Product intent

DevForge Studio is a local Windows desktop application for a freelancer/team leader who repeatedly initializes web, app, game, C# and Python projects. The user configures a project, presses one button, and DevForge generates a clean repository, applies a product/team overlay, installs dependencies, validates the project, initializes Git, optionally publishes to GitHub, writes a generation report and opens the selected IDE.

DevForge is **not** an AI code generator. It uses versioned blueprints, compatibility rules and official local CLIs. It must not call OpenAI, Gemini or any AI API.

## 2. Non-negotiable decisions

- Native Windows desktop application: **C# + WPF on .NET 10**.
- MVVM with CommunityToolkit.Mvvm.
- Clean Architecture: Desktop -> Application -> Domain; Infrastructure implements abstractions.
- .NET Generic Host for DI, configuration, logging and lifecycle.
- SQLite + EF Core for local metadata. Never store source code or secrets in the database.
- Scriban for restricted text templating.
- Git CLI and GitHub CLI (`gh`) for Git/GitHub operations. Never read or persist the token.
- NuGet Central Package Management via `Directory.Packages.props`.
- No web shell, Electron, Tauri or Blazor Hybrid.
- No cloud backend in MVP.
- No arbitrary shell strings, `cmd /c`, or untrusted PowerShell execution.
- No overwrite of non-empty target directories.
- Generate in staging, validate, then finalize.
- “Completed” requires evidence from all mandatory quality gates.

## 3. MVP scope

1. WPF shell, Dashboard, Create Project, Execution Center, Environment Doctor, Run History and Settings.
2. Blueprint Engine: manifest, inputs schema, rules, safe step handlers, validators, versioning and trust.
3. Execution Engine: immutable plan, staging, structured logs, timeout, cancellation, process-tree kill, retry, resume, cleanup and report.
4. Three production-quality blueprints:
   - `web.react-vite-ts`
   - `desktop.csharp-wpf-tool`
   - `tool.python-cli`
5. Local Git init/commit and basic branch policies.
6. Optional GitHub publish through `gh`, private by default, with `PublishPending` recovery.
7. Team Standard profiles, saved presets and handoff documents.
8. Unit, integration, blueprint contract, E2E, failure and security tests.

## 4. Required solution structure

```text
DevForge/
├── src/
│   ├── DevForge.Desktop/
│   ├── DevForge.Application/
│   ├── DevForge.Domain/
│   ├── DevForge.Infrastructure/
│   ├── DevForge.Blueprints.Abstractions/
│   ├── DevForge.Blueprints.BuiltIn/
│   └── DevForge.Cli/
├── tests/
│   ├── DevForge.UnitTests/
│   ├── DevForge.IntegrationTests/
│   ├── DevForge.BlueprintTests/
│   └── DevForge.E2ETests/
├── blueprints/
├── docs/
├── scripts/
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
└── DevForge.sln
```

### Dependency rules

- Domain references no UI, EF Core, WPF, process or file-system implementation.
- Application references Domain and abstractions only.
- Infrastructure implements process, file, persistence, template, Git, GitHub and IDE services.
- Desktop contains views/viewmodels/navigation only; it never runs external processes directly.
- CLI and Desktop reuse the same Application layer.

## 5. Core contracts

- `IProjectPlanner`
- `IExecutionOrchestrator`
- `IProcessRunner`
- `IFileSystem`
- `ITemplateRenderer`
- `IBlueprintCatalog`
- `IEnvironmentDoctor`
- `IRunJournalStore`
- `IGitService`
- `IGitHubService`
- `ISecretScanner`
- `IIdeLauncher`

`IProcessRunner` must use `ProcessStartInfo.ArgumentList`, redirect stdout/stderr asynchronously, support per-step timeout, cancellation, redaction and child-process-tree termination. Exit code alone is not sufficient; every step may define postconditions.

## 6. One-button pipeline

1. Validate and normalize input.
2. Preflight tools, versions, write access, disk space and optional GitHub auth.
3. Resolve blueprint version and compatibility rules.
4. Produce and preview an immutable `ExecutionPlan`.
5. Create a run-owned staging workspace.
6. Scaffold through the official framework CLI or create a template skeleton.
7. Apply Team Standard and selected feature overlays.
8. Install dependencies and create lockfiles.
9. Run format, lint, type-check, tests, build, smoke checks and secret scan.
10. Finalize atomically into the target directory.
11. Initialize Git and create the initial commit.
12. Optionally create and push a GitHub repository.
13. Persist recipe, lock, policy snapshot and generation report.
14. Open the selected IDE and show handoff information.

Required statuses: `Draft`, `Planning`, `PreflightFailed`, `Executing`, `ValidationFailed`, `LocalReady`, `PublishPending`, `Completed`, `Cancelled`, `Failed`.

## 7. Blueprint rules

Blueprint package:

```text
<blueprint-id>/
├── manifest.yaml
├── inputs.schema.json
├── rules.yaml
├── templates/
├── overlays/
├── validators/
├── migrations/
├── README.md
└── checksums.json
```

Allowed handlers in MVP: create-directory, render-template, copy-overlay, patch-json/yaml/xml, run-process, package-install, validate-command, built-in Git/GitHub operations and finalize-workspace.

Trust levels: `BuiltIn`, `TrustedLocal`, `Untrusted`, `Quarantined`. Untrusted blueprints are inspect-only. Deny path traversal, arbitrary deletion, registry/firewall/service changes, admin requirements, arbitrary PowerShell and executable downloads.

## 8. Persistence and privacy

SQLite tables: AppSettings, IdeInstallations, EnvironmentTools, Blueprints, TeamProfiles, Presets, ProjectRuns, RunSteps, RecentProjects and SchemaMigrations.

Never persist:

- GitHub token or `gh auth token` output.
- Passwords, private keys, connection strings or `.env` content.
- Customer source code.
- Unredacted logs containing credentials.

## 9. Git/GitHub rules

- Git only after local quality gates pass, unless a blueprint explicitly requires earlier initialization.
- Initial commit: `chore: bootstrap project with DevForge`.
- Default repository visibility: private.
- MVP branch policies: `main` or `main + develop`.
- GitHub auth is delegated to `gh auth login/status/switch`.
- Publish failure must keep the local project and transition to `PublishPending`.
- Never force-push, delete a remote or overwrite an existing repository.

## 10. Required testing

- Unit tests: validators, rules, planner, path guards, renderer context, error mapping.
- Contract tests: manifest/schema, action whitelist, checksum, variables and expected tree.
- Integration tests: process runner, file system, SQLite migrations, Git and templates.
- Blueprint E2E: generate -> install -> lint/test/build -> Git clean.
- Failure tests: network, timeout, locked file, auth fail, app kill, cancel and resume.
- Security tests: command injection, traversal, symlink/junction escape, malicious pack and secret leakage.

## 11. Milestone order

- M0: Repository baseline and CI.
- M1: Domain and contracts.
- M2: Persistence.
- M3: Core infrastructure.
- M4: Planner, rules and blueprint catalog.
- M5: Orchestrator, staging, retry/resume and finalizer.
- M6: WPF shell, settings and environment doctor.
- M7: Dynamic Create Project, Plan Preview, Execution Center and Completed.
- M8: Git/GitHub.
- M9: Three MVP blueprints.
- M10: Security, diagnostics, packaging and release hardening.
- M11: V1 catalog only after M10 gates pass.

Do not begin the next milestone until the current exit gate passes and `docs/implementation-status.md` is updated.

## 12. Definition of Done for a generated project

A run is complete only when input/compatibility validation, staging, scaffold/overlay, dependency install, lockfile, `.env.example`, lint/type-check/tests/build, secret scan, finalization, Git state, optional publish, recipe/lock/policy/report and IDE/handoff actions all satisfy the selected policy.

## 13. Master Prompt for Codex

```text
You are the Principal Software Engineer responsible for DevForge Studio - Leader Edition.

Read the entire attached DevForge specification before editing code. It is the source of truth. Build a native Windows desktop application in C# + WPF on .NET 10 using MVVM and Clean Architecture. Do not replace it with a web shell, Electron, Tauri or Blazor Hybrid. Do not add AI APIs, a cloud backend or outbound telemetry.

Inspect the repository first. Create/update docs/implementation-plan.md, docs/implementation-status.md and ADRs. Work milestone by milestone. If the repository is empty, implement M0 only: solution structure, project references, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig, README, CI skeleton and architecture tests. Do not build large UI, GitHub automation or multiple blueprints in M0.

All external commands must go through IProcessRunner using executable + ArgumentList, async output, timeout, cancellation, redaction and child-process-tree termination. All file operations must go through a guarded IFileSystem. Never use arbitrary cmd /c or PowerShell strings. Never store/log secrets or call gh auth token. Pin packages centrally; do not use latest/wildcards.

Write production-quality code and tests. Run restore, format, build and tests for real. Do not claim success without command evidence. Do not push, create a remote, force-push, delete or overwrite user data unless explicitly requested. Choose safe defaults and record ADRs; ask only for true blockers or destructive ambiguity.

After work report: scope completed, key decisions, files changed, commands and exact results, tests, limitations/technical debt and recommended next milestone.
```
