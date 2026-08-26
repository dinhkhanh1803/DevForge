# Testing

## Test levels

The current automated suite contains unit tests, run with `dotnet test TeamTool.slnx --configuration Release --no-build --no-restore`. No dedicated integration test suite exists yet. The publish command below is a publish smoke check, not an integration test.

## Release gate

Run the deterministic local quality gate:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet format TeamTool.slnx --verify-no-changes --no-restore
dotnet build TeamTool.slnx --configuration Release --no-restore
dotnet test TeamTool.slnx --configuration Release --no-build --no-restore
dotnet publish src/TeamTool.Desktop/TeamTool.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=WindowsSmoke
```
