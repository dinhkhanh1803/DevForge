# Testing

## Test levels

Unit tests verify the domain message, initial ViewModel status, and Refresh
property-change notifications. They use application interfaces, not a live window.
No dedicated integration test suite exists yet.

## Release gate

Run from the repository root:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet format TeamTool.slnx --verify-no-changes --no-restore
dotnet build TeamTool.slnx --configuration Release --no-restore
dotnet test TeamTool.slnx --configuration Release --no-build --no-restore
dotnet publish src/TeamTool.Desktop/TeamTool.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=WindowsSmoke
```

Open `artifacts/publish/TeamTool.Desktop.exe` on the supported Windows host,
activate Refresh through keyboard and mouse, verify the status updates, and close
the window. Repeat on Windows 11 at 100/125/150% display scaling before release.
The publish smoke is framework-dependent and requires the .NET 10 Desktop Runtime.
