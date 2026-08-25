# Blueprint packages

This is the canonical authoring root for DevForge's built-in blueprint packages. M9 ships exactly these package directories:

- `desktop.csharp-wpf-tool`
- `web.react-vite-ts`
- `tool.python-cli`

Each directory is named by the exact blueprint ID and contains `manifest.yaml`, `inputs.schema.json`, `rules.yaml`, `templates/`, `overlays/`, `validators/`, `migrations/`, `README.md`, and `checksums.json`. The semantic version is declared in the manifest and bound by the package checksum; there is no additional version-directory level.

Build and publish output places these assets under `blueprints\built-in`. Desktop opens that immutable root with built-in provenance and keeps user-managed local packages in a separate application-data source. A package cannot assign its own trust.

