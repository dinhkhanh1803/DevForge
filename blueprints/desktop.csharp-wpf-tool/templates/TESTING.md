# Testing

Run the deterministic local quality gate:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet format TeamTool.slnx --verify-no-changes --no-restore
dotnet build TeamTool.slnx --configuration Release --no-restore
dotnet test TeamTool.slnx --configuration Release --no-build --no-restore
dotnet publish src/TeamTool.Desktop/TeamTool.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=WindowsSmoke
```
