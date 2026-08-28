# DEVELOPMENT

## Toolchain

Use Node 22.23.2 and pnpm 10.24.0. Do not use an unreviewed global upgrade or run as
Administrator. Install with pnpm install --frozen-lockfile --ignore-scripts.

## Environment

.env.example documents NEXT_PUBLIC_SITE_NAME. It is public content, never a secret.
Missing configuration uses Team Portal; invalid names fail with a generic error.
DevForge execution does not inherit the developer's .env or credentials.

## Local work

pnpm run dev starts a loopback-only development server. pnpm run format applies
Prettier. next-env.d.ts follows the pinned Next generator and is not hand-edited.
