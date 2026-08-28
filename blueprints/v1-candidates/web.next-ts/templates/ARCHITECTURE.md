# ARCHITECTURE

## Boundaries

src/app owns routes, layout and global styles. src/lib/environment.ts validates
only the public site name and never echoes invalid values. The health route
returns a fixed typed JSON shape with no infrastructure dependencies.

## Execution

DevForge validates a source-verified tooling copy. node_modules, .next and caches
are not handed off; build evidence is bound to the generation report. Install and
build again at the final path. Local server state is not a portable artifact.
