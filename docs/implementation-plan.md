# Milestone M4 Planner, Rules, and Blueprint Catalog Implementation Plan

**Goal:** Load guarded blueprint packages and produce deterministic immutable hashed plans without executing steps.

**Status:** Approved design and executable TDD plan; implementation starting.

**Architecture:** Infrastructure owns guarded package discovery/parsing and atomic catalog snapshots. Application owns closed rules, schema validation, previews, canonical serialization, and hashing. Domain and Blueprint abstractions own immutable dependency-free values.

**Tech stack:** .NET SDK 10.0.302, C# 14, Windows BCL APIs, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-10-m4-planner-rules-blueprint-catalog-design.md`
- Task plan: `docs/superpowers/plans/2026-08-11-m4-planner-rules-blueprint-catalog.md`
- Decision: `docs/decisions/0007-deterministic-blueprint-planning.md`

## Scope

M4 includes catalog discovery, checksum/trust/quarantine, bounded manifest/schema/rule loading, action and variable policy, exact compatibility evaluation, immutable previews, canonical serialization, and SHA-256 plan hashes.

M4 excludes execution, staging, orchestration, WPF workflows, Git/GitHub behavior, production blueprints, packaging, cloud backends, and AI APIs.

## Planned files

- Add typed plan values under Domain and normalized package models under Blueprint abstractions.
- Add catalog/planning contracts and rule/schema/hash services under Application.
- Add guarded blueprint loaders and atomic catalog snapshots under Infrastructure.
- Extend guarded workspace directory enumeration without direct Application filesystem access.
- Add Blueprint contract, Application unit, Infrastructure integration, architecture, failure, and security tests.
- Update ADR-0007, status, plan, and changelog with verified evidence.

## Tasks

- [ ] Add immutable typed plan values and complete SemVer/package contracts.
- [ ] Evolve catalog/filesystem/planner contracts.
- [ ] Parse and verify bounded blueprint packages through guarded roots.
- [ ] Publish atomic trusted/quarantined catalog snapshots.
- [ ] Implement closed compatibility rules and input/variable validation.
- [ ] Produce deterministic previews, canonical serialization, and plan hashes.
- [ ] Run the complete M4 exit gate and record exact evidence.

## Exit gate

M4 passes only when valid built-in/trusted-local fixtures load, malicious/invalid packages quarantine deterministically, non-executable trust states cannot resolve, rule/schema/tool/engine failures are covered, plan/hash snapshots are stable and mutation-sensitive, refresh is atomic, no production blueprint is added, and all locked restore/format/build/full/focused gates pass with zero skipped M4 tests.
