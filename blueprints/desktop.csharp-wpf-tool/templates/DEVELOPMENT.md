# Development

Install a supported .NET 10 SDK on Windows. Restore with the checked-in lockfiles:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet format TeamTool.slnx --verify-no-changes --no-restore
dotnet build TeamTool.slnx --configuration Release --no-restore
```

Application configuration belongs in `appsettings.json`; secrets belong in an approved external secret store and never in this repository.
