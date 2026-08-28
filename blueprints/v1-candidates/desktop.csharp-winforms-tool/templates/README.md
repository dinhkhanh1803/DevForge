# TeamTool

TeamTool is a native Windows desktop tool built with C# 14, WinForms, .NET 10, MVVM, Generic Host, dependency injection, configuration, and structured logging.

## Start here

Read `TEAM_START_HERE.md`, then run:

```powershell
dotnet restore TeamTool.slnx --locked-mode
dotnet build TeamTool.slnx --configuration Release --no-restore
dotnet test TeamTool.slnx --configuration Release --no-build --no-restore
```

No Administrator session, web runtime, embedded browser, or cloud account is required.

## Repository layout

`src` contains the Domain, Application, Infrastructure, and WinForms Desktop projects; `tests` contains the unit test project. Solution-wide build policy and exact package versions live at the repository root.

## Local setup

Install the .NET 10 SDK selected by `global.json`, then use the locked restore command above from the repository root.

## Quality gates

Run every command in `TESTING.md` before review or release preparation.
