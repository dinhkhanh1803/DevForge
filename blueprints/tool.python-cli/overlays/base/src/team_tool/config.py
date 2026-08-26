import os
from collections.abc import Mapping
from dataclasses import dataclass

_VALID_LOG_LEVELS = frozenset({"CRITICAL", "ERROR", "WARNING", "INFO", "DEBUG"})


@dataclass(frozen=True, slots=True)
class AppConfig:
    log_level: str

    @classmethod
    def from_environment(cls, environment: Mapping[str, str] | None = None) -> AppConfig:
        source = os.environ if environment is None else environment
        log_level = source.get("TEAM_TOOL_LOG_LEVEL", "INFO").strip().upper() or "INFO"
        if log_level not in _VALID_LOG_LEVELS:
            raise ValueError("TEAM_TOOL_LOG_LEVEL must be a supported standard logging level.")
        return cls(log_level=log_level)
