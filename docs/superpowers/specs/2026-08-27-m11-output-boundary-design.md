# M11 source/output boundary and production .NET design

The owner explicitly requests repairing both known acceptance blockers before
catalog expansion. ADR-0025 is the selected design; prior standing approval of
recommended defaults and inline execution applies. DevForge remains native WPF.

The evidence writer derives output membership from persisted preview artifacts
and validators. The full finalized tree, including outputs and membership, remains
the publication authority. Git's committed tree is the exact complementary source
set, including all engine evidence. Scan all files, not just source. Canonical
bounded marker parsing and exact existing-byte adoption prevent ambiguous recovery.
Legacy trees without a marker are unchanged. No file is removed.

The process runner injects a declared .NET-only runtime environment after trusted
resolution. SDK paths come from that resolution, not caller PATH. Protected keys
reject override. The real acceptance observer only records process results; it
must not create a replacement CommandSpec or supply environment values.

Scope: optional evidence manifest, reserved-path policy, complete-tree/source-set
projection, .NET launch policy, tests and docs. No database migration, Git command
change, remote operation, release promotion, or additional blueprint.

Exit: targeted regression tests and complete restore/format/build/four-suite gate
pass; native WinForms opens, Refresh changes text, closes cleanly; local Git and
durable retry reach Completed without regeneration. Windows 11 remains external.
