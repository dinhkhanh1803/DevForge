# M3 Restricted Template Renderer Closure Design

**Status:** Proposed - awaiting user review
**Date:** 2026-08-10
**Milestone:** M3 closure prerequisite for M4

## Context

The detailed DevForge DOCX specification assigns the restricted Scriban template renderer to M3. The repository currently contains `ITemplateRenderer` and `TemplateRenderRequest`, but no production implementation or renderer tests. M3 was therefore recorded complete with one missing exit item.

This closure implements and verifies that missing port before any M4 production code begins. It does not add blueprint catalog, planner, orchestration, template file loading, or blueprint content.

## Considered approaches

### Restricted Scriban runtime - chosen

Use Scriban as required by the source specification, but construct a closed runtime, inspect the parsed AST against an allowlist, expose only string-backed `ScriptObject` values, and bound every input/output dimension. This preserves the selected technology without treating a general scripting language as trusted.

### Custom token replacement - rejected

A custom `{{ variable }}` replacer would be smaller but would immediately need an ad hoc parser for conditional content, escaping, diagnostics, and future blueprint needs. It would diverge from the source specification and create a second template language.

### Razor, Liquid, or another renderer - rejected

Changing engines would add a platform decision that the specification already made. Razor also introduces compilation and a much broader .NET execution surface. Another Liquid implementation would add a package while still requiring a sandbox review.

## Goals

- Implement `ITemplateRenderer` in Infrastructure with Scriban 7.2.5.
- Preserve exact template/context snapshots and deterministic text output.
- Support safe variable output and conditional blocks needed by text templates.
- Support dotted context paths without exposing .NET objects.
- Reject scripts, functions, assignments, includes, loops, imports, member methods, and runtime evaluation.
- Enforce request, AST, recursion, cancellation, and output bounds.
- Map failures to stable scrubbed error codes without template/context leakage.
- Update M3 documentation and rerun the complete M3 exit gate.

## Non-goals

- No template file discovery or filesystem access inside the renderer.
- No blueprint catalog or manifest variable validation; M4 owns those concerns.
- No arbitrary Scriban built-in functions, custom delegates, regex, date/time, random, environment, or network access.
- No template includes/imports or loader.
- No loops or collection model in the current string-only context contract.
- No HTML escaping policy; DevForge renders source/configuration text and format-specific escaping belongs to typed M5 handlers.
- No production template or blueprint is added.

## Dependency and placement

- Pin `Scriban` exactly at `7.2.5` in `Directory.Packages.props`.
- Reference `Scriban` without a local version from `DevForge.Infrastructure` only.
- Regenerate the Infrastructure and dependent project lock files through locked NuGet workflow.
- Place production code under `src/DevForge.Infrastructure/Templates/`.
- Keep Application and Domain free of Scriban references.
- Add Application contract tests to `DevForge.UnitTests` and renderer behavior/security tests to `DevForge.IntegrationTests`, preserving the approved project-reference graph.

The package version was verified on the official NuGet registry on 2026-08-10. The selected version targets .NET 8 and compatible higher frameworks, including the project's .NET 10 target, and supersedes earlier 7.0.x/6.x versions shown with known security advisories by the registry.

The runtime design was also checked against the official Scriban 7.2.5 source. That release exposes `TemplateContext.CancellationToken`, `TemplateContext.PushOutput(IScriptOutput)`, an empty-builtins constructor, strict/relaxed access switches, and read-only `ScriptObject` members. The bounded writer therefore uses public supported APIs rather than reflection or an implementation-specific field.

## Contract hardening

`TemplateRenderRequest.Create` remains the only public construction boundary and snapshots the context once. It adds these constants and validations:

- maximum template length: 1 MiB characters;
- maximum context entries: 256;
- maximum context-name length: 256 characters;
- maximum value length: 64 KiB characters;
- maximum total context value length: 2 MiB characters;
- no null characters in template, names, or values;
- no control characters in context names;
- context names use one or more identifier segments separated by dots;
- each segment matches `[A-Za-z_][A-Za-z0-9_]*`;
- ordinal uniqueness after trimming;
- no parent/child collisions such as `project` with `project.name`;
- no secret-shaped segment or full name;
- context values must pass the existing credential-shape defense but remain stored verbatim rather than trimmed.

The template itself is not passed through `RedactedText`: trusted templates may legitimately contain `.env.example` keys or example placeholders. Template text is bounded and never logged. Secret scanning of generated files remains a later pipeline gate.

Invalid input returns aggregated stable `ValidationIssue` values. No validation path throws for expected caller data.

## Supported template language

The allowed Scriban subset is intentionally small:

- raw text;
- `{{ variable }}` output;
- dotted variable paths backed only by nested `ScriptObject` instances created from the validated request;
- string and Boolean literals used in conditions;
- `if`, `else if`, `else`, and `end` conditional blocks;
- `==`, `!=`, `&&`, `||`, `!`, and parentheses inside conditions.

Because the current Application contract exposes string context values only, variable conditions must use explicit comparison, for example `project.kind == "api"`. Implicit string truthiness such as `if project.enabled` is rejected. Boolean literals may compose already-Boolean comparison results but no string is coerced to Boolean.

Everything else is rejected by AST policy before rendering, including:

- local/global assignment;
- `for`, `while`, `tablerow`, `case`, `when`, `break`, and `continue`;
- function definitions or calls;
- pipes and built-in namespaces such as `object`, `regex`, `date`, `math`, `string`, and `array`;
- `eval`, `eval_template`, include, import, capture, wrap, and template-loader operations;
- array/object literals and indexers;
- optional member access, member methods, and any .NET object exposure;
- scientific or Liquid parsing modes.

This subset is sufficient for current string context rendering and prevents a blueprint template from becoming a general script.

## Parse and AST policy

`RestrictedScribanTemplateRenderer` parses only Scriban text mode. Parse diagnostics are checked before constructing a runtime context.

A dedicated `RestrictedTemplatePolicy` walks every AST node and:

- permits only the exact node kinds needed by the supported subset;
- counts at most 10,000 nodes;
- permits at most 64 nested syntax levels;
- rejects an output expression that is not a validated variable path or literal;
- rejects every function-call, assignment, loop, loader, indexer, and unsupported operator node;
- returns one stable policy failure without echoing source text.

The renderer does not attempt to sanitize a forbidden AST into an allowed one. The complete template is rejected.

## Runtime sandbox

Every call creates a fresh Scriban `TemplateContext`; contexts are never shared because they are mutable and not thread-safe.

The runtime configuration:

- starts with an empty built-in `ScriptObject`;
- pushes one read-only global `ScriptObject` assembled from validated context names;
- exposes only strings and nested `ScriptObject` nodes;
- sets strict-variable behavior;
- disables relaxed member, function, target, and indexer access;
- has no template loader;
- imports no .NET object, type, method, property, delegate, or service;
- sets the caller cancellation token on the render context;
- uses invariant behavior and performs no culture-dependent conversion.

Nested context objects are constructed deterministically from ordinal-sorted dotted paths. Values remain exact, including leading/trailing whitespace and Unicode.

## Bounds and cancellation

The renderer checks cancellation:

1. before parsing;
2. after parsing and AST validation;
3. while Scriban evaluates through its context cancellation token;
4. before returning the final output.

A `BoundedTemplateWriter` implements Scriban's public `IScriptOutput`, is installed through `TemplateContext.PushOutput`, and stops output after 4 MiB characters. Scriban's own `LimitToString` truncation is disabled for this restricted context so it cannot silently add an ellipsis before the writer observes the limit. The writer checks cancellation on each write, implements both synchronous and asynchronous writes, and stores at most the configured bound. Exceeding the limit fails the render; generated source is never silently truncated.

Pre-cancelled and mid-render cancellation propagate as `OperationCanceledException`. Cancellation is not converted into a template error.

## Error model

Expected request errors remain `ValidationResult` issues. Runtime failures use `InfrastructureOperationException` with these stable codes and fixed safe messages:

- `template.parse.invalid` - Scriban parse diagnostics exist;
- `template.policy.forbidden` - AST contains a disallowed construct;
- `template.variable.missing` - strict lookup cannot resolve a variable;
- `template.output.too-large` - bounded output limit was exceeded;
- `template.render.failed` - another known Scriban render failure occurred.

Exception mapping never retains an inner exception and never includes raw template text, context names/values, rendered partial output, source spans containing text, or a Scriban exception message. Fatal runtime exceptions are not swallowed.

## Rendering flow

```text
TemplateRenderRequest.Create
    -> immutable bounded template/context
    -> Template.Parse
    -> parse diagnostic check
    -> RestrictedTemplatePolicy AST walk
    -> fresh empty TemplateContext
    -> nested read-only ScriptObject context
    -> BoundedTemplateWriter + cancellation
    -> deterministic string result
```

The renderer is pure with respect to DevForge-managed effects: it reads no file, starts no process, accesses no database, and writes no log.

## Testing strategy

### Application contract tests

- Single-enumeration snapshot remains exactly once.
- Template, entry-count, name, value, and total-context bounds aggregate stable issues.
- Dotted identifiers and exact verbatim values are accepted.
- malformed/control-character names, prefix collisions, null characters, secret-shaped names, and credential-shaped values are rejected.
- Caller collection mutation cannot alter a valid request.

### Renderer integration tests

- Scalar and dotted variable output.
- Conditional true/false, equality, inequality, Boolean composition, and nested conditionals.
- Unicode, CRLF/LF, blank lines, indentation, and leading/trailing context whitespace remain deterministic.
- Missing variables fail with the stable scrubbed code.
- Malformed templates fail without returning source fragments.
- Each forbidden AST family has a permanent regression test: assignment, loop, function, pipe, eval, include/import, array/object, indexer, and member method.
- A secret fixture appearing in template/context does not appear in exception text, captured test output, or diagnostics.
- Oversized AST, deep nesting, oversized output, pre-cancellation, and mid-render cancellation fail safely.
- Concurrent renders use independent contexts and return their own values.
- Repeated renders under different current cultures return byte-for-byte identical results.

### Architecture and dependency tests

- Only Infrastructure references `Scriban`.
- Package version is centrally pinned and lock files are consistent.
- Renderer source contains no filesystem, process, network, reflection activation, environment lookup, or logger dependency.
- Existing forbidden-process/filesystem architecture tests remain green.

## Documentation correction

The closure will:

- amend the M3 design and implementation plan to list the restricted renderer;
- add ADR-0006 documenting the restricted Scriban runtime and dependency pin;
- correct `docs/implementation-status.md` with fresh M3 evidence;
- add the renderer to `CHANGELOG.md`;
- keep the already committed M4 design on its separate branch and record that M4 production work was blocked until this gate passed.

No previous M3 commit is rewritten. The closure is an additive, reviewable checkpoint.

## Exit gate

The M3 closure is complete only when:

- `ITemplateRenderer` has a production Infrastructure implementation;
- the supported subset and every forbidden construct are covered;
- bounds, cancellation, concurrency, culture determinism, and privacy tests pass;
- no raw template/context/rendered content appears in failures;
- Scriban is centrally pinned with consistent lock files and no local package version;
- locked restore, format verification, Release build, full solution tests, focused Application contract tests, focused renderer integration/security tests, and M3 Infrastructure suites all exit 0;
- Release build reports zero warnings and zero errors;
- zero renderer/M3 test is skipped;
- M3 docs and ADR-0006 record exact commands and results.

Only after this exit gate may implementation planning or code for M4 begin.
