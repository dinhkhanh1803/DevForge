from team_tool.model import StatusModel


def test_initial_status() -> None:
    assert StatusModel().text == "Ready"


def test_refresh_increments_visible_status() -> None:
    model = StatusModel()
    model.refresh()
    assert model.text == "Ready - refresh 1"
    model.refresh()
    assert model.text == "Ready - refresh 2"


def test_instances_do_not_share_state() -> None:
    first = StatusModel()
    first.refresh()
    assert StatusModel().text == "Ready"
