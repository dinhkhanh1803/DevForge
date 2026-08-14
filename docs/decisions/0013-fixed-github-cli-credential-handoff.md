# ADR-0013: Fixed GitHub CLI credential handoff for isolated Git pushes

**Status:** Accepted

**Date:** 2026-08-14

## Context

M8 delegates GitHub authentication to `gh`, but Task 3 deliberately clears ambient Git credential helpers, system/global configuration, prompts, and inherited environment. An ordinary HTTPS `git push` therefore cannot use the verified `gh` session unless the push command supplies one explicit credential helper. Reading `gh auth token`, copying credentials into arguments/environment, enabling Git Credential Manager, or inheriting user Git configuration would violate the privacy and exact-account boundaries.

## Decision

- Every top-level `gh` and `git` invocation remains a closed `CommandSpec` executed by `IProcessRunner` with separated arguments and an empty/minimal environment.
- Immediately before each push, DevForge resolves the trusted absolute `gh.exe` through the same executable resolver used by `IProcessRunner`. The path is treated as a sensitive process value and registered as a redaction needle.
- The isolated Git command resets all ambient helpers, then supplies exactly one host-scoped helper for `https://github.com`: the resolved `gh.exe auth git-credential` protocol. Git necessarily starts that fixed helper while servicing its credential protocol; no `cmd`, PowerShell, user-provided shell text, PATH lookup, token command, token value, or arbitrary helper is accepted.
- The helper is command-scoped only. DevForge does not call `gh auth setup-git`, mutate global/local credential configuration, read authentication files, request `gh auth token`, or retain authentication output.
- Before each push, the local Git service revalidates the final-tree digest, fresh secret scan, exact single parentless commit, exact branch policy, clean tree, exact origin fetch/push URL, and closed local configuration. The push source is the persisted commit object ID, never a mutable branch name.

## Consequences

Private-by-default publication can use the exact account already verified by `gh` while Git configuration remains isolated. The only indirect child process is the fixed, trusted Git credential-protocol helper required by Git itself; all mutation intent and arguments remain constructed inside the `IProcessRunner` boundary. The helper path may appear in the in-memory command specification but is always redacted and is never persisted.

## Rejected alternatives

- Calling or parsing `gh auth token`.
- Passing a token through arguments, environment variables, standard input, logs, checkpoints, or receipts.
- Running `gh auth setup-git` and mutating the user's global configuration.
- Re-enabling ambient Git Credential Manager, askpass, helpers, hooks, filters, or PATH-based helper discovery.
- Using a user-supplied helper or arbitrary shell command.
- Treating a fake-runner push as proof that private publication is authenticated.
