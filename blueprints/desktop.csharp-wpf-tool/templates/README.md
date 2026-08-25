# TeamTool

TeamTool is a native Windows desktop tool built with C# 14, WPF, .NET 10, MVVM, Generic Host, dependency injection, configuration, and structured logging.

## Start here

Read `TEAM_START_HERE.md`, then run:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet build TeamTool.slnx --configuration Release --no-restore
dotnet test TeamTool.slnx --configuration Release --no-build --no-restore
```

No Administrator session, web runtime, embedded browser, or cloud account is required.
