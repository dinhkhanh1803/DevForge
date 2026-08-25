# DevForge Studio

DevForge Studio is a planned native Windows project factory for team leaders and freelancers. The application is built with C# and WPF on .NET 10 using MVVM and Clean Architecture. It will generate projects from versioned local blueprints and official local CLIs; it is not an AI code generator and has no cloud backend.

## Current status

Milestones M0-M8 are implemented. The native WPF application composes validated domain contracts, deterministic local blueprint planning, SQLite persistence, guarded Windows infrastructure, recoverable generation, and evidence-backed Git/GitHub completion into a persistent desktop shell. Dashboard, Settings, Environment Doctor, Create Project, Blueprint Catalog, Run History, reviewed Plan Preview, Execution Center, `LocalReady`, `PublishPending`, and `Completed` evidence are functional. Read-only startup safe mode keeps all mutating creation, recovery, and publication actions disabled.

Reviewed projects can be initialized as an exact clean local Git repository with the fixed bootstrap commit and `main` or `main + develop`. Optional personal GitHub publication is private by default, verifies the exact `github.com` account, binds recovery to an ownership nonce, and retries from durable checkpoints without rerunning generation or duplicating the commit. Production blueprints, packaging/release hardening, and catalog expansion remain assigned to M9-M11. The product has no AI API, cloud backend, embedded browser, arbitrary shell execution, or Administrator happy-path requirement.

## Prerequisites

- Windows 10 or Windows 11.
- .NET SDK 10.0.302 or a later patch in the 10.0.3xx feature band.
- Git for local source control.

The SDK selection is defined by `global.json`. NuGet versions are exact and centralized in `Directory.Packages.props`.

## Verify the repository

Run these commands from the repository root:

```powershell
dotnet restore DevForge.sln --locked-mode
dotnet format DevForge.sln --verify-no-changes --no-restore
dotnet build DevForge.sln --configuration Release --no-restore
dotnet test DevForge.sln --configuration Release --no-build
```

CI runs the same mandatory gates on Windows and retains only generated TRX test results as a short-lived build artifact.

## Solution structure

```text
src/
  DevForge.Desktop/                 Native WPF composition root
  DevForge.Application/             Use cases and ports (from M1 onward)
  DevForge.Domain/                  Domain model (from M1 onward)
  DevForge.Infrastructure/          Adapter implementations (from M2/M3 onward)
  DevForge.Blueprints.Abstractions/ Public blueprint contracts
  DevForge.Blueprints.BuiltIn/      Built-in blueprint implementations
  DevForge.Cli/                     CLI composition root
tests/
  DevForge.UnitTests/               Unit and architecture tests
  DevForge.IntegrationTests/        Infrastructure integration tests
  DevForge.BlueprintTests/          Blueprint contract tests
  DevForge.E2ETests/                End-to-end generation tests
blueprints/                          Milestone-owned blueprint packages
scripts/                             Maintainer automation
docs/                                Specification, plans, status, and ADRs
```

## Dependency rules

- Domain and Blueprint Abstractions reference no other DevForge project.
- Application references only Domain and Blueprint Abstractions.
- Infrastructure references Application, Domain, and Blueprint Abstractions.
- Built-in Blueprints reference Blueprint Abstractions.
- Desktop and CLI compose Application with Infrastructure.
- Presentation code must never launch processes or access the file system directly. Those operations will use guarded abstractions introduced in M3.

Executable architecture tests enforce the project graph, target frameworks, WPF boundary, solution membership, and central package-version policy.

## Safety boundaries

- No web shell, Electron, Tauri, Blazor Hybrid, or embedded browser.
- No OpenAI, Gemini, other AI API, cloud backend, or outbound telemetry.
- No arbitrary shell strings, Administrator requirement, or secret persistence.
- Git/GitHub operations use a closed typed vocabulary through the guarded process runner; no shell string, token command, force push, remote deletion, or unrelated repository adoption is allowed.
- GitHub publication is opt-in and private by default. Automated tests use deterministic fakes and never create or contact a real repository.
- Project finalization, Git bootstrap, and publication receipts are no-overwrite operations; unowned or mismatched local/remote state fails closed.

See `docs/implementation-plan.md` for the milestone plan and `docs/implementation-status.md` for verified gate evidence.

