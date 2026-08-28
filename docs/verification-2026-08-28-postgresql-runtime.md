# PostgreSQL prerequisite verification — 2026-08-28

Worktree: `E:/MyProjects/DevForge/.worktrees/m4-m11-completion`, branch
`codex/m4-m11-completion`, base `cd44154`. Host remains Windows 10 Pro 22H2,
not Windows 11. No commit/push, remote action, service change or catalog promotion.

## Verdict

**Runtime prerequisite NOT passed; Nest NOT started.** Design, host inspection,
binary provisioning and an unsuccessful real bootstrap experiment are recorded.
The temporary product/test changes were removed after discovering the shell
policy boundary. Existing Node/.NET/Python changes were preserved.

The policy question is independent of the unresolved native crash: official
Windows initdb invokes a command processor internally. The owner currently bans
cmd /c without an explicit exception for trusted CLI internals. No COMSPEC,
shell shim, trust-auth, password file, Docker or existing DB workaround was used.

## Provisioning and host evidence

- PATH lookup found no postgres/initdb/pg_ctl/psql/docker.
- Read-only service inspection initially hit sandbox access denial. Approved
  outside-sandbox retry returned `postgresql-x64-18`, `Running`.
- File metadata at `C:/Program Files/PostgreSQL/18/bin` reported 18.2 for
  postgres/initdb/psql; Authenticode `NotSigned`. No connection, service stop,
  upgrade or read of that installation's database files was performed.
- The official [EDB binary page](https://www.enterprisedb.com/download-postgresql-binaries)
  identified Windows x64 18.6 link `https://sbp.enterprisedb.com/getfile.jsp?fileid=1260435`.
  HEAD returned HTTP 200, application/zip, 343,808,005 bytes and resolved to
  `https://get.enterprisedb.com/postgresql/postgresql-18.6-1-windows-x64-binaries.zip`.
- Approved download went to worktree `.tools/postgresql-18.6-1-windows-x64-binaries.zip`.
  SHA-256: `FBE23DA234EE31547BF8A36D29DFD81E82B849DF2D2B78D2EECB43D360252F8C`.
  This measured digest is NOT an independently published checksum/signature.
- Checked archive entry paths for rooted/traversal/drive paths and bounded
  count/expanded size before extraction into a new, absent directory
  `.tools/postgresql-18.6-1`. Archive: 21,968 entries, 913,597,411 expanded bytes.
  No existing directory was overwritten and no installer was run.
- Extracted PE versions: 18.6; Authenticode `NotSigned`. Measured executable hashes:

| Binary | SHA-256 |
| --- | --- |
| initdb.exe | 68195F0C6F22694660BA86D914AE8C74BCD38E71EB342F98E065B1962311142E |
| postgres.exe | AF5B897CB69C9CE692A4A15ECD022B540DB85DB1ADD0F66D2B9F0697BE2451A0 |
| psql.exe | 1E23B7F9AC7649B4717ADA9B1F4E1B5B66C343937B6A91583F63528B96A61503 |

Full DLL/share closure approval and ordinary-user runtime certification are still
open. Portable files are ignored development tools, not shipped product assets.

## Temporary proof and exact results

The experiment used a closed initdb identity and bounded internal stdin on the
existing IProcessRunner. Arguments were separate elements:

```text
--pgdata=data
--username=df_bootstrap
--auth=scram-sha-256
--pwprompt
--encoding=UTF8
--locale=C
--locale-provider=libc
--no-instructions
```

CreateNoWindow, no shell outer launch, redirected stdin/stdout/stderr, cleared
environment, 60-second timeout; two identical random 256-bit hexadecimal password
lines followed by EOF. Both lines were redaction needles before start. Each real
attempt used atomic create-if-absent in a unique `.tools/pg-bootstrap-proof-*`
workspace via the guarded filesystem. No postgres server was explicitly started,
no TCP connection or psql command was attempted.

Command shorthand `dotnet` below means
`E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe`, SDK 10.0.302.

```text
dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~PostgreSqlBootstrapTests
```

| Experiment | Actual result |
| --- | --- |
| Identity test before new identity | Exit 1; 0/1 passed, expected missing-identity assertion. |
| Identity after temporary addition | Exit 0; 1/1 passed. |
| First input-test compilation | Exit 1; CA1861 in test expected-array allocation; corrected without disabling analyzer. |
| Input contract before runner input support | Exit 1; 1 passed, 1 failed: helper received EOF but no password lines. |
| Input runner support | Exit 0; 2/2 passed; helper output redacted twice then EOF. |
| Actual 18.6 initdb inside sandbox | Exit 1; 2 passed, 1 failed; restricted-token errors 87 and 3. |
| Approved outside-sandbox retry with `--no-build --no-restore` | Exit 1; 2 passed, 1 failed. |
| Retry with safe exit-code diagnostics | Exit 1; 2 passed, 1 failed; native exit -1073741819 / 0xC0000005, no retained error text. |
| One-variable hypothesis: trusted SystemRoot injected | Exit 1; 2 passed, 1 failed; same native exit. Hypothesis did not resolve crash. |

The scan assertions after successful initdb **were never reached**. This is not
a password-leak scan pass or SCRAM proof. Subsequent guarded-scope inspection of
the four attempt directories found zero files in each:

```text
pg-bootstrap-proof-10a8835f13d5489a85fe4d3c6cec41d9
pg-bootstrap-proof-1ee13c8c29b54b2eb42470ca5b5287df
pg-bootstrap-proof-25e113ba5e404ed9bfc6dd5b18844feb
pg-bootstrap-proof-b43cd0874b0545ba9e3f2fa14808f79c
```

No credential file was intentionally written; no host-wide forensic claim about
OS-managed crash artifacts is made. No crash dump was collected or exported.

## Why the experiment stopped

Read exact tag [initdb.c](https://github.com/postgres/postgres/blob/REL_18_6/src/bin/initdb/initdb.c),
[common/exec.c](https://github.com/postgres/postgres/blob/REL_18_6/src/common/exec.c)
and [port/system.c](https://github.com/postgres/postgres/blob/REL_18_6/src/port/system.c).
The Windows wrapper calls `_popen` with constructed command text.
[Microsoft's CRT reference](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/popen-wpopen?view=msvc-170)
states that it starts the command processor. This is source-backed shell-boundary
evidence, not proof that COMSPEC caused the access violation or that enabling it
would repair credential/containment behavior.

Removed only the experiment: initdb identity/input contract, runner stdin branch,
new bootstrap factory/test class and echo-stdin helper mode. No pre-existing test
was removed. No PostgreSQL execution permission survives in the product. There
are no retained new runtime tests and no claim that Task 1 or the runtime passed.

## Baseline verification after removing the experiment

Independent design review confirmed the policy hold and found two ordering gaps.
The corrected plan now requires durable marker/lease and pre-launch containment
before any resumed real initdb invocation, not only before server start. The
discarded experiment did not prove those requirements and is not an approved
runtime implementation. The design also now requires OS-backed listener/process
checks before credentials, client-side SCRAM enforcement and downgrade/reuse
tests; PostgreSQL's default client authentication policy is not sufficient.

Final read-only process inspection found no initdb process. All ten observed
postgres processes had creation times on 2026-08-21, predating this proof; their
image paths were inaccessible to the current non-elevated context. Existing
`postgresql-x64-18` remained Running. No process was stopped or adopted.

Restore: `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1`
returned exit 0, all projects up-to-date.

Formatting scope is exactly the three files below. Initial format hit sandbox
MSBuild named-pipe access denial (exit 1); approved outside-sandbox retry and
verify-no-changes both returned exit 0:

```text
dotnet format DevForge.sln --no-restore --include src/DevForge.Application/Contracts/ProcessContracts.cs src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs tests/DevForge.ProcessTestHelper/Program.cs
dotnet format DevForge.sln --verify-no-changes --no-restore --include src/DevForge.Application/Contracts/ProcessContracts.cs src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs tests/DevForge.ProcessTestHelper/Program.cs
```

| Command | Actual result |
| --- | --- |
| `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false` | Exit 0; 0 warnings, 0 errors; 22.84 seconds. |
| `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore` | Exit 0; 651 passed, 0 failed/skipped; 964 ms. |
| `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore` | Exit 0; 724 passed, 0 failed/skipped; 36 seconds. |
| `dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj -c Release --no-build --no-restore` | Exit 0; 152 passed, 0 failed/skipped; 3 seconds. |
| `git diff --check` | Exit 0; checkout line-ending advisories only. |

Baseline total: **1,527/1,527**, no failures or skips. Full E2E was not rerun for
the retained documentation-only result. The experiment's failed acceptance must
not be included in or masked by this regression total. No new permanent tests
or source changes survive this turn. Diff inspection confirms the existing
Node/runtime/helper changes remain; ProcessContracts has no content diff.

The prior 1,762-test Node/Next acceptance remains historical, not PostgreSQL
evidence. Current runtime/auth/ownership/recovery and all external release gates
remain open regardless of baseline test results.
