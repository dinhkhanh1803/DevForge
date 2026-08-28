# PostgreSQL runtime proof implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline, honoring the
> owner's earlier execution preference. Use test-driven-development and
> verification-before-completion. No commit/push without a current request.

**Goal:** prove safe PostgreSQL bootstrap and lifecycle before authorizing Nest.

**Architecture:** closed Infrastructure PostgreSQL commands use IProcessRunner;
guarded workspaces hold disposable data and non-secret ownership evidence.
Credentials remain ephemeral. No UI, existing-service or catalog changes.

**Tech stack:** existing C#/.NET 10/xUnit; separately provisioned PostgreSQL 18.6-1
Windows x64. No new NuGet package is required for the first process-input proof.

**Execution checkpoint:** Task 1 was attempted and failed its real-runtime exit
gate. The temporary process/input/test changes were removed after discovering
initdb's internal shell requirement. This plan is held pending the ADR-0030
policy decision; unchecked items are unfinished, not skipped acceptance.

## Task 1: bounded sensitive stdin and real initdb proof

**Corrected ordering after review:** helper-only stdin tests may run first, but
no real initdb invocation may resume until the shell-policy decision, verified
installation closure, durable attempt marker/exclusive lease and pre-launch
lifetime containment are implemented and proven. initdb itself spawns children;
being a bootstrap command does not exempt it from ownership or crash containment.
The original temporary experiment did not prove these prerequisites and is not
an accepted implementation example.

Files: Application `Contracts/ProcessContracts.cs`; Infrastructure
`Processes/WindowsProcessRunner.cs`, new `Processes/PostgreSqlBootstrapCommands.cs`;
Integration `Infrastructure/Processes/PostgreSqlBootstrapTests.cs`;
ProcessTestHelper `Program.cs`.

- [ ] Add a failing executable-identity contract for engine PostgreSQL operations:

  ```csharp
  Assert.True(ExecutableIdentity.Create("initdb").IsValid);
  ```

- [ ] Run the exact targeted test before implementation:

  ```text
  E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~PostgreSqlBootstrapTests
  ```

- [ ] Add engine-only bounded stdin and automatic input redaction, concurrent
  async input/output, EOF and timeout/cancel handling. Test echo suppression,
  blocked input, early child exit, empty/no-input behavior and cancelled launch.
- [ ] Add closed initialization arguments with fresh SCRAM password, guarded
  empty data directory and explicit approved binary resolution, only after the
  prerequisite containment/ownership tests above pass. Do not add any
  blueprint handler permission. Run actual initdb with no password file and scan
  persisted fixture bytes for the generated password. Missing binary is failure.

Exit: targeted process tests pass and real 18.6 initialization evidence exists.
This does not by itself pass the runtime gate or authorize Nest.

## Remaining mandatory proof batches

These are acceptance checkpoints, not claims of implemented services. Each needs
its own test-first implementation slice before its checkbox can be completed.

- [ ] Installation closure + foreground server + authenticated instance identity;
  verify owned listener/process before credentials, enforce client-side SCRAM
  without downgrade, then verify database identity. Prove wrong-listener and
  in-handshake owner-death refusal. Bootstrap/migration/application role separation
  and bounded SQL operations follow only on the authenticated owned endpoint.
- [ ] Pre-launch lifetime containment with actual parent kill; no orphan window,
  no breakaway fallback, unrelated processes survive. Extend process helper only
  for fixed test scenarios, never arbitrary shell strings.
- [ ] Durable marker/checksum + exclusive lease + guarded cleanup; replay each
  kill-window row in the design, including PID reuse, copied/corrupt markers,
  absent directory and source/target preservation. Fresh secrets after restart.
- [ ] Node context/remap and artifact boundary; all source remains scanned/hashed,
  DB internals never enter payload/receipts/Git/support bundles.
- [ ] Real database acceptance and full restore/format/build/test; review exact
  results against all six design gates before creating any Nest package.

Keep implementation-plan/status/ADR and a verification record synchronized with
actual results. Do not report a mock-only proof, old full-suite result, or a
downloaded binary as runtime acceptance. Windows 11 release evidence stays open.
