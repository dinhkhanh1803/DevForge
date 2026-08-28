import pytest

from team_tool.desktop import DesktopView
from team_tool.desktop_cli import main


def test_native_smoke_opens_refreshes_and_closes() -> None:
    assert main(["--smoke-test"]) == 0


def test_unknown_arguments_are_rejected() -> None:
    with pytest.raises(SystemExit) as failure:
        main(["--unknown"])
    assert failure.value.code == 2


def test_help_does_not_start_gui() -> None:
    with pytest.raises(SystemExit) as result:
        main(["--help"])
    assert result.value.code == 0


def test_callback_failure_after_updating_state_fails_smoke(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    original = DesktopView.refresh

    def broken_refresh(view: DesktopView) -> None:
        original(view)
        raise RuntimeError("callback failed")

    monkeypatch.setattr(DesktopView, "refresh", broken_refresh)
    assert main(["--smoke-test"]) == 1
