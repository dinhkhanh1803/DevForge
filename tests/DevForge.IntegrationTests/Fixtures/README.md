# Reviewed React artifact fixture

`react-19.2.8-vite-8.2.2.base64` stores exact generated public JavaScript bytes,
not a regenerated approximation. Base64 avoids checkout newline conversion and
preserves the original absence of a trailing newline. Decode before scanning.
Keep the bundled legal comments and accompanying React/Vite core licenses.

- Origin: immutable `web.react-vite-ts@1.0.0`; Team Portal / team-portal, no features.
- Inventory SHA-256: `ac4a7ebff9f5bbe4fc06db2538f8800a39c93ef6415fdbc2bd67a55208de6218`.
- pnpm lock SHA-256: `287e3f5724e4b9b0e84a51cc5cc6eca816d4ce7d510a87ed6d28a97573dec149`.
- Pins: React/react-dom 19.2.8, Vite 8.2.2, pnpm 10.24.0; frozen install with
  lifecycle scripts disabled. Captured on Windows 10 with Node 22.21.1; current
  acceptance additionally checks the same artifact on portable Node 22.23.2.
- Output: `dist/assets/index-CYPyM7up.js`, exactly 190720 bytes.
- Raw SHA-256: `0dc53246ec934df87e6acfa00a2471debd43f04b14226866942282655cb5236d`.
- Sole generic-assignment finding: line 8 (one-based), UTF-16 index 21275
  (zero-based), length 11, exact text `password:!0`. Inspection identifies the
  React input-type boolean map alongside email/month/number/range/search flags.
  This is a public boolean, not a configured credential. All other scanner
  patterns still execute; only this exact occurrence has a compiled exception.

Never update the fixture/hash automatically after a build. Any dependency, source,
formatting or toolchain change requires a new explicit artifact review. A match
must use the full raw bytes from the same bounded read as the scan. No workspace,
manifest, user or environment setting can authorize an exception. See ADR-0028.
