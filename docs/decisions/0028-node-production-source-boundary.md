# ADR-0028: Node production runtime and source-only handoff

Date: 2026-08-27
Status: Accepted locally on 2026-08-28; foundation, Next production acceptance
and full-solution regression gate passed (1,762/1,762).
External release gates remain open.

## Decision

Execute root pnpm operations in a guarded source snapshot below the existing
run-owned staging container (`tooling/node/project`). Never transfer node_modules,
the pnpm store or .next to the final target. The existing immutable React 1.0.0
package explicitly promises dist/index.html: retain its verified static dist
tree through exact atomic copies after successful build. No other package gets
this exception; Next handoff remains source-only. Compare every original source
byte before and after each command; unexpected files outside the explicitly
recognized tooling output roots fail closed. Bind output inventory to command
evidence. Final source and engine evidence retain the existing complete-tree
publication digest, secret scan, local Git receipt and recovery rules.

Use the installed trusted Node executable and installed pnpm JavaScript entry
point, without shell shims or Corepack downloads. Declare only Windows runtime
paths and protected pnpm policy: frozen installation, no lifecycle hooks, no
pnpmfile hooks, hoisted/copy dependency layout, shell emulator, no package-manager
auto-download and no inherited user/global registry credentials or NODE_OPTIONS.
Use run-owned pnpm home/cache/store paths. No Administrator or machine setting.
The runner prepends the fixed `--ignore-workspace` option before pnpm commands:
pnpm discovers ancestor workspaces before reading environment settings. A real
ancestor malformed-workspace regression proves this barrier. Every exited command
is source-verified even if its declared allowed exit is nonzero.

Real static JavaScript exceeds the scanner's former 16,384-character line limit.
Scan each full line under the unchanged 1 MiB UTF-8 file bound and 100 ms per-regex
timeout. Do not skip minified files or split tokens across arbitrary chunks.
Long safe/secret-bearing bundle regressions and oversized-file refusal protect
this correction; all shipped static bytes remain scanned and digest-bound.

The generic assignment detector flags React's public input-type boolean map.
An initial JavaScript delimiter exception was rejected: strings/comments/templates
can contain identical text. Do not infer syntax using regex or weaken the global
assignment rule. The accepted closed false-positive rule requires the complete
raw-byte SHA-256 of the reviewed public bundle, `.js`, line 8, index 21275, length
11 and exact generic-assignment match `password:!0`. Hash the same bounded byte
array that is decoded and scanned; no normalization, second read or runtime
learning. All other patterns/matches remain active. The hash is engine-owned,
not configurable by blueprint, project or user. Review provenance and exact-byte
fixture are in `tests/DevForge.IntegrationTests/Fixtures/README.md`.

Any byte change loses this specific allowance; publication independently rejects
all tampering. Add `.mjs`/`.cjs` to text candidates and fail closed for JavaScript
with a binary prefix. Test string/comment/template markers, byte/space/BOM/newline/
concatenation mutations, direct bundle tampering (including NUL) and restored-byte
recovery. Changes to the immutable output fail closed until separately reviewed;
the engine must never automatically learn a new permitted hash.

The real React matrix passed all native validators but initially failed reviewed
artifact evidence because source-only handoff omitted its declared dist file.
This compatibility correction preserves the package contract instead of changing
the immutable package or bypassing evidence. Existing dist bytes must match the
tooling tree on retry; no overwrite of conflicting target files is permitted.

Dependency tooling has separate explicit enumeration bounds (65,536 files,
16,384 directories, depth 48); payload keeps its existing 4,096-file bound.
The larger cleanup bound applies only to checkpoints with root pnpm operations.
All enumeration still rejects junctions/reparse points. Cleanup is exclusively
through the existing marker/lease-authorized staging manager. Source cannot
declare tooling/output namespaces. Root-only pnpm remains a single-project
boundary, not implicit monorepo support.

## Alternatives

Transferring node_modules/.next exposes staging-dependent files and excessive
payload size. Ignoring them during final-tree hashing weakens tamper detection.
Deleting outputs from payload introduces destructive recovery windows. A
source-verified tooling snapshot avoids all three while retaining artifact
inventory in execution evidence. Developers install/build at the final path.

## Candidate and gates

After foundation acceptance, add only test-discovered `web.next-ts`: App Router,
strict TypeScript, local production server smoke, independent formatter/lint/test/
build, pinned dependencies/lockfile and seven handoff documents. No database,
auth, Docker, cloud deployment or production catalog promotion.

Required regressions: runtime isolation and protected overrides; source snapshot
tamper, unexpected output and junction refusal; frozen install and validation;
source-only publication and repeat recovery; bounded smoke shutdown; package
checksum quarantine; real generated-project acceptance and full managed gates.
Windows 11, UX and packaging/remote CI release holds remain unchanged.

References: [pnpm settings](https://pnpm.io/10.x/settings),
[Next lint migration](https://nextjs.org/docs/app/guides/upgrading/version-16).

## Toolchain security review

The installed Node 22.21.1 is not the certification runtime for the new candidate.
Use workspace-local portable Node 22.23.2 (July security release), downloaded from
nodejs.org and verified against its official SHA-256 inventory:
`1177b4137ba5adaa56354ae40f1080c7450e8ae09cecb47da459d1c52ac99f97`
for `node-v22.23.2-win-x64.zip`. No machine installation or persistent PATH change.
The July fixes are described in the
[official Node advisory](https://nodejs.org/en/blog/vulnerability/july-2026-security-releases).

Pin Next 16.3.3, not 16.3.2: the
[official Next release index](https://nextjs.org/blog) announces the August 25
security patch for two critical issues, and the npm registry confirms 16.3.3.
Foundation acceptance passed with the production React workflow before candidate
implementation. This dependency review alone is not Next build/release evidence.

Use ESLint 10.9.1 with the official `@next/eslint-plugin-next` 16.3.3 directly,
alongside pinned JS, TypeScript and React Hooks recommended rules and Next's
core-web-vitals rules. `eslint-config-next` currently pulls legacy peer ranges
that exclude ESLint 10; ESLint 9.39.4 is marked unsupported by the registry.
Neither ignore peer incompatibilities nor choose that unsupported fallback.
The direct-plugin lock resolves without peer/deprecation warnings; no compatibility
adapter, peer override or global rule disable is used. All six required gates
(format, lint, typecheck, test, build, smoke) must pass in the generated project.
