# Milestone M1 Domain & Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the immutable domain vocabulary, blueprint manifest contracts, and secure application ports required by later DevForge milestones.

**Architecture:** `DevForge.Domain` and `DevForge.Blueprints.Abstractions` remain dependency-free and own their respective immutable models and validation results. `DevForge.Application` combines those concepts through interfaces only; M1 adds no infrastructure implementation, planner algorithm, persistence, process execution, or UI workflow.

**Tech Stack:** .NET SDK 10.0.302, C# 14, `System.Collections.Immutable`, xUnit, WPF solution baseline from M0.

---

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-07-31-m1-domain-contracts-design.md`
- Task-level plan: `docs/superpowers/plans/2026-07-31-m1-domain-contracts.md`
- Decision: `docs/decisions/0003-validated-immutable-domain-and-contract-boundaries.md`

## Scope

M1 includes immutable domain and blueprint models, all twelve core application interfaces, security-shaped process/file contracts, and focused tests.

M1 excludes planner/rule/hash behavior, implementations of any application port, persistence, EF Core/SQLite, blueprint catalog content, Git/GitHub automation, and UI expansion.

## Expected files

- Create `src/DevForge.Domain/{Validation,Projects,Execution,Runs,Diagnostics,Environment,Reports}/*`.
- Create `src/DevForge.Blueprints.Abstractions/{Models,Validation}/*`.
- Create `src/DevForge.Application/Contracts/*`.
- Create `tests/DevForge.UnitTests/{Domain,Application}/*`.
- Create `tests/DevForge.BlueprintTests/Contracts/*`.
- Modify milestone documentation and changelog after verification.

## Tasks

### Task 1: Domain models

- [ ] Write and run failing tests for validation aggregation, immutable snapshots, retry invariants, run status/transitions, and redacted errors.
- [ ] Implement the minimum domain types needed to pass.
- [ ] Run focused and complete unit suites and refactor only while green.

### Task 2: Blueprint manifest contracts

- [ ] Write and run failing tests for semantic versions, identifier uniqueness, positive timeouts, trust, and immutable snapshots.
- [ ] Implement dependency-free blueprint models and validation.
- [ ] Run blueprint and architecture tests and refactor only while green.

### Task 3: Application ports

- [ ] Write and run failing tests for all twelve ports.
- [ ] Prove separated executable/arguments, immutable redaction values, root-scoped file operations, and validated relative paths.
- [ ] Implement interfaces and request/result records only.
- [ ] Run focused and full tests and refactor only while green.

### Task 4: Exit gate

- [ ] Run format, locked restore, Release build, full tests, and focused M1 tests.
- [ ] Require zero warnings/errors and no skipped M1 tests.
- [ ] Update `docs/implementation-status.md`, `CHANGELOG.md`, and this checklist with exact evidence.

## Exit gate

M1 passes only when:

1. Required models and all twelve ports exist.
2. Invalid drafts cannot produce valid aggregates, and caller mutation cannot change constructed objects.
3. The process contract separates executable and arguments.
4. File operations require a root-scoped abstraction and validated relative paths.
5. Domain and Blueprint Abstractions remain dependency-free; Application does not reference Infrastructure, Desktop, or CLI.
6. Locked restore, format, Release build, full tests, and focused M1 tests pass with zero warnings and zero errors.