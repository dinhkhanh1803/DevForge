# ADR-0024: M11 candidate development with explicit open release debt

Date: 2026-08-27

Status: Accepted for development by the owner's explicit request to move to M11.

## Context

The detailed baseline sections 17.2 and 18 define eight independently released V1 blueprints. Section 18.1 permits subsequent milestone development when outstanding technical debt is explicitly accepted and recorded. The owner requested M11 after being informed that Windows 11 and remote CI evidence for M9/M10 remains unavailable. This authorizes development, not release certification, remote publication, operating-system upgrades, or cloud provisioning.

## Decision

- Start with `desktop.csharp-winforms-tool` version `1.0.0` as an isolated candidate under `blueprints/v1-candidates/`.
- Reuse the existing .NET 10 closed process vocabulary and five-project TeamTool layout. No DevForge runtime dependency, security capability, or WPF shell change is needed.
- Candidate payload is copied only into test output `blueprints/candidates`; shipped BuiltIn content and the M10 package audit retain exactly the three MVP roots.
- Use native WinForms for generated projects only; DevForge itself remains native WPF/MVVM/Clean Architecture.
- Independently certify each candidate through catalog, deterministic generation, real toolchain, native smoke, and Git-clean tests. Windows 10 evidence is local development evidence, never Windows 11 certification.
- Keep M9/M10 release gates Pending until their original evidence exists. Promoting a candidate into the release catalog is a separate reviewed change after that candidate's required release matrix and the product release gates pass.

## Alternatives

Starting with Next/Nest adds Node package and execution-policy work before the first independent V1 slice. Starting all eight candidates at once obscures per-blueprint acceptance. WinForms uses the already available .NET SDK, so it is the first slice; the remaining candidates are implemented separately.

## Consequences and acceptance

M11 development is active, but the product is not release-ready. Tests must prove candidate absence from the default BuiltIn catalog and package declarations, not merely document it. No wildcard shipping or arbitrary shell escape is introduced. M11 completion requires all eight independent blueprint gates; this decision completes none of them by itself.

## Integration checkpoint (not a waiver)

The real WinForms matrix exposed a pre-existing boundary mismatch: finalization
digests every payload file, while Git correctly respects the package's .gitignore.
Real restore/build/publish leaves bin/obj/artifacts files, so exact committed-tree
verification rejects publication (DF-GIT-004, surfaced as DF-PUB-003 by the
coordinator). The existing ignored-path Git regression requires this rejection.
The candidate therefore remains incomplete; no security check is weakened and no
acceptance test is skipped or changed to expect success on failure.

A follow-up design must distinguish engine-owned validation outputs from the
reviewed source tree while retaining full finalized-tree tamper detection and
kill-window recovery. This is not authorization to exclude arbitrary ignored
files or delete user directories. The real .NET harness supplies a bounded
test-only host environment; production command-environment composition remains
another promotion prerequisite. Windows 10 smoke is not Desktop/Windows 11
certification.
