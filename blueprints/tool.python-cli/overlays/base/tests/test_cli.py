import pytest

from team_tool.cli import main


def test_help_exits_successfully(capsys: pytest.CaptureFixture[str]) -> None:
    with pytest.raises(SystemExit) as error:
        main(["--help"])

    assert error.value.code == 0
    assert "A production Python CLI" in capsys.readouterr().out


def test_main_returns_success(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("TEAM_TOOL_LOG_LEVEL", raising=False)

    assert main([]) == 0
