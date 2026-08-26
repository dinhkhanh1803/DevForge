# DevForge Studio Blueprint Author Guide

## Blueprint trust

MVP trust levels are `BuiltIn`, `TrustedLocal`, `Untrusted`, and `Quarantined`. Built-in packages come from the immutable application directory. Trusted local packages come from the guarded user catalog. Untrusted and quarantined packages are inspect-only and cannot plan or execute. A package cannot grant itself trust.

## Required package structure

Each package root contains `manifest.yaml`, `inputs.schema.json`, `rules.yaml`, `templates/`, `overlays/`, `validators/`, `migrations/`, `README.md`, and `checksums.json`. The root name is the exact blueprint ID; version comes from the manifest. Every controlled file is listed in `checksums.json`, and checksum verification runs again when execution reopens a package.

The production M10 catalog remains exactly:

- `desktop.csharp-wpf-tool`
- `web.react-vite-ts`
- `tool.python-cli`

Adding another ID is M11 work and is blocked until the M10 release checklist is complete.

## Inputs, rules, and variables

Declare only the supported Text, Choice, Boolean, and WholeNumber inputs. Defaults must satisfy their schema. Rules use the closed compatibility grammar and deterministic runtime context. Variables are string-only, resolved once, bounded, and cannot access reflection, environment variables, files, processes, or secrets. Any change that affects a plan must affect its canonical hash.

## Allowed actions

MVP actions are create-directory, render-template, copy-overlay, patch-json/yaml/xml, closed run-process/package-install/validate-command operations, built-in Git/GitHub operations, and finalize-workspace. Executables and package-manager operations must map to the existing trusted identity catalog with exact separated arguments. Arbitrary PowerShell, cmd, downloads, registries, services, firewalls, elevation, lifecycle scripts, and custom registries are rejected.

## Authoring workflow

1. Copy the structure of the closest reviewed package without copying stale product facts.
2. Write immutable templates and overlays; use `.env.example`, never `.env`.
3. Define deterministic expected paths and closed validators.
4. Update `checksums.json` for every controlled byte.
5. Run Blueprint contract tests, composed generation twice, the real toolchain matrix, secret/privacy scans, and Git-clean verification.
6. Update handoff documents and engine-owned evidence expectations. Blueprint content must never write `.devforge` engine evidence directly.

Package validation failure must remain scrubbed and inspect-only. Never weaken checksum, traversal, action, or trust validation to accept a package.
