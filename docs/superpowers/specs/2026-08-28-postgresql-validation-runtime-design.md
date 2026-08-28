# PostgreSQL validation runtime design

Date: 2026-08-28. Scope: M11 prerequisite for `backend.nest-postgres`.
Status: proposed design; bootstrap feasibility gate failed. Implementation is
held on the shell-policy boundary below; this is not an accepted runtime.

## Proof outcome and policy hold

A temporary test-first IProcessRunner/stdin extension passed two managed helper
checks but real initdb 18.6 failed before creating cluster files. Outside the
sandbox it exited `-1073741819` (`0xC0000005`); adding only trusted SystemRoot
did not fix the failure. The crash root cause is not established. This does not
prove password delivery, SCRAM, ownership or recovery for PostgreSQL.

Exact `REL_18_6` source shows initdb calling `popen_check`; the Windows wrapper in
`src/port/system.c` delegates to CRT `_popen`. Microsoft documents that this
spawns the command processor. An unmodified Windows initdb therefore cannot be
represented as a shell-free process tree because its outer call uses ArgumentList.
No COMSPEC or shell-enabling workaround was added. Under the owner's unqualified
cmd /c prohibition, continuing requires an explicit policy decision about official
CLI-internal shell execution; standing approval of recommendations is not a waiver.

Experimental product/test additions were removed, preserving existing changes.
No failed proof was relabeled as a passing test or silently skipped. The binary
download and [verification record](../../verification-2026-08-28-postgresql-runtime.md)
remain, not a new executable permission.

Alternatives for the required decision:

1. Keep the strict ban and separately review a shell-free bootstrap distribution
   or sanitized pre-provisioned cluster-image workflow. This requires additional
   supply-chain/compatibility work; neither is certified by this design.
2. Explicitly allow reviewed initdb-internal shell execution only. Outer calls
   still use IProcessRunner/ArgumentList, pinned runtime and shell identity,
   constrained paths/arguments and contained descendants. Blueprint-supplied shell
   text stays forbidden. This is a policy change, not implemented here.

Do not substitute an existing service, password file, trust auth, Docker or a
fake COMSPEC shim to bypass this decision.

## Boundary and alternatives

DevForge remains C#/WPF/.NET 10 and keeps SQLite metadata. PostgreSQL is an
ephemeral validation dependency, not DevForge persistence or a deployment target.
The detailed baseline was read through paragraph 1311, including FR-003/032,
sections 12.3, 14.2, 15.3, 17.2 and 18.1. ADR-0024 release holds remain.

Select pre-provisioned Windows x64 PostgreSQL binaries and a fresh cluster owned
by one validation attempt. No service, installer, registry, firewall, Docker,
existing database, user password, download during project execution or catalog
promotion. Docker would add daemon/image/volume ownership; using an existing DB
cannot meet automatic destructive cleanup safety. Neither is a fallback.

Host inspection found `postgresql-x64-18` Running and installed 18.2 binaries
outside PATH. Do not use, upgrade, stop, query or inspect that service's data.
A separate EDB 18.6-1 archive has been provisioned under ignored `.tools` with
network approval. Its measured SHA-256 is
`FBE23DA234EE31547BF8A36D29DFD81E82B849DF2D2B78D2EECB43D360252F8C`.
This is a measured artifact digest from the official HTTPS download, not an
independently published signature. The executables report unsigned Authenticode;
do not describe them as signature-verified. A production installation profile
must bind the complete binary/library/share closure, not just postgres.exe.

## Components and authority

- Application process contracts retain executable identity + ArgumentList. Add
  bounded ephemeral standard input without putting its content into previews,
  serialization, logs or arguments. Register every input secret for output
  redaction before the child starts. Writing stdin and draining both output
  streams must run concurrently; blocked stdin must not defeat timeout/cancel.
- Infrastructure owns closed PostgreSQL operations: version, initialize, start
  foreground, authenticated identity probe, scoped role/database provisioning,
  stop and owned cleanup. No arbitrary SQL or PostgreSQL argument bag is exposed
  to blueprints. The existing run-process/validate-command allowlists stay closed.
- Infrastructure owns the runtime installation profile, per-attempt workspace,
  OS lease, process containment and recovery. Neither Desktop nor a pnpm script
  launches a database daemon. All launches go through IProcessRunner.
- Node remapping must retain the engine-supplied sensitive environment and
  redaction needles; it must still reject protected Node overrides. No runtime
  credential is a recipe input or part of a deterministic plan hash.

## Credential bootstrap and privilege separation

Use independently generated random ASCII secrets for bootstrap, migration and
application roles. Minimum 256 bits of randomness; no reused or user-provided
credential. Use SCRAM-SHA-256 from initialization, never TCP trust/MD5/password.
The first proof is `initdb --pwprompt` with two password lines over redirected
stdin, EOF and CreateNoWindow. PostgreSQL's Windows prompt implementation first
tries CONIN$/CONOUT$, then falls back to stdin/stderr when no console is available.
That source observation is a hypothesis, not proof for the selected binary.
No `--pwfile=-` assumption, named password file, .pgpass, .env or console echo.
If headless prompting cannot be proven, stop this design; do not silently write
a password file or enable trust authentication.

Keep raw secrets in short-lived sensitive objects only. Managed strings cannot
promise forensic zeroization; release references promptly and exclude raw memory
dumps from DevForge diagnostics. Process environment is ephemeral, not a vault:
do not claim protection from a compromised same-user process or Administrator.

Bootstrap authority never reaches pnpm. Migration role owns only the attempt's
application database/schema, has no cluster administration or role creation.
Application role gets connect/schema usage and required table/sequence rights,
not DDL, migration-ledger ownership or role membership. Reject absent DB context;
never fall back to ambient PGHOST/PGPORT/DATABASE_URL or localhost:5432.
Disable ambient libpq settings, service files and psql startup files. Explicit
loopback endpoint, database and role are mandatory. Pass credentials through
sensitive environment or bounded stdin, never command arguments/connection URI.

Server logs go only to redirected stderr; disable collector/eventlog and statement,
parameter and duration logging. Redact before every observer. Credential-bearing
role setup must not persist cleartext SQL through WAL, log or error paths; real
sentinel scans are required. SCRAM verifiers/internal auth catalogs are confined
to the disposable DB tree, treated as sensitive, and never exported.

## Ownership and filesystem boundary

Create-if-absent attempt directory under run-owned staging, separate from payload
and Node tooling. Pin ancestry with the existing guarded filesystem abstractions.
Keep an exclusive OS lease for the complete lifecycle. Durable marker binds
schema version, run/plan/blueprint digest, attempt nonce, canonical relative data
root and directory identity, current-user SID, owner PID + creation time,
containment-job identity, runtime fingerprint and monotonic phase.
Atomic checkpoint + checksum detects partial writes/corruption; a checksum is
not authorization. Marker, journal identity, root containment and OS lease must
agree before side effects. Reject unknown fields/versions, oversized files,
reparse points, copied markers, stale generations and unexpected paths.

Bind an explicit IPv4 loopback port selected for the attempt. Port selection is
not reservation authority: if binding loses a race, fail and preserve the other
listener. An open port or pg_isready result is not readiness. Require OS-backed
owned live-process/listener identity before any credential-bearing connection;
then authenticate and verify database nonce and PostgreSQL system identifier
before migrations. Recheck process identity/liveness across the handshake.
Client-side authentication must require SCRAM, not merely rely on pg_hba:
libpq probes use `require_auth=scram-sha-256`, with explicit bounded connection
settings. A raced/replaced listener must not be able to request plaintext, MD5
or skipped authentication. Future Node client compatibility must prove equivalent
downgrade refusal and server-proof validation; do not assume pg inherits libpq
settings. Wrong-listener and in-handshake owner-death tests are mandatory.

Database/WAL/config/log/temp data never enter payload, source manifests, Git,
receipts or support bundles. SQL migrations are source and remain checksummed,
secret-scanned and publication-bound. Do not add broad .gitignore/scanner bypasses.
Use bounded tree enumeration, file counts/depth/bytes, disk-headroom preflight and
runtime monitoring. PostgreSQL max_wal_size is not a hard storage quota. If hard
disk bounds cannot be enforced on the selected non-admin host, record this as an
unmet gate, not a claim that periodic measurement prevents all disk exhaustion.

## Process lifetime and recovery

Foreground postgres must remain represented by an owned live process handle.
Do not use `pg_ctl start`: its timeout can leave a server running in background.
Do not kill by process name, PID alone, port, or unverified postmaster.pid.

Containment must exist before any PostgreSQL process can execute. Assigning an
already-running child to a job leaves a kill window. Recommended proof: a
non-inheritable KILL_ON_JOB_CLOSE lifetime job established for the engine host
before managed Process.Start; child inheritance closes that launch window while
preserving ProcessStartInfo.ArgumentList. Hold the handle for host lifetime;
do not dispose a self-containing job during normal application operation. Reject
job-assignment failure; never enable breakaway as a fallback. Test nested-job
compatibility and unrelated-process survival explicitly. Per-run isolation may
use a nested worker job only if it preserves parent-death containment.

Recovery is **rebuild disposable validation state**, not resume an old password.
After interruption, take the exclusive lease, verify durable ownership, prove
the former owner/process group has ended, and only then remove the positively
owned DB tree and initialize a fresh generation with fresh secrets. Re-run DB
validators; a previous Passed row alone cannot authorize skipping them.
No password reset, role takeover or forced shutdown of an ambiguous instance.
If proof fails, preserve data and return a recovery-required failure.

| Kill window | Required next action |
| --- | --- |
| Directory creation before marker commit | Preserve unproven directory; never infer ownership from its name. |
| Marker committed before initdb | Verify lease/ancestry; fresh initialize. |
| During initdb or immediately after it exits | Confirm contained children ended; discard only proved attempt data; fresh secret. |
| Start intent before server identity checkpoint | Lifetime containment must remove every launched child; otherwise preserve/fail closed. |
| Server ready before provisioning checkpoint | No secret recovery; clean owned instance and recreate. |
| During/after migration commit before validator evidence | Fresh DB + replay migrations/validators; never reuse a possibly committed fixture. |
| Cancellation/stop timeout | Return failure until owned-process exit is proven; nonzero pg_ctl exit is insufficient. |
| Stop complete before cleanup/receipt | Revalidate ownership, process absence and source digest; cleanup is idempotent. |
| Cleanup complete before checkpoint removal | Verify exact directory absence; finish checkpoint transition without touching a replacement. |
| PID/port reused, marker copied/tampered, owner inaccessible | No signal, connection, credential injection or deletion; preserve/fail closed. |

## Verification gates before Nest

1. Runtime installation provenance/version, complete dependency closure and
   ordinary-user execution proven. No existing service mutation.
2. Actual 18.6 headless SCRAM bootstrap with no credential files; missing/wrong
   password rejected, stdin blockage/cancel/EOF bounded, all observations scrubbed.
3. Actual disposable DB roles, authenticated instance identity, least privilege,
   migration commit/rollback/contention and explicit fresh-generation retry.
4. Actual parent kill at each launch/checkpoint window, no owned descendants or
   listener left; PID/port reuse and unrelated sentinel process survive.
5. Filesystem/junction/corrupt-checkpoint/lock/size tests; no cleanup outside the
   exact owned attempt. Raw credential sentinel absent from persistent artifacts.
6. Node remap/context contracts plus source/artifact/publication/recovery tests;
   full managed gates. No mock or skipped test substitutes for a real DB gate.

Only after all six may `backend.nest-postgres` be created as a test-only candidate.
Its own contract/checksum/lockfile/HTTP+DB/publication gates follow separately.
Windows 11, native UX/DPI, packaging and observed remote CI remain release holds.

## Primary references used for design checks

- [PostgreSQL version policy](https://www.postgresql.org/support/versioning/)
  and [Windows distribution](https://www.postgresql.org/download/windows/).
- [EDB binary distribution](https://www.enterprisedb.com/download-postgresql-binaries).
- [initdb](https://www.postgresql.org/docs/18/app-initdb.html) and
  [Windows prompt source](https://doxygen.postgresql.org/sprompt_8c_source.html).
  Doxygen is development-source evidence, not the pinned 18.6 implementation.
- [Password authentication](https://www.postgresql.org/docs/18/auth-password.html),
  [logging](https://www.postgresql.org/docs/18/runtime-config-logging.html) and
  [pg_ctl](https://www.postgresql.org/docs/18/app-pg-ctl.html).
- [Windows job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
  and [nested jobs](https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs).
- Exact [18.6 initdb source](https://github.com/postgres/postgres/blob/REL_18_6/src/bin/initdb/initdb.c),
  [Windows shell wrapper](https://github.com/postgres/postgres/blob/REL_18_6/src/port/system.c)
  and [Microsoft CRT _popen](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/popen-wpopen?view=msvc-170).
- [libpq required authentication](https://www.postgresql.org/docs/18/libpq-connect.html#LIBPQ-CONNECT-REQUIRE-AUTH).
