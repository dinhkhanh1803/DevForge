# DevForge Studio

DevForge Studio is a planned native Windows project factory for team leaders and freelancers. The application is built with C# and WPF on .NET 10 using MVVM and Clean Architecture. It will generate projects from versioned local blueprints and official local CLIs; it is not an AI code generator and has no cloud backend.

## Current status

Milestone M1 is complete. The repository now includes immutable validated domain models, blueprint manifest contracts, and twelve security-shaped Application ports, with 305 focused tests covering domain, blueprint, architecture, request, process, file-system, privacy, and serialization boundaries. Infrastructure implementations, persistence, orchestration, Git/GitHub automation, production blueprints, and UI workflows remain assigned to later milestones.

The CLI still exits with a nonzero status because no commands are available yet. The WPF project is a minimal native shell, not a completed UI.

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
- No GitHub push, force-push, or destructive target-directory behavior is implemented in M1.

See `docs/implementation-plan.md` for the milestone plan and `docs/implementation-status.md` for verified gate evidence.

