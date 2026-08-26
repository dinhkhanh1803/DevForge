import pytest

from team_tool.config import AppConfig


def test_environment_defaults_and_normalizes_log_level() -> None:
    assert AppConfig.from_environment({}).log_level == "INFO"
    assert AppConfig.from_environment({"TEAM_TOOL_LOG_LEVEL": " debug "}).log_level == "DEBUG"


def test_environment_rejects_unknown_log_level() -> None:
    with pytest.raises(ValueError, match="supported standard logging level"):
        AppConfig.from_environment({"TEAM_TOOL_LOG_LEVEL": "verbose"})
