# Privacy and Support Bundles

## Privacy model

DevForge is local-first. It has no AI integration, cloud backend, embedded browser, outbound telemetry, or token collection. SQLite stores settings, tool metadata, blueprint metadata, presets, recent projects, and durable run checkpoints; it must not store customer source, passwords, private keys, connection strings, `.env` content, GitHub tokens, `gh auth token` output, or unredacted credential-bearing logs.

## Local diagnostics

Diagnostics are bounded UTF-8 JSONL under guarded application storage. Events contain UTC time, closed level/event identifiers, optional run/step/attempt/duration/exit metadata, a redacted message, and bounded structured values. Serialization revalidates secret-shaped values. Daily/run logs use exact ownership markers and a bounded cross-process lease. Retention defaults to 30 days and 256 MiB and can delete only matching marker-owned logs; unrelated or finalized customer data is never a candidate.

## Support bundle contents

A Support bundle is created only for an authoritative persisted run. Its closed inventory may contain blueprint identity/manifest checksum, bounded catalog errors, optional tool availability without raw environment properties, plan/recipe summaries, checkpoint, engine reports/evidence, marker-verified run logs, and an integrity inventory. Entries are strict UTF-8, normalized, individually capped at 4 MiB, aggregate-capped at 16 MiB, secret-scanned, sorted, and written with deterministic ZIP metadata.

The archive excludes customer source, arbitrary paths, `.env`, databases, credentials, connection strings, private keys, duplicate/archive-slip names, and unverified logs. Creation is atomic and recoverable across process interruption. The bundle ID derives from its archive SHA-256.

## Export and cleanup

Use Execution Center or Run History **Support bundle**. **Copy receipt** copies the owned relative path, full SHA-256, and length—not an arbitrary absolute path. Cleanup authority is the typed receipt plus matching ownership marker and complete archive digest. Safe mode permits verified evidence export but refuses cleanup mutation. A canonical-looking unowned archive is preserved.

If export reports `DF-SUPPORT-001`, refresh the authoritative run and retry. Never rename a bundle or marker to manufacture ownership. Before sharing, compare the copied receipt to the file through an approved support process and transmit it only to the intended recipient.
