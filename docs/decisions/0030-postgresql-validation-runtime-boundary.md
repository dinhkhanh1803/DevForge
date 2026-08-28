# ADR-0030: Disposable PostgreSQL validation runtime before Nest

Date: 2026-08-28. Status: proposed; runtime proof failed; shell-policy decision required.

The owner requests PostgreSQL runtime, ownership/recovery and transient-credential
proof before implementing Nest. Keep ADR-0029's candidate boundary and ADR-0024's
release holds. Use a separate pre-provisioned patched Windows PostgreSQL binary
set, ordinary-user foreground execution, run-owned disposable data and SCRAM.
The existing installed 18.2 service is explicitly outside scope.

Prefer headless initdb password prompting over bounded sensitive stdin; do not
introduce password files or trust auth. Prove it on the selected 18.6 binary.
Containment must precede child execution, not a best-effort post-start assignment.
Recovery requires durable ownership plus OS evidence, then fresh DB generation
and fresh secrets; never retain a password to resume an old validation database.
Checksum alone, PID alone and port alone confer no authority.

See the [design](../superpowers/specs/2026-08-28-postgresql-validation-runtime-design.md)
for the kill-window matrix, role separation, artifact boundary and six mandatory
gates. Missing evidence remains a failed/pending gate. No Nest package or shipping
catalog expansion is authorized by an incomplete runtime proof.

The real headless initdb attempt failed with native access violation; adding
SystemRoot did not fix it. Exact 18.6 source also confirms initdb's internal CRT
shell use. Do not enable COMSPEC or infer a shell-policy exception. Keep the
strict ban until the owner explicitly resolves official CLI-internal shell use;
otherwise separately review a shell-free bootstrap strategy. The temporary
process/test experiment was removed, not shipped or declared complete.
