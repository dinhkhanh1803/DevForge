# ADR-0025: Full-tree integrity with explicit build-output membership

Date: 2026-08-27
Status: Accepted for the explicitly requested M11 repair; no release waiver.

## Decision

Retain the finalized digest over every byte/path except owned root Git metadata.
Write an optional canonical `.devforge/build-outputs.json` from the reviewed plan
at the existing atomic evidence-writing boundary. It records exact output file
membership and the reviewed .csproj paths. Only descendants of bin/obj beside a
reviewed project, and artifacts/publish for the reviewed fixed WindowsSmoke
validator, qualify. Reviewed artifact paths always remain source, even within an
output-shaped directory. All other files remain source. No .gitignore parsing,
wildcard ignored-file exemption, force-add, deletion, or command argument change.

The marker is reserved from blueprint actions and preview artifacts, canonical,
bounded, write-once/exact-retry, and itself committed. It is bound by both the
project lock integrity inventory and the durable full-tree digest. Git verifies
the exact source subset while secret scanning and tamper checks still cover all
files. Missing, extra, changed outputs or changed membership invalidate the
persisted digest. Without the marker, historical checkpoints retain all-file
Git verification. No persistence schema or migration is necessary.

## Production .NET environment

After resolving a trusted dotnet executable, the production runner declares a
small runtime environment: SDK root/host, SDK-plus-System32 PATH, OS/profile/temp
folders needed by NuGet/MSBuild, telemetry opt-out, and disabled reusable build
servers. It never copies the ambient environment wholesale. Protected names
cannot be overridden by CommandSpec. Other tool identities retain existing
environment semantics, including isolated Git/gh credentials. No environment
values are logged or persisted. Acceptance uses the production runner unmodified.

## Alternatives and consequences

Deleting build outputs requires additional ownership and recovery semantics;
force-adding binaries pollutes handoff and violates ignored-source expectations.
Exempting every ignored path weakens integrity. An explicit digest-bound manifest
preserves available smoke binaries without these changes. This slice supports the
reviewed .NET output convention only; Node/Python policies remain separate work.

Required gates: real .NET/native/Git acceptance; ignored-source rejection; output
tamper and marker corruption rejection; idempotent evidence writes; production
environment isolation; complete existing recovery/security regressions. No new
blueprint is introduced until these gates pass.

## Verified refinements

Canonical JSON is indented with LF so large output inventories remain scannable
within the unchanged 16,384-character scanner line limit. AllFiles is supplied to
the existing text-candidate scanner, including ignored output JSON/config files;
the binary/text scanner policy is not widened or bypassed. Membership is parsed
from the exact bounded bytes hashed in the full-tree loop, never a second read.
The deterministic swapping-read regression fails without that binding.

The candidate now declares all five project files in its hash-bound artifact
preview. This policy only classifies outputs for explicitly reviewed projects;
undeclared project roots and other ecosystem outputs are not silently excluded.
Real local acceptance passed 5/5; Windows 11 certification remains separate.
