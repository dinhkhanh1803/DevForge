# ADR-0006: Restrict the Scriban template runtime

**Status:** Accepted

**Date:** 2026-08-11

## Context

The authoritative M3 specification requires a restricted Scriban renderer. The original M3 checkpoint implemented process, workspace, scanner, environment, and IDE boundaries but left `ITemplateRenderer` without a production owner. Treating general Scriban templates as trusted would expose functions, assignment, loops, loaders, built-ins, and potentially unbounded evaluation to blueprint content.

## Decision

- Pin `Scriban` exactly at `7.2.5` through `Directory.Packages.props`; only `DevForge.Infrastructure` owns a direct reference.
- Keep `ITemplateRenderer` and its guarded immutable request in Application; keep Domain, Application, Blueprints, Desktop, and CLI free of Scriban APIs.
- Parse templates once and reject every semantic AST node outside the closed language: raw text, scalar/dotted string output, Boolean/string literals, `if`/`else if`/`else`, `==`, `!=`, `&&`, `||`, `!`, and parentheses.
- Reject functions, built-ins, assignment, loops, pipes, eval, includes/imports, loaders, arrays/objects, indexers, optional access, arithmetic, and alternate parsing/escape modes.
- Create a fresh context per render with empty built-ins, strict lookup, all relaxed access disabled, no loader, and a frozen nested `ScriptObject` graph containing strings only.
- Bound requests at the Application factory, AST traversal to 10,000 semantic visits and depth 64, and rendered output to 4 MiB through `IScriptOutput`.
- Propagate cancellation before parse, after policy validation, during each output write, through Scriban's context token, and before returning.
- Return only stable scrubbed failure codes/messages. Never attach parser/runtime exceptions, source spans, template fragments, variable names/values, or partial output. Fatal runtime exception types remain outside broad failure mapping, including when wrapped.
- Keep the renderer pure: it performs no filesystem, process, database, environment, network, reflection, or logging operation.

## Consequences

Blueprint templates can express the deterministic conditionals needed by generation without becoming scripts. The subset is intentionally narrower than Scriban and requires explicit string comparisons rather than implicit truthiness. Adding future syntax requires a new decision, RED security tests, and explicit policy traversal; M4 may consume the renderer port but does not widen the language.

The runtime adds one exact Infrastructure-only package and dependent lock-file entries. It also adds policy maintenance when Scriban is upgraded; dependency tests and the forbidden-family matrix make that review mandatory and visible.

## Rejected alternatives

- A custom token replacer or a second ad hoc template language.
- Razor, Liquid, runtime compilation, or another template engine in place of the specified Scriban dependency.
- Default Scriban built-ins, imported .NET objects, delegates, member methods, or reflection-based access.
- Sanitizing or rewriting forbidden AST nodes into an allowed template.
- Template file loading, includes, imports, or catalog discovery inside the renderer.
- Returning raw Scriban diagnostics or silently truncating output.
