# Architecture

`src/team_tool/cli.py` owns argument parsing, `config.py` validates public process configuration, and `logging_config.py` owns standard-library logging setup. The package exposes the fixed `team-tool` console entrypoint. Tests mirror behavior without importing from repository-relative paths because uv installs the package into the project environment.
