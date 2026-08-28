import argparse
import logging
import tkinter as tk
from collections.abc import Sequence
from contextlib import suppress
from types import TracebackType

from team_tool.config import AppConfig
from team_tool.desktop import DesktopView
from team_tool.logging_config import configure_logging
from team_tool.model import StatusModel


def run_desktop(smoke: bool) -> int:
    root = tk.Tk()
    result = 1 if smoke else 0
    callback_failed = False

    def callback_failure(
        _kind: type[BaseException],
        _value: BaseException,
        _traceback: TracebackType | None,
    ) -> None:
        nonlocal result, callback_failed
        result = 1
        callback_failed = True
        logging.getLogger(__name__).error("Desktop action failed.")
        root.after_idle(root.destroy)

    root.report_callback_exception = callback_failure
    root.title("Team Desktop")
    root.minsize(360, 180)
    view = DesktopView(root, StatusModel())

    def smoke_check() -> None:
        nonlocal result
        try:
            before = view.status.get()
            view.refresh_button.focus_force()
            view.refresh_button.invoke()
            if (
                not callback_failed
                and root.winfo_viewable()
                and view.status.get() != before
                and view.status.get() == view.model.text
                and root.focus_get() == view.refresh_button
            ):
                result = 0
        finally:
            root.destroy()

    try:
        if smoke:
            root.after(150, smoke_check)
            root.after(5000, root.destroy)
        else:
            view.refresh_button.focus_set()
        root.mainloop()
        return result
    finally:
        # The user-close path already destroyed Tcl widgets.
        with suppress(tk.TclError):
            root.destroy()


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="team-desktop", description="Native Team Desktop tool.")
    parser.add_argument("--smoke-test", action="store_true", help="Verify native refresh and exit.")
    args = parser.parse_args(argv)
    try:
        configure_logging(AppConfig.from_environment())
        return run_desktop(args.smoke_test)
    except tk.TclError, ValueError:
        logging.getLogger(__name__).error(
            "Desktop startup failed; verify Python Tcl/Tk and config."
        )
        return 1
