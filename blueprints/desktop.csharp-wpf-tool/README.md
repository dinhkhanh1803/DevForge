# C# WPF Tool blueprint

This built-in package produces a native .NET 10 WPF desktop tool using MVVM, Generic Host dependency injection, structured logging, typed configuration, nullable reference types, analyzers, unit tests, and a checked-in publish profile.

The package is deterministic: framework files are shipped as reviewed templates and overlays, dependency versions are centrally pinned, NuGet lockfiles are required, and every package byte is declared by `checksums.json`.

Generation requires Windows and .NET SDK `>=10.0.0 <11.0.0`. The required quality gates are locked restore, format verification, Release build, unit tests, and Windows publish smoke.
