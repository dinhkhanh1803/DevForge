# M11 next-candidate review: backend.nest-postgres

Date: 2026-08-28. Scope: review and documentation only.
Worktree: `codex/m4-m11-completion`, HEAD `cd44154`, with existing uncommitted M11 work.

## Verdict

Select `backend.nest-postgres` for the next independent candidate. **Conditional
go for a PostgreSQL validation-runtime design/proof; no-go for claiming a working
Nest/PostgreSQL candidate or promoting any catalog package yet.** No candidate
directory, executable identity, handler, dependency or shipping declaration is
added by this review.

The detailed baseline DOCX sections 4.2, 17.2 and 18 list Nest/PostgreSQL separately
from Next and the Next/Nest monorepo. Section 17.2 lists eight V1 candidates; the
current three are WinForms, Python Desktop and Next. Five remain, including Nest.
The list establishes membership, not an explicit implementation dependency order;
Nest is the recommended next slice because Node/pnpm is now locally accepted and
its database boundary should be proven before the separate monorepo.

ADR-0024 permits isolated development with recorded release debt, not release.
BuiltIn must remain the three MVP packages. DevForge itself remains native WPF,
MVVM/Clean Architecture on .NET 10; PostgreSQL is for generated-project validation,
not a replacement for DevForge's SQLite or a new DevForge cloud backend.

## Findings that must precede candidate implementation

These are integration gaps for the proposed candidate, not defects demonstrated
in the accepted Next/React workflows.

| Priority | Evidence in the current tree | Required resolution |
| --- | --- | --- |
| P1 | `ProcessContracts.cs:7` has no PostgreSQL/Docker identity. `WindowsProcessRunner.cs:63` awaits command exit and exposes no durable service lease. | Design typed, bounded PostgreSQL start/readiness/stop/cleanup and cross-process recovery through IProcessRunner; do not hide a daemon launch inside a pnpm script or add a raw-shell escape. |
| P1 | `ProcessExecutionHandlers.cs:433` provides no pnpm runtime environment; `InWorkspace` at line 456 reconstructs the command with empty environment and redaction lists. Sensitive value types already exist in `ProcessContracts.cs:83,126`. | A future engine-owned, in-memory DB context must survive workspace remapping together with its redaction data. Preserve protected Node settings; do not accept arbitrary blueprint-provided environment or persist credentials in plans/checkpoints. |
| P1 | No real PostgreSQL acceptance fixture exists. PATH discovery found none of `postgres`, `initdb`, `pg_ctl`, `psql`, `docker`. This does not prove they are uninstalled or that no service exists. | Establish a reviewed non-admin runtime and real disposable DB evidence before declaring database support. Missing DB means failed preflight/acceptance, not skipped tests or a mock-only pass. |
| P2 | `NodeExecutionWorkspace.cs:18` recognizes only node_modules/.devforge-node/.next/dist; it refuses source-side env/config injection. Only exact React 1.0.0 exports dist. | Keep Nest output/cache in existing tooling roots; transfer source only. A database data/WAL/log tree needs its own guarded owned boundary, limits and cleanup proof, not a new ignored directory under project source. |

The `pg_ctl` documentation explicitly notes that a timed-out command can leave
the operation running in the background. Therefore a nonzero command exit is not
proof of server shutdown. Its PID file alone is also insufficient ownership proof
for destructive cleanup or process termination. See the
[PostgreSQL service-control reference](https://www.postgresql.org/docs/18/app-pg-ctl.html).

## Candidate architecture recommendation

Prefer a single Nest application with `@nestjs/typeorm`, TypeORM and `pg`, using
explicit migrations. Nest documents a dedicated TypeORM integration; its warning
against production `synchronize: true` directly supports disabling schema sync.
[Nest database integration](https://docs.nestjs.com/techniques/database).

Alternatives considered:

- Direct `pg` plus explicit SQL: fewer ORM dependencies and transparent queries,
  but adds a custom migration ledger/locking/retry implementation. It does not
  remove the service-lifecycle and credential blockers. Keep as a fallback if
  compatibility evidence rejects the recommended integration.
- Next/Nest monorepo now: rejected for this slice. It is a separate baseline ID
  and conflicts with the current root-only pnpm/ancestor-workspace isolation.

Proposed bounded application scope:

- Conventional Nest modules/controllers/services, strict TypeScript and DI.
- Validated runtime configuration, bounded DB pool/connect/query timeouts, generic
  error responses, and shutdown that closes HTTP and DB resources.
- Separate liveness and database readiness; one small notes create/list module
  to prove persistence, validation and parameterized database access.
- Versioned migrations with `synchronize`, `dropSchema` and startup auto-migration
  disabled. Migration execution is explicit and can target only the owned test
  fixture during DevForge acceptance; generated handoff explains operator use.
- Loopback-only local server by default. No auth, payment, Redis, queues, uploads,
  cloud provisioning, public exposure, Docker automation or DevForge UI changes.
- Exact dependency pins and frozen lockfile after a separate compatibility check.
  No Nest/ORM/PostgreSQL version is certified by this review. Existing reviewed
  Node 22.23.2/pnpm 10.24.0 are the proposed starting toolchain, not a compatibility
  claim for packages that have not been installed.
- Seven section-17.5 handoff documents, source manifest/rules/schema/checksums,
  and test-only content links with `CopyToPublishDirectory=Never`.

## PostgreSQL validation-runtime prerequisite

First investigate a trusted, pre-provisioned Windows PostgreSQL binary set running
as the ordinary user in an engine-owned disposable cluster. Do not install a
Windows service, alter firewall/registry, use an existing user's database, or
silently fall back to Docker/cloud infrastructure. Docker is not the default:
it introduces daemon/container ownership and credential-storage policy in addition
to the missing executable boundary.

The prerequisite design/proof must resolve all of the following, before coding
the candidate package:

1. Explicit trusted executable/version provenance, typed closed operations, and
   no implicit executable download or Administrator happy path.
2. A lease bound to the run, guarded data root, verified process identity, cluster
   identity and loopback endpoint. Port/PID/name reuse must not authorize access,
   shutdown or deletion. Readiness requires the expected DB identity, not merely
   an open TCP port. Keep DB lifecycle separate from immutable project source.
3. Authenticated bootstrap and credential delivery without writing raw passwords,
   connection strings, `.env` or reusable credential files. Do not use global or
   local TCP `trust` as an automatic workaround. `initdb --pwfile` reads a file;
   it is not evidence of an in-memory bootstrap path. The current runner also has
   no standard-input contract. Whether an OS-authenticated or bounded transient
   input design is viable on this host needs a dedicated proof, not an assumption.
   [PostgreSQL initdb reference](https://www.postgresql.org/docs/18/app-initdb.html).
4. Least-privilege application access and separately scoped migration authority.
   No credentials in recipe, preview, plan hash, SQLite, receipts, log, crash
   diagnostics, support bundle, process arguments or final project. A raw password
   must not be relabeled a safe environment value. PostgreSQL internal auth/data
   artifacts must also remain confined to the disposable DB boundary.
5. Safe propagation through the Node snapshot remap. Nest's config module can
   disable env-file loading with `ignoreEnvFile`; programmatic driver settings
   can avoid accidental libpq-style defaults. Validation must fail if its typed
   runtime context is absent, never connect to ambient localhost:5432.
   [Nest configuration](https://docs.nestjs.com/techniques/configuration),
   [node-postgres connection settings](https://node-postgres.com/features/connecting).
6. Bounded data/WAL/log sizes, secret-safe diagnostics and no cluster data/WAL/
   runtime logs in source, package checksums, final-tree evidence or publication.
   SQL migration source remains checksummed, scanned and publication-bound.
   No broad ignore rule
   that silently exempts new source files from hashing/scanning.
7. Crash recovery before/after start, readiness, migration commit, stop and lease
   removal. Secret loss after an app kill cannot cause credential persistence as
   a recovery shortcut. If ownership cannot be proven, fail closed and preserve
   data; clean up only positively identified engine-owned resources.

The current runtime gap is real work for the next slice, not a request to waive
security or to ask the owner for a production database password.

## Acceptance required after the prerequisite passes

- Contract: exact pins/lock/checksum inventory, tamper quarantine for manifest,
  source, migrations, lock and inventory; deterministic plans and source tree;
  candidate absent from BuiltIn and release package.
- Unit: validated config and redacted failures; service/repository behavior;
  parameterized queries, invalid inputs and independent health/readiness states.
- Integration: real PostgreSQL fresh migration, idempotent rerun, failed migration
  rollback, migration-lock contention, least-privilege refusal, actual writes and
  subsequent reads. Repeat-validator execution must not duplicate application
  data or reuse an unowned database. Mocks are not DB acceptance.
- Runtime/security: process/environment and redaction preservation through remap;
  absent/wrong DB identity; auth/connect/query timeouts; port conflict; server
  dying mid-step; cancellation; bounded shutdown; app kill and PID/port reuse;
  junction/data-root escape and secret-leak sentinels. No kill-by-port/name.
- Candidate E2E: frozen install, independent format/lint/typecheck/test/build and
  bounded real HTTP+DB smoke; source-only finalization, owned cleanup, local Git
  publication, repeated verification and source/migration tamper recovery.
- Full managed restore/format/build/test and reviewed snapshots, followed by the
  original Windows 11, native UX/DPI, packaging and observed remote CI gates.
  Development acceptance never changes a Pending release row by itself.

## Future file boundary, not edits authorized by this review

The runtime prerequisite would own narrowly scoped Application contracts,
Infrastructure process/execution/recovery implementations and their tests. Reuse
`SensitiveProcessValue`/`IProcessRunner`/guarded workspaces; do not bypass them.
Only after that gate passes should a separate implementation add
`blueprints/v1-candidates/backend.nest-postgres/**`, its contract/E2E tests and
explicit test-only project links. BuiltIn declarations, release audit and shipped
MVP packages are not part of either candidate edit scope.

## Evidence and review exit

Read-only inspection covered the detailed DOCX sections above, Markdown baseline,
ADR-0024/0028, current plan/status/release checklist, process contracts/runner,
Node workspace/environment, validator vocabulary and catalog/test content links.
No AGENTS.md was found in the inspected repository. No DB command was executed.

Fresh command:

```text
E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DefaultCatalogDoesNotContainAnyV1Candidate|FullyQualifiedName~NextCandidateContractTests"
```

Result: exit 0, 8/8 passed, 0 failed/skipped, 413 ms. This checks the existing
catalog/runtime contracts, not Nest acceptance. No new tests or product code were
added. The previous full 1,762/1,762 result remains historical evidence from the
Node/Next implementation turn; restore/format/build/full E2E were not rerun for
this documentation-only review. `git diff --check` passed after the review docs,
with checkout line-ending advisories only. A placeholder scan found no unfinished
markers; no new release evidence is asserted by the proposed architecture.

Review exit: candidate identified; alternatives and concrete blockers recorded;
safe prerequisite and later acceptance defined; release holds and catalog intact.
There is no implementation authorization, new package pin or release certification
implicit in this document.
