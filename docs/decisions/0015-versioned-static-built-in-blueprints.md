# ADR-0015: Versioned static built-in blueprint packages

**Status:** Accepted

**Date:** 2026-08-25

## Context

M9 must ship exactly three production blueprints that are deterministic, reviewable, checksummed, and executable through the existing guarded planner and orchestration boundaries. Runtime framework scaffolders can resolve changing templates or packages and require a broader network/process policy. C#-hardcoded generation would hide the documented blueprint package contract and make package release review harder.

The existing loader defines one canonical source-root directory per blueprint ID. The exact semantic version is declared by `manifest.yaml` and bound into the aggregate package checksum and plan fingerprint; it is not represented by a second directory level.

## Decision

- Author exactly `desktop.csharp-wpf-tool`, `web.react-vite-ts`, and `tool.python-cli` as static package directories under `blueprints/<id>`.
- Ship the authoring tree into build and publish output at the fixed application-relative `blueprints\built-in` location. `DevForge.Blueprints.BuiltIn` owns the stable source identity/location vocabulary; consuming Desktop and contract-test outputs explicitly include the immutable assets.
- Desktop opens that root through `IFileSystem` with `BlueprintSourceProvenance.BuiltIn`, before creating or opening the separate user-writable `blueprints\local` source. A missing or unopenable built-in root fails before local blueprint storage mutation.
- Trust derives only from composed source provenance. Manifest content cannot assign built-in or trusted-local trust.
- Packages use reviewed skeletons, exact lockfiles, restricted templates, closed actions, complete checksums, and deterministic plan/expected-tree contracts. Runtime scaffolders and unversioned dependency resolution are not used.
- Real package contracts load production assets through the production `BlueprintCatalog` and guarded Windows filesystem boundary.

## Consequences

Built-in assets are directly inspectable and checksum-verifiable, normal Desktop build output discovers the same bytes tested by the production contract suite, and local packages remain isolated from built-in trust. Updating a blueprint requires a manifest semantic-version change, refreshed exact locks and checksums, expected-tree review, and the release matrix.

The build and M10 installer must preserve the fixed output directory. Only one version of a blueprint ID can be present in one source root with the current catalog model; side-by-side installed versions require a future reviewed catalog contract rather than an undocumented directory convention.

## Rejected alternatives

- Invoking online `create-*`, `dotnet new`, or equivalent scaffolders during generation.
- Accepting a `<blueprint-id>/<version>` tree that the production loader does not recognize.
- Copying shipped packages into the trusted-local user directory.
- Allowing manifest text, filename, or user choice to self-assign built-in trust.
- Hardcoding generated project files inside C# handlers.
