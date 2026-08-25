# Architecture

Dependencies point inward:

```text
TeamTool.Desktop -> TeamTool.Infrastructure -> TeamTool.Application -> TeamTool.Domain
TeamTool.Desktop ---------------------------> TeamTool.Application
```

`Domain` contains business values. `Application` contains use-case contracts. `Infrastructure` implements operating-system concerns. `Desktop` is the native WPF composition and presentation layer. View models use CommunityToolkit.Mvvm and do not perform direct filesystem or process operations.
