import tkinter as tk
from tkinter import ttk

from team_tool.model import StatusModel


class DesktopView(ttk.Frame):
    def __init__(self, root: tk.Tk, model: StatusModel) -> None:
        super().__init__(root, padding=20)
        self.model = model
        self.status = tk.StringVar(master=root, value=model.text)
        self.grid(sticky="nsew")
        root.columnconfigure(0, weight=1)
        root.rowconfigure(0, weight=1)
        self.columnconfigure(0, weight=1)
        ttk.Label(self, text="Team Desktop", font=("Segoe UI", 16)).grid(sticky="w")
        ttk.Label(self, textvariable=self.status).grid(row=1, sticky="w", pady=12)
        self.refresh_button = ttk.Button(self, text="Refresh status", command=self.refresh)
        self.refresh_button.grid(row=2, sticky="w")
        root.bind("<Alt-r>", lambda _event: self.refresh())
        root.protocol("WM_DELETE_WINDOW", root.destroy)

    def refresh(self) -> None:
        self.model.refresh()
        self.status.set(self.model.text)
