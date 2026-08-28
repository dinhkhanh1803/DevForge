# C# WinForms Tool candidate

Identity: `desktop.csharp-winforms-tool@1.0.0`.

This M11 candidate generates a native Windows Forms tool with C# 14/.NET 10,
MVVM Toolkit, Generic Host, DI, configuration, and the five-project TeamTool
Clean Architecture layout. It is not included in the shipped MVP catalog.

## Prerequisites and quality gates

Windows and .NET SDK 10.0.302 (selected by global.json). The package requires locked
restore, format verification, Release build, unit tests, and WindowsSmoke publish.
No new command identity, package manager, cloud account, or SDK installation
permission is introduced.

## Native interface

The status label observes the application ViewModel; Refresh (Alt+R or Enter)
updates it through the existing application service. The form uses a DPI-aware
layout and system accessibility. Configuration is loaded from the application
directory and contains no credentials.

## Certification

Local toolchain and native smoke evidence is recorded in implementation-status.md.
Local Windows 10 acceptance passes with the production .NET runner, responsive
native Refresh/close smoke, clean initial Git commit and durable publication retry.
The engine records exact build-output membership while keeping every output byte
in the finalized integrity digest; build outputs are retained, not committed.
Windows 11 certification and release promotion remain Pending. Candidate
development does not close the outstanding M9/M10 release gates.

## Package maintenance

All payload files are covered by checksums.json. Generated project dependencies
are centrally pinned and every project carries its NuGet lock. Never update a
dependency or promote this package without repeating its independent matrix.
