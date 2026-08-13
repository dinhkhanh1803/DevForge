# M7 Dynamic Create Project, Plan Preview, Execution Center, and Completed Design

**Status:** Approved by standing user direction

**Date:** 2026-08-13

## Objective

M7 exposes the verified M4 planner and M5 recoverable execution engine through the M6 native WPF shell. A user can select an executable local blueprint, complete a schema-driven recipe, review the exact immutable plan, generate a project without a terminal, observe or cancel execution, resume eligible runs, and inspect evidence on the final local-ready screen.

M7 does not implement Git or GitHub side effects, add production blueprints, expand the V1 catalog, package the product, or weaken any guarded file/process boundary.

## Fixed product choices

- The Create Project experience is one recoverable workspace with four explicit stages: Configure, Review Plan, Execute, and Local Ready.
- The persistent M6 navigation rail remains visible. Create Project, Blueprint Catalog, and Run History become functional; the selected run remains available while navigating between M7 pages.
- Planning occurs only after explicit `Review plan`; it does not run on every keystroke. Any edit after planning invalidates the reviewed plan and requires replanning.
- The primary action is `Create & Validate` in M7. The specification's `Create, Validate & Publish` label is activated only when M8 provides Git/GitHub behavior.
- M5's exact `RunStatus` remains authoritative. The final view is named Completed in the UX flow but presents `LocalReady` as `LOCAL PROJECT READY`; it never mutates or displays `Completed` without M8 completion evidence.
- Blueprint Catalog lists M4 executable and inspect-only/quarantined entries. M7 adds no production package. E2E uses a bounded test fixture package; M9 supplies the three MVP production blueprints.
- Git intent is visible as unavailable until M8 and remains disabled in M7 recipes. IDE selection and safe post-success launch use the existing typed `IIdeLauncher` boundary.

## Considered approaches

### Recommended: staged workspace with immutable handoff

Configure and schema inputs share one page, Review Plan is a distinct immutable checkpoint, and Execution Center owns progress/recovery. This makes plan invalidation explicit, supports keyboard use, and maps directly to M4/M5 contracts.

### Rejected: always-live split-pane planning

It gives rapid feedback but would repeatedly run catalog resolution and environment compatibility while the user types, complicate cancellation, and blur which plan was accepted.

### Rejected: modal multi-window wizard

It hides state, makes resume/history navigation awkward, duplicates the persistent shell, and is harder to make accessible and test at 100/125/150% scaling.

## Architecture

M7 adds an Application-owned creation workflow facade so Desktop never constructs execution workspaces or calls Infrastructure directly. Its responsibilities are validation, plan identity, explicit plan invalidation, run creation, and orchestration handoff. Concrete path opening, target inspection, workspace creation, and run-artifact opening remain Infrastructure adapters behind Application contracts.

The main units are:

- `ProjectCreationDraft`: bounded user input for identity, absolute target, blueprint reference, typed dynamic values, enabled features, team profile reference, and completion intent. It cannot carry credentials.
- `DynamicInputValue`: one discriminated value for Text, Choice, Boolean, or WholeNumber. Conversion to recipe strings is invariant and schema-owned.
- `IProjectCreationWorkflow`: load catalog, validate/plan a draft, create a fresh run, prepare guarded execution locations, execute the reviewed plan, resume an eligible checkpoint, and save/load scrubbed presets.
- `IProjectExecutionWorkspaceFactory`: Infrastructure port that validates a canonical local target, proves the destination is absent or empty before mutation, opens the guarded target-parent workspace, derives the target directory, and opens a run-specific artifact workspace under local application data.
- `IRunIdentityGenerator`: Infrastructure port producing bounded unique run/recipe identifiers without placing randomness in Desktop or Application.
- Desktop feature services and ViewModels: schema form projection, plan preview projection, execution session coordination, run-history projection, and completed evidence projection.

Application returns immutable workflow snapshots. ViewModels do not retain `IWorkspaceFileSystem`, Infrastructure implementations, raw exceptions, or unredacted process output.

## Catalog and dynamic form

Create Project begins from `IBlueprintCatalog.InspectAsync`. Only `BuiltIn` and `TrustedLocal` entries in `ExecutableBlueprints` can be selected. Untrusted, disabled, quarantined, or malformed entries are inspect-only and show stable issue codes/remediation.

Each `BlueprintInputPropertyDefinition` maps exactly once:

- `Text` -> labeled `TextBox` with required and min/max-length validation.
- `Choice` -> non-editable `ComboBox` containing the exact allowed values.
- `Boolean` -> labeled `CheckBox`.
- `WholeNumber` -> invariant integer text field with min/max validation.

Defaults are applied once when the blueprint changes. Field IDs, required state, constraint summaries, validation issues, and accessible names come from the schema. Unknown enum values fail closed; Desktop never guesses a control.

Project name, output root, output folder, blueprint, dynamic inputs, features, completion IDE, and optional team profile are validated inline. The target path is derived from root plus one guarded relative folder segment. M7 rejects non-canonical, reserved, traversal, reparse, existing non-empty, or inaccessible targets before planning. Description/client/project-code remain presentation metadata only when a persistence/domain contract exists; M7 does not smuggle them into arbitrary blueprint variables.

## Planning and preview

`Review plan` performs the following transaction without mutating the target:

1. Snapshot and validate the complete draft.
2. Confirm the selected blueprint remains executable and its fingerprint is current.
3. Convert schema values to a `ProjectRecipe` with Git disabled and validated completion intent.
4. Ask the guarded target preflight for absence/emptiness and write eligibility evidence.
5. Call `IProjectPlanner.CreatePlanAsync` once.
6. Publish an immutable `ProjectCreationPlanSnapshot` containing the recipe, `PlannedProject`, blueprint fingerprint, target descriptor, creation IDs, and creation timestamp.

The preview displays exact plan hash, blueprint ID/version/trust, expected artifact tree, dependencies, required tools and compatibility, ordered steps, validators, redacted command previews, effective inputs, enabled features, warnings, and estimated operation counts. It does not estimate duration or disk size when no evidence exists.

Any draft change clears the accepted snapshot. Execution compares the current draft fingerprint with the reviewed snapshot and refuses stale plans.

## Presets

Presets serialize a versioned, canonical, bounded recipe draft through `PersistableJson` and `IPresetStore`. The codec has an allowlisted schema and rejects unknown fields, secret-shaped keys/values, `.env` content, connection strings, private keys, credentials, and oversized collections. Loading a preset revalidates its blueprint version against the current catalog and reports unavailable versions without silently upgrading.

Saving a preset never starts planning or execution. Presets include project settings and dynamic values but never run IDs, target-derived workspace handles, process output, tokens, or cached plans.

## Execution session

After explicit confirmation of the preview, the Application workflow creates an `ExecutionRequest` using the reviewed `PlannedProject`, fresh `ProjectRun`, and guarded workspaces from `IProjectExecutionWorkspaceFactory`. It invokes only `IExecutionOrchestrator` and reports bounded `ExecutionProgressLine` snapshots.

Execution Center shows ordered plan steps and validators with `Pending`, `Running`, `Passed`, `Warning`, `Failed`, `Skipped`, or `Cancelled` presentation states. It shows bounded elapsed time, attempt number, redacted command preview, bounded redacted progress, stable error code/summary/remediation, and checkpoint status. The UI never reconstructs lifecycle state from log text.

`Cancel safely` cancels the session token and waits for M5 to durably close the active attempt before presenting Cancelled. Repeated execute/cancel/resume commands are non-reentrant. Observer/dispatcher failures cannot abort orchestration. Shutdown uses the same cooperative cancellation boundary.

Retry and resume are checkpoint-driven:

- Cancelled, ValidationFailed, Planning, and eligible idle Executing checkpoints use `IRunRecoveryService.ResumeAsync` only after its exact marker/plan/blueprint checks.
- Manual retry is offered only when the durable checkpoint satisfies the M5 retry contract.
- Cleanup is offered only for M5-owned cleanup-eligible staging and delegates to `IRunRecoveryService.CleanupAsync`.
- Missing blueprint, fingerprint drift, target drift, marker mismatch, or terminal state is shown as a scrubbed refusal; Desktop cannot bypass it.

Open Staging, full log export, and support bundles remain disabled until M10 supplies safe typed ports. M7 does not open arbitrary paths from UI state.

## Local-ready and history UX

When M5 returns `LocalReady`, the final page displays icon-plus-text success, target display path, plan hash, blueprint ID/version, ordered step/validator evidence, warnings, generation-report references, elapsed time, and actions supported by real ports. `Open IDE` uses the selected trusted IDE ID through `IIdeLauncher`; failure does not rewrite the successful checkpoint. Folder opening remains disabled until a safe typed shell-handoff port exists.

Run History reads `IRunCheckpointStore.ListAsync`, sorts deterministically by durable update evidence available from persistence, and displays exact domain status, current step, attempts, last scrubbed error, blueprint/version, and eligible actions. It never marks a run successful manually or deletes finalized projects.

Dashboard actions route to Create Project, filtered Run History, or Environment Doctor. Saved presets initialize a new mutable draft in one action. Missing recent project paths remain read-only unavailable entries.

## WPF composition and responsiveness

M7 follows the M6 feature-organized Desktop project. Views are resource-backed WPF controls with no code-behind orchestration. Dynamic controls use DataTemplates selected by closed input kind, not reflection or arbitrary type names. Large artifact/step/history lists use bounded snapshots and WPF virtualization. Progress publication is throttled on the dispatcher and retains a bounded ring buffer.

The shell retains the 960x640 minimum and supports 100%, 125%, and 150% scaling. Every status uses icon plus text, every field has an accessible name and nearby issue, keyboard focus moves to the first invalid field or failed step, and primary actions expose deterministic enabled/busy/cancel states.

## Error and privacy policy

Domain/Application validation issues are projected inline. Operational failures use `DevForgeError` code, summary, phase, retryability, and suggested actions. Raw exception text, stack traces, environment values, executable paths, source content, and unredacted output are never shown or persisted by Desktop.

All target operations go through guarded workspaces. All commands remain inside M5 handlers and `IProcessRunner` with separated executable/arguments. No Administrator path, shell string, `cmd /c`, arbitrary PowerShell, AI API, cloud backend, telemetry, or remote mutation is added.

## Testing strategy

Application unit tests cover draft snapshots, schema conversion, catalog trust, target preflight, explicit plan invalidation, plan/run identity binding, stale-plan refusal, preset codec privacy, execution request construction, cancellation, resume/manual-retry eligibility, and immutable progress snapshots.

Desktop tests cover every dynamic input kind/default/constraint, blueprint switching, inline validation/focus, preview completeness, command enablement, progress throttling/bounds, cancel/resume/cleanup routing, LocalReady evidence, run-history filters, Dashboard navigation, safe mode, and host composition.

Infrastructure integration tests cover canonical target decomposition, absent/empty/non-empty targets, junction/reparse refusal, write probe cleanup, run-artifact workspace creation, cancellation, and production-like guarded failures.

One M7 E2E fixture creates a bounded TrustedLocal test blueprint package in a temporary guarded workspace, loads it through the real M4 catalog, plans it, executes real M5 safe file handlers into owned staging, validates/finalizes it, and reaches `LocalReady` through the M7 workflow without terminal use. It does not become a shipped production blueprint.

WPF smoke covers Configure, Preview, Execution Center, LocalReady, Blueprint Catalog, and Run History at the three M6 scaling constraints. Security scans verify no secrets, `.env` content, raw exceptions, source content, or unredacted output enter presets, notifications, UI state, checkpoints, or test artifacts.

## Exit gate

M7 is complete only when:

- A schema change adds a supported input without changing Create Project XAML.
- Invalid input, incompatible tools/rules, untrusted blueprint, stale plan, and non-empty target all fail before mutation.
- Preview exactly matches the immutable plan hash and evidence.
- The E2E fixture reaches `LocalReady` through WPF/Application workflow without a terminal or direct Desktop I/O.
- Cancel/resume/manual retry/cleanup use M5 durable contracts and never duplicate successful evidence.
- LocalReady is never mislabeled Domain `Completed`; Git/GitHub remains disabled until M8.
- Locked restore, format, Release build, full serialized tests, focused M7 tests, EF model consistency, WPF scaling smoke, privacy scan, and architecture tests pass with zero skipped M7 tests.
- Implementation plan/status, ADR, README, and changelog contain exact command evidence.

## Deferred work

M8 activates Git initialization, branch policies, Git client choices, GitHub auth/publish, `PublishPending`, and true `Completed` transitions. M9 supplies the three production MVP blueprints. M10 supplies support bundles, production log retention/viewing, safe folder handoff, packaging, and release hardening. M11 expands the V1 catalog only after M10 gates pass.
