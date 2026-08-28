# ADR-0027: uv production environment and run-owned Python tooling

Date: 2026-08-27
Status: Accepted and locally verified; no Windows 11/remote release waiver.

## Context and decision

The unmodified production runner reproduces uv DNS failure 11003 outside the
sandbox. Its cleared environment omits Windows runtime folders. Declare a small
uv-specific environment, resolve Python through the trusted executable resolver,
disable interpreter downloads, use copy linking, and never inherit ambient
credentials, proxy/index configuration, PYTHONPATH, or activated environments.
Keep the exact frozen/no-config command vocabulary. Protected runtime values
cannot be overridden. Version-only uv probes do not require Python.

The owned staging lease also exposes its guarded container workspace. Python
execution prepares tooling beside payload, inside that container: virtualenv
and mypy cache. uv uses no persistent shared cache; Ruff caching
and bytecode writes are disabled, pytest's cache provider is disabled. No tooling
directory is moved to the target or excluded from an otherwise finalized tree.
Existing marker/lease/replay/cleanup ownership applies; no new deletion path.
This avoids shipping editable-install and executable paths pointing to staging.
The handoff remains source plus distribution packages; developers run the already
documented frozen sync at the final path to create their own environment.

uv's no-cache build isolation uses ephemeral directories in the declared OS temp
directory, managed by uv itself. Putting TEMP inside deeply nested staging
reproduced a Windows path-length failure in build metadata; using OS temp removed
it. No registry, Administrator requirement, global cache deletion or path-length
policy change. Deep user-selected targets may still exceed third-party limits
and fail safely; the engine does not silently relocate the user's target.

Finalized cleanup accepts only the marker and recognized tooling root after
bounded enumeration with no exclusions. Its bounds account for exactly one marker
file and one tooling-root directory beyond the unchanged tooling subtree limits.
Nested junction, over-bound and exact-bound regressions protect this boundary;
unexpected siblings still prevent deletion. The existing run-owned cleanup does
the deletion; the finalized target is never a cleanup target.

Only exact dist files produced under a reviewed root pyproject.toml/uv.lock and
mandatory uv build validator qualify as Python build outputs. Extend the existing
canonical membership format with a separate Python schema, preserving old .NET
bytes. Full-tree digest and secret scan remain unchanged. .venv or cache appearing
in payload remains unrecognized source and must not silently pass publication.

## Alternatives

Transferring .venv and enabling relocatable scripts does not repair editable
source paths. Deleting payload directories after validation adds destructive
recovery windows. Ignoring all Git-ignored paths loses integrity. A run-owned
tooling workspace avoids these problems at the existing staging boundary.

## Gates

Environment isolation/protected override tests; guarded sibling tooling tests;
dist membership and tamper tests; actual frozen Python install, seven validators,
finalization, local Git publication, repeated recovery; no .venv/cache in target;
post-finalization frozen sync and native smoke; full restore/format/build/test.
No blueprint expansion, remote operation, package upgrade or release waiver.

Reference: [uv environment settings](https://docs.astral.sh/uv/reference/environment/)
documents interpreter selection, download disabling, copy linking and no-cache.
