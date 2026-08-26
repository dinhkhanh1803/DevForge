# Python CLI blueprint

This built-in package emits a static, locked Python CLI project. Generation never invokes a project scaffolder. Dependency synchronization uses uv frozen mode without ambient configuration, followed by fixed formatting, lint, typecheck, test, package-build, and CLI-help validators.
