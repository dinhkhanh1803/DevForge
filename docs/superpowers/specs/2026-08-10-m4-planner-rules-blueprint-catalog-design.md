# M4 Planner, Rules, and Blueprint Catalog Design

**Status:** Approved for implementation planning
**Date:** 2026-08-10
**Milestone:** M4 - Planner, Rules, and Blueprint Catalog

## Context

M0-M3 are complete, including the restricted Scriban renderer closure and its six-boundary exit gate. The implemented baseline provides immutable recipe, manifest, execution-plan, environment, persistence, guarded workspace, process, secret-scanning, IDE, and template-rendering contracts. M4 turns those contracts into a deterministic planning boundary without executing a step.

The official specification requires M4 to deliver catalog loading, manifest/schema validation, compatibility evaluation, an immutable plan preview, and plan hashing. Invalid or malicious blueprint packages must be blocked before execution and snapshot tests must prove deterministic output.

M4 does not add a production blueprint. Test-owned fixture packages prove the engine. The three MVP blueprints remain M9 work.

## Goals

- Discover built-in and local blueprint packages through guarded workspace abstractions.
- Assign trust from catalog provenance, never from manifest content.
- Validate package structure, checksums, manifest data, input schema, rules, handler policy, and variable references.
- Quarantine invalid, conflicting, or malicious packages and keep them out of the executable catalog.
- Resolve only an exact blueprint ID and semantic version from a recipe.
- Evaluate required inputs, tool versions, engine compatibility, features, and compatibility rules before execution.
- Produce an immutable, previewable execution plan with stable warnings, requirements, predicted artifacts, and ordered steps.
- Produce the same SHA-256 plan hash for the same normalized blueprint, recipe, and policy inputs.
- Preserve Clean Architecture and all M0-M3 security boundaries.

## Non-goals

- No step execution, staging, retry, resume, finalization, or generation report writing. Those belong to M5.
- No WPF catalog or plan-preview screens. Those belong to M6-M7.
- No Git or GitHub behavior. That belongs to M8.
- No production blueprint content or catalog expansion. That belongs to M9 and M11.
- No blueprint migration or modification of an existing customer project.
- No arbitrary expression evaluator, script runtime, reflection-based action activation, network access, or executable download.

## Chosen architecture

M4 keeps I/O and policy logic separate:

- `DevForge.Blueprints.Abstractions` owns dependency-free normalized blueprint models, semantic-version comparison, catalog inspection types, and the closed rule/action vocabulary.
- `DevForge.Application` owns recipe-to-plan orchestration, rule evaluation, input normalization, preview construction, canonical plan serialization, and SHA-256 hashing.
- `DevForge.Infrastructure` owns guarded package discovery, bounded YAML/JSON parsing, checksum verification, source trust assignment, quarantine classification, and catalog metadata persistence.
- `DevForge.Domain` owns immutable execution-plan values and invariants. It does not parse YAML, read files, or reference Infrastructure.
- `DevForge.Blueprints.BuiltIn` remains an empty production-content host in M4. M4 tests supply fixture packages through configured guarded roots.

This preserves the dependency direction `Infrastructure -> Application -> Domain`, while `Application` and Infrastructure may reference `Blueprints.Abstractions`.

## Package source and trust

A configured `BlueprintPackageSource` contains:

- a stable source identifier;
- a guarded `IWorkspaceFileSystem` rooted at the catalog directory;
- source provenance: built-in or local.

Trust is never deserialized from a package. Built-in sources assign `BuiltIn`. Local trust is assigned per package identity and aggregate checksum through `IBlueprintMetadataStore`: a new local package is `Untrusted`, an exact persisted checksum with `TrustedLocal` remains trusted, and a changed checksum is downgraded to `Untrusted` until the user explicitly approves it in a later UI flow. A mismatch between a package file and its own `checksums.json` is corruption and produces `Quarantined`. A loader failure also produces `Quarantined`. A local package can never acquire `BuiltIn` trust.

`Untrusted` packages are inspect-only. `Quarantined` packages are visible only through inspection results. Neither is returned by executable list or exact resolution APIs.

## Required package layout

Each immediate child of a configured source root is one package:

```text
<blueprint-id>/
|-- manifest.yaml
|-- inputs.schema.json
|-- rules.yaml
|-- checksums.json
|-- templates/
|-- overlays/
|-- validators/
|-- README.md
```

`manifest.yaml`, `inputs.schema.json`, `rules.yaml`, and `checksums.json` are mandatory. Content folders may be empty in test fixtures. All paths are workspace-relative and pass the M3 canonical/reparse guard.

`checksums.json` is a JSON object whose keys are canonical forward-slash relative paths and whose values are lowercase 64-character SHA-256 strings. Every regular package file except `checksums.json` must be declared exactly once. Missing, duplicate, extra, absolute, parent-relative, device, rooted, or reparse-backed entries quarantine the package. The checksum file cannot declare itself.

The package directory name must equal the normalized manifest ID. The aggregate package checksum is SHA-256 over the canonical UTF-8 sequence of ordinal-sorted `path`, null separator, declared file hash, and newline entries. This fingerprint changes when any declared path or content hash changes without depending on enumeration order.

## Bounded loading

Catalog refresh fails closed with these limits:

- at most 256 packages per source;
- at most 2,048 files per package;
- at most 32 MiB total declared content per package;
- at most 256 KiB for each control file;
- at most 16,384 characters for one YAML/JSON scalar;
- at most 128 nested mapping/sequence levels;
- a 100 ms timeout for every regular expression used during validation.

Exceeding a limit creates a stable quarantine issue and does not return raw file content, absolute paths, or exception text.

## Parsing strategy

YAML parsing uses `YamlDotNet` 18.1.0, pinned centrally in `Directory.Packages.props`. The version was verified against the official NuGet registry on 2026-08-10 and supports .NET 10. JSON uses `System.Text.Json` from the .NET 10 BCL.

The YAML loader uses a closed DTO graph with exact property names and rejects:

- unknown fields;
- duplicate mapping keys;
- aliases, anchors, merge keys, and custom tags;
- non-scalar dictionary keys;
- unsupported scalar coercion;
- values outside configured bounds.

The loader never enables arbitrary type construction or reflection-based tags. Parser exceptions are mapped to stable `DF-BP-001` inspection issues with scrubbed messages.

## Manifest v1 contract

`manifest.yaml` owns:

- `id`, `name`, `version`, `engineVersion`;
- required tool definitions;
- declared feature definitions;
- ordered action steps;
- ordered validators;
- predicted generated files (workspace-relative paths; extensionless files are valid, directories are not artifacts);
- declared project dependencies.

`inputs.schema.json` owns the dynamic input definition. M4 supports a deliberate JSON Schema subset:

- root `type: object`;
- `properties`, `required`, and `additionalProperties: false`;
- property types `string`, `boolean`, and `integer`;
- `enum`, `default`, `minLength`, `maxLength`, `minimum`, and `maximum` where applicable.

References, remote schemas, regex patterns supplied by a package, conditionals, custom formats, and unevaluated properties are not supported in M4. Unsupported keywords quarantine the package rather than being ignored.

Blueprint, input, feature, action, validator, and rule identifiers retain the existing M1 canonical policy: lowercase ASCII segments separated by dots or hyphens. The camelCase identifiers in the specification appendix are illustrative data, not a second identifier grammar. This choice is recorded in ADR-0007 so M9 authors have one unambiguous convention.

Feature definitions are a separate manifest collection with a stable ID and optional default-enabled state. Every `ProjectRecipe.Features` entry must resolve to one declared feature. Feature-specific settings remain ordinary typed input-schema properties; enabling a feature does not create a second untyped value channel.

`rules.yaml` owns ordered compatibility rules. Each rule contains:

- stable `id`;
- `condition` expression;
- severity `blocking` or `warning`;
- user-safe `message`;
- optional user-safe `remediation`;
- `override: none` in M4.

Blocking rules cannot be bypassed. Warning rules appear in the plan preview and do not block planning.

## Closed action policy

The M4 action whitelist is the specification's MVP set:

- `create-directory`
- `render-template`
- `copy-overlay`
- `patch-json`
- `patch-yaml`
- `patch-xml`
- `run-process`
- `package-install`
- `validate-command`
- `git-operation`
- `github-operation`
- `finalize-workspace`

The loader validates action-specific required keys and value kinds against a fixed policy catalog. It rejects unknown handlers, unknown parameters, missing parameters, raw command strings, shell modes, absolute output paths, parent traversal, destructive arbitrary paths, secret-shaped variables, and handler values outside their trust policy.

The normalized M4 payload contract is:

| Handler | Required typed fields | Policy |
| --- | --- | --- |
| `create-directory` | `path` | Workspace-relative staging path. |
| `render-template` | `source`, `target` | Package-relative template source and staging-relative target. |
| `copy-overlay` | `source`, `target` | Package-relative overlay source and staging-relative target. |
| `patch-json`, `patch-yaml`, `patch-xml` | `target`, ordered `operations` | Each operation is `set` or `remove`, has a format-specific bounded selector, and may carry one immutable plan value. |
| `run-process` | `executable`, ordered `arguments`, `workingDirectory`, `allowedExitCodes` | Executable is a trusted tool ID; working directory is staging-relative; no command-line or shell field exists. |
| `package-install` | `packageManager`, ordered `arguments`, `workingDirectory` | Package manager is a resolved trusted tool ID. |
| `validate-command` | `executable`, ordered `arguments`, `workingDirectory`, `allowedExitCodes`, `required` | Same process restrictions; `required: false` produces warning evidence in M5. |
| `git-operation` | `operation`, typed operation payload | Built-in only; operation is from the closed Git vocabulary reserved for M8. |
| `github-operation` | `operation`, typed operation payload | Built-in only; operation is from the closed GitHub vocabulary reserved for M8. |
| `finalize-workspace` | no package-controlled path | Built-in only; M5 supplies guarded staging and target context. |

Every action and validator also has its normalized ID and positive timeout. Retry policy is selected by the built-in handler policy, not by an untrusted arbitrary integer or script.

`TrustedLocal` packages may use safe file/template/patch/validation actions and whitelisted process/package actions. Git, GitHub, and finalization actions are built-in only. `Untrusted` and `Quarantined` packages cannot produce an executable plan.

Process actions preserve executable identity and argument templates as separate typed fields. A manifest cannot provide a combined command line.

## Immutable plan values

Execution-step inputs evolve from flat strings to a guarded immutable JSON-like value tree:

- string;
- Boolean;
- signed 64-bit integer;
- ordered array;
- ordinal-keyed object.

Factories snapshot every input exactly once and reject null/undefined nodes, duplicate object keys, excessive depth, secret-shaped keys, unsupported numeric values, and credential-shaped content where the field policy requires a non-secret value. Path fields separately allow safe names such as `.env.example` while rejecting `.env`, traversal, rooted/device paths, and credential content. Arrays preserve order. Object keys are normalized and sorted only during canonical serialization.

This allows argument lists to remain arrays through planning and M5 execution instead of being flattened into a shell-like string.

## Compatibility rule grammar

M4 implements a small parser and evaluator, not a general-purpose expression engine:

```text
expression  := or-expression
or          := and ("||" and)*
and         := primary ("&&" primary)*
primary     := "(" expression ")" | comparison
comparison  := operand operator operand
operator    := "==" | "!=" | "in" | "not-in" | "satisfies"
operand     := identifier | string | boolean | integer | list
```

Allowed identifiers are resolved from a fixed planning context:

- `runtime.os`, `runtime.arch`;
- `engine.version`;
- `blueprint.id`, `blueprint.version`;
- `recipe.input.<id>`;
- `recipe.feature.<id>`;
- `team.package-manager`, `git.branch-policy`;
- `tool.<id>.available`, `tool.<id>.version`.

No function call, member reflection, assignment, interpolation, regular expression, file access, environment lookup, process access, or network access exists in the grammar.

`satisfies` accepts a semantic-version value on the left and a parsed `SemanticVersionRange` on the right. Semantic versions are compared numerically with SemVer 2.0 prerelease precedence; build metadata does not affect precedence. Other type-invalid comparisons return a stable rule validation issue instead of coercing values.

## Catalog refresh and resolution

Refresh proceeds in this order:

1. Enumerate source package directories through the guarded workspace.
2. Enforce package/file limits and reject reparse escapes.
3. Read and validate `checksums.json`.
4. Verify every declared file hash before parsing package-controlled content.
5. Parse manifest, input schema, and rules into closed DTOs.
6. Build the normalized `BlueprintManifest` through its guarded factory.
7. Validate handlers, variables, tool ranges, rule grammar, and trust policy.
8. Reconcile persisted `BlueprintMetadataRecord` state, including `IsDisabled`.
9. Publish one immutable catalog snapshot atomically.

M4 adds guarded immediate-child directory enumeration to `IWorkspaceFileSystem`; the Windows implementation applies the same canonical and reparse checks as file enumeration. Catalog discovery does not use direct `Directory` calls.

Readers see either the previous complete snapshot or the new complete snapshot. They never observe a partial refresh.

An individual invalid package becomes a quarantine inspection entry while other packages may load. A source-root enumeration failure, cancellation, or metadata-store failure aborts the refresh and retains the entire previous catalog snapshot. Cancellation never publishes quarantine entries derived from an incomplete scan.

Catalog order is deterministic: blueprint ID ordinal ascending, then semantic version descending, then source identifier ordinal ascending. Exact duplicate ID/version packages are conflicts. Conflicting versions are quarantined as a group; source order never silently shadows one package with another.

`FindAsync` resolves only exact ID/version. It never selects `latest`, widens a version range, or falls back to another version. Disabled, untrusted, quarantined, or conflicting packages are not executable matches. Engine and tool compatibility remain planner decisions so an available exact package can return a precise `DF-PLAN-001` issue instead of appearing missing.

`IBlueprintCatalog` retains exact `ListAsync` and `FindAsync` behavior and adds `RefreshAsync` plus `InspectAsync`. `FindAsync` returns a `ResolvedBlueprint` containing the normalized manifest and an opaque fingerprint: source ID, package-relative ID, assigned trust, and aggregate checksum. It never exposes an absolute catalog path. M5 will use the fingerprint to re-resolve package content and reject checksum drift before execution.

## Planner data flow

`ProjectPlanner.CreatePlanAsync` performs no direct file or process operation. It receives `IBlueprintCatalog`, `IEnvironmentDoctor`, the rule engine, and a fixed engine-version provider through Application ports. It obtains the current `EnvironmentSnapshot` through `IEnvironmentDoctor` and uses the catalog's immutable snapshot:

1. Resolve exact blueprint ID/version.
2. Validate engine range against the injected DevForge engine version.
3. Validate recipe keys and values against `inputs.schema.json`.
4. Apply manifest defaults only when a recipe value is absent.
5. Reject unknown inputs and features.
6. Validate required tools and semantic-version ranges.
7. Build the fixed rule context and evaluate rules in declared order.
8. Stop with aggregated stable issues when any blocking requirement fails.
9. Resolve variable templates without rendering files or executing commands.
10. Build ordered immutable execution steps, validators, requirements, dependencies, predicted artifacts, and warnings.
11. Canonically serialize the structural plan and calculate its SHA-256 hash.
12. Return an immutable `PlannedProject` containing the enriched `ExecutionPlan` and `PlanPreview`.

Cancellation is checked between bounded stages. Cancellation propagates as `OperationCanceledException`; it is not converted into a validation issue.

`IProjectPlanner.CreatePlanAsync` therefore evolves from `ValidationResult<ExecutionPlan>` to `ValidationResult<PlannedProject>`. `PlannedProject.Plan` is the M5 execution input; `PlannedProject.Preview` is the M7 presentation input. The wrapper keeps execution behavior out of the preview model and makes the contract change explicit instead of hiding two return values behind mutable state.

## Variable references

M4 recognizes only `{{ identifier }}` references in manifest scalar templates. The complete scalar is tokenized; malformed delimiters, filters, function syntax, unknown variables, secret-shaped identifiers, and references unavailable at planning time quarantine the package.

The allowed variable catalog follows the specification and includes project, blueprint, team, feature, Git, and runtime values. Staging/run-specific values remain typed placeholders in the plan because M5 owns run workspace allocation. A preview displays those placeholders without inventing absolute paths.

Variable resolution is single pass. Resolved values are never reparsed as templates, preventing recursive expansion.

## Plan preview model

The preview contains only immutable, privacy-safe data:

- exact blueprint ID/version and plan hash;
- ordered step summaries and redacted process previews;
- required tools and compatibility status;
- declared project dependencies;
- predicted workspace-relative artifacts;
- blocking issues and ordered warnings;
- effective non-secret input names and values;
- Git/completion intent summaries without credentials.

No raw exception, absolute catalog path, customer file content, environment-variable value, token, password, connection string, `.env` content, or output from `gh auth token` is admitted.

## Canonical plan hash

`CanonicalPlanSerializer` writes UTF-8 JSON directly through `Utf8JsonWriter` with:

- fixed top-level and model property order;
- ordinal-sorted object keys;
- preserved arrays and step order;
- normalized IDs, semantic versions, paths, and enum names;
- invariant integer and duration representation;
- no indentation, BOM, timestamp, random ID, culture-sensitive text, or machine-specific staging path.

`SHA256.HashData` produces lowercase hexadecimal output prefixed with `sha256:`. The hash includes blueprint identity and aggregate package checksum, normalized effective recipe inputs/features, team/Git/completion policy, action/validator payloads, required tools, dependencies, and predicted artifacts. It excludes target-machine absolute roots, run-owned staging paths, detected tool paths/versions, timestamps, and non-effect-bearing warning outcomes; those belong to execution context or preview evidence. Blocking outcomes produce no plan at all.

Changing any effect-bearing structural value changes the hash. Reordering a manifest object does not. Reordering an ordered step or argument list does.

## Error and quarantine model

Blueprint inspection issues use stable codes and user-safe summaries. The initial M4 catalog includes:

- `DF-BP-001`: malformed structure, YAML/JSON, schema, or unsupported field;
- `DF-BP-002`: checksum or trust provenance failure;
- `DF-BP-003`: forbidden handler, parameter, path, or variable;
- `DF-BP-004`: package bounds exceeded;
- `DF-BP-005`: duplicate/conflicting identity;
- `DF-PLAN-001`: recipe, engine, tool, or compatibility rule prevents planning;
- `DF-PLAN-002`: canonical plan or hash construction failed guarded validation.

Internal parser/I/O exceptions are mapped at the Infrastructure boundary. Results contain no raw YAML/JSON content, absolute package path, matched credential, stack trace, or inner exception.

## Persistence interaction

The catalog reuses `IBlueprintMetadataStore` from M2 as a read-only policy snapshot during refresh. It reconciles discovered ID, version, source, aggregate package checksum, persisted trust, and the existing disabled flag. Discovery never silently writes or overwrites an approved checksum. A changed local checksum is published as inspect-only `Untrusted` and requires an explicit later approval command before metadata can become `TrustedLocal` again. The catalog does not add a migration in M4.

This read-only refresh rule prevents partial metadata writes from weakening an otherwise atomic catalog publication. Explicit trust/disable/pin mutations remain separate user-intent operations for the settings/catalog UI milestone and will use the existing metadata store.

An invalid package without a valid ID/version remains an in-memory inspection result because it cannot satisfy the persistence record invariant. This is deliberate; diagnostic persistence expansion belongs to M10 support-bundle work.

## Security and privacy

- All package reads and enumeration use `IWorkspaceFileSystem`; no direct production `File` or `Directory` access is added outside Infrastructure.
- M4 never starts a process directly. When planning requests a current environment snapshot, the existing M3 Environment Doctor may perform only its fixed probes through `IProcessRunner`.
- YAML/JSON input is bounded before parsing and checksummed before trust-sensitive use.
- Trust is assigned outside the manifest.
- Packages cannot supply .NET types, scripts, regexes, command lines, absolute output paths, or arbitrary expressions.
- Blueprint values and preview content pass privacy guards before entering a plan or hash input.
- Hashing is local and deterministic. No source or metadata is uploaded.
- No Administrator privilege, registry, firewall, service, or network mutation is required.

## Testing strategy

### Unit tests

- SemVer parsing, ordering, range inclusion, prerelease precedence, and build metadata.
- Rule lexer/parser/evaluator precedence, type checking, known identifiers, warnings, and blocking rules.
- Input-schema subset validation, defaults, required fields, bounds, enums, unknown inputs, and secret rejection.
- Action policy and variable-reference validation for every allowed handler and trust level.
- Immutable plan-value snapshots and canonical serialization.
- Planner aggregation, cancellation, exact catalog resolution, deterministic ordering, preview, and hash sensitivity/stability.

### Blueprint contract tests

`DevForge.BlueprintTests` remains dependency-free from Application and Infrastructure. It covers normalized manifest contracts, semantic versions/ranges, action vocabulary, input/feature definitions, rule model invariants, and immutable snapshots. Its approved project-reference graph does not change in M4.

### Infrastructure package-fixture tests

Test-owned packages in `DevForge.IntegrationTests` exercise the real guarded loader and cover:

- one valid built-in fixture;
- one valid trusted-local fixture;
- malformed YAML and JSON;
- unknown/duplicate fields and unsupported schema keywords;
- anchors, aliases, tags, merge keys, and excessive nesting;
- missing, extra, duplicate, and mismatched checksums;
- traversal, rooted/device paths, and junction escape attempts;
- unknown/forbidden actions and raw command strings;
- malformed/unknown/recursive variable references;
- duplicate ID/version conflicts;
- untrusted inspect-only and disabled packages;
- deterministic catalog and plan snapshots.

Additional integration coverage includes:

- Load real fixture trees through `WindowsWorkspaceFileSystem`.
- Verify real SHA-256 checksums, reparse rejection, file bounds, and atomic snapshot replacement.
- Verify metadata-store disabled state is honored without weakening trust.
- Verify no invalid, untrusted, quarantined, or conflicting package can be returned by `FindAsync`.

### Architecture tests

- Application contains no YAML, filesystem, process, EF Core, or Infrastructure dependency.
- Blueprint package parsing remains Infrastructure-only.
- No shell, direct process, direct unguarded filesystem, network, or reflection-based type activation enters M4 production code.

## Exit gate

M4 is complete only when:

- valid built-in and trusted-local fixture packages load through guarded roots;
- invalid and malicious fixtures are deterministically quarantined with stable codes;
- untrusted, disabled, conflicting, and quarantined packages cannot resolve for execution;
- schema, handler, variable, checksum, trust, engine, tool, and rule failures are covered;
- planner snapshots and SHA-256 hashes are stable across repeated runs and change for every effect-bearing mutation;
- catalog refresh exposes no partial state;
- no production blueprint is added;
- locked restore, format verification, Release build, full solution tests, focused Blueprint tests, and focused M4 unit/integration/security tests all exit 0 with zero skipped M4 test;
- `docs/implementation-plan.md`, `docs/implementation-status.md`, ADR-0007, and `CHANGELOG.md` contain exact evidence.

## Deferred work

- M5 executes the typed action payloads, owns staging placeholders, and persists run checkpoints.
- M6-M7 expose catalog and plan preview through WPF/MVVM.
- M8 implements Git/GitHub actions after quality gates.
- M9 supplies the three production MVP blueprint packages and their E2E matrices.
- M10 adds support-bundle persistence for malformed package diagnostics and final release hardening.
