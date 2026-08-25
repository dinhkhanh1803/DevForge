# ADR-0016: Closed production blueprint validation vocabulary

**Status:** Accepted

**Date:** 2026-08-25

## Context

Production blueprints must prove that their generated projects restore, format, build, test, and package successfully without turning a trusted manifest into an arbitrary command channel. The existing `validate-command` handler admits a small .NET validation vocabulary through `IProcessRunner`; WPF publish smoke needs one additional operation.

## Decision

- Keep project generation static and checksum-bound. The WPF package copies reviewed source/test overlays and renders only fixed root artifacts; it does not invoke `dotnet new` or an online scaffolder.
- Pin all generated NuGet versions centrally and ship exact `packages.lock.json` files. Restore always uses `--locked-mode`.
- Permit WPF publish smoke only as the exact separated argument sequence `dotnet publish src\TeamTool.Desktop\TeamTool.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=WindowsSmoke`.
- Admit that sequence only through the validator handler. The general process handler remains unable to publish, and project/profile/option mutations fail before runner invocation.
- Keep publish output inside the generated project at `artifacts\publish`; a profile may not traverse above the project root.
- Prove generation separately from the real toolchain: composed E2E uses the production catalog/planner/staging/handler/finalizer boundaries with a typed recording runner, while a standalone temp tree runs the real locked .NET matrix without inheriting DevForge configuration.

## Consequences

The package is deterministic and reviewable, real validation evidence cannot be confused with inherited repository settings, and the new capability does not expose a general `dotnet publish` escape hatch. Any future blueprint that needs a different publish target or option set requires an explicit policy change and regression tests.

The architecture test repository model ignores `bin` and `obj` directories because build output now legitimately contains blueprint payload `.csproj` files. Only authored DevForge projects under `src` and `tests` participate in dependency-graph enforcement.

## Rejected alternatives

- Allowing arbitrary `dotnet publish` arguments from a manifest.
- Reusing `run-process` for publish.
- Running the real generated-project matrix only beneath the DevForge repository, where parent build settings can mask defects.
- Producing unlocked or floating NuGet dependencies.
- Publishing outside the generated project tree.
