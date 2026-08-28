# ADR-0029: Review Nest/PostgreSQL after the Node foundation

Date: 2026-08-28
Status: Review recommendation recorded; implementation and release not approved by this document.

## Context

The owner requests a separate review of the next M11 candidate, explicitly without
shipping-catalog expansion. Baseline DOCX 17.2 lists `backend.nest-postgres` and
the fullstack monorepo as separate candidates. Three other V1 candidates already
have local development evidence. ADR-0024 retains all external release holds.

## Recommendation

Select the standalone Nest/PostgreSQL candidate next. Prefer Nest's dedicated
TypeORM integration with `pg`, explicit migrations and disabled automatic schema
synchronization. Direct `pg`/SQL remains an alternative if compatibility fails;
do not introduce the monorepo or a custom migration framework in this review.

Before candidate implementation, separately design and prove an engine-owned,
authenticated PostgreSQL validation runtime, typed lifecycle/lease recovery,
guarded data boundary and transient credential delivery. The present pnpm command
builder/remap carries no DB environment, and the tool vocabulary/lifecycle lacks
PostgreSQL service support. Reuse existing sensitive-value abstractions rather
than persisting secrets or inheriting ambient configuration.

A pre-provisioned non-admin Windows binary set is the first runtime direction to
evaluate, not a certified solution. Authentication without raw secret files and
safe recovery after secret loss must be proven. No implicit Docker/service install,
trust-auth shortcut, existing DB access, raw shell, download or remote mutation.
Exact new dependency versions are deliberately not pinned until compatibility is
actually checked. No candidate package is created in this review.

## Gates and consequences

The [review](../reviews/2026-08-28-m11-nest-postgres-review.md) contains the concrete
code evidence, alternatives, architecture boundary, required tests and primary
references. Runtime prerequisite first, one test-only candidate second, independent
acceptance third. BuiltIn stays at three MVP roots. Windows 11, native UX/DPI,
packaging and observed CI release evidence remain unchanged and mandatory.

This selects review direction only; it completes no Nest feature or release gate.
