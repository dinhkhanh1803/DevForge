# DevForge Studio Maintainer Guide

## Architecture and safety

DevForge is native C# 14/WPF on .NET 10 using MVVM and Clean Architecture. Domain and Blueprint Abstractions are independent; Application owns use cases and ports; Infrastructure owns guarded file/process/database/Git/GitHub adapters; Desktop and CLI are composition roots. Product file access belongs behind guarded workspace/filesystem abstractions. External commands use a closed `ExecutableIdentity` and separated `ArgumentList` through `IProcessRunner`.

Do not add web shells, embedded browsers, AI/cloud APIs, telemetry, arbitrary commands, Administrator requirements, raw credentials, or shell strings. Package versions remain exact in `Directory.Packages.props`; `win-x64` runtime assets remain represented in checked-in lock files.

## Local verification

Run from the repository root with SDK 10.0.302:

```powershell
dotnet restore DevForge.sln --locked-mode
dotnet format DevForge.sln --verify-no-changes --no-restore
dotnet build DevForge.sln --configuration Release --no-restore
dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj --configuration Release --no-build --no-restore
```

The current local counts are Unit 651, Integration 601, Blueprint 127, and E2E 216, with zero failed or skipped tests. Counts are evidence, not permanent expectations; update the checklist only after rerunning every gate.

## Persistence and Recovery

EF Core SQLite migrations are tracked under `src/DevForge.Infrastructure/Persistence/Migrations`. Before a pending upgrade to an existing database, `SqliteMigrationCoordinator` creates an online backup, applies migrations, verifies integrity, and restores the backup on migration or integrity failure. Run the pending-model assertion and fresh/upgrade/restored-failure integration tests after every model change. Never edit a shipped migration or delete retained recovery evidence automatically.

## Diagnostics operations

Production diagnostics are bounded marker-owned JSONL files with a process-wide/cross-process lease. Retention defaults to 30 days and 256 MiB, deletes only exact marker-owned candidates, and preserves unrelated paths. Support bundles accept authoritative checkpoints, a closed evidence inventory, strict UTF-8, per-entry and aggregate caps, secret scans, deterministic ZIP metadata, atomic publication, and marker-plus-digest cleanup authority.

## Release package

```powershell
dotnet restore src/DevForge.Desktop/DevForge.Desktop.csproj --locked-mode --runtime win-x64
dotnet publish src/DevForge.Desktop/DevForge.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=ReleaseWinX64 --output artifacts/release/win-x64
./scripts/Test-ReleasePackage.ps1 -PackageRoot artifacts/release/win-x64
```

The audit must run before artifact upload. It requires the self-contained runtime, application metadata, release docs, catalog README, and exactly three blueprint roots; it rejects database, key, shell-script, and support-bundle payloads. Package process smoke uses only a descendant of `%TEMP%\DevForge-ReleasePackageTests` and covers fresh, upgrade, and restored-failure safe mode.

## CI and release ownership

CI actions are pinned by commit SHA and permissions are `contents: read`. A release status becomes Pass only from a linked command, test, or reviewed artifact. Windows 10 results never satisfy a Windows 11 row. Do not push, create a remote, publish a package, or mutate a real GitHub repository unless the task explicitly authorizes it.
