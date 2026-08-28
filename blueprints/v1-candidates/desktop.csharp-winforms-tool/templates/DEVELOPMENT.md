# Development

Install a supported .NET 10 SDK on Windows. Restore with the checked-in lockfiles:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet format TeamTool.slnx --verify-no-changes --no-restore
dotnet build TeamTool.slnx --configuration Release --no-restore
```

Application configuration belongs in `appsettings.json`; secrets belong in an approved external secret store and never in this repository.

## Prerequisites

Use Windows and the .NET 10 SDK selected by `global.json`; no JavaScript runtime or elevated shell is required.

## Local setup

From the repository root, run locked restore, formatting verification, and the Release build shown above.

## Environment

Use the SDK selected by `global.json`. Keep machine-specific values outside the repository; `appsettings.json` contains only non-secret defaults.

## Database

No database is used by this blueprint.

## Debugging

Debug `src/TeamTool.Desktop/TeamTool.Desktop.csproj` from Visual Studio or run `dotnet run --project src/TeamTool.Desktop/TeamTool.Desktop.csproj`. Reproduce failures with the Release build and test commands in `TESTING.md` before changing code.
