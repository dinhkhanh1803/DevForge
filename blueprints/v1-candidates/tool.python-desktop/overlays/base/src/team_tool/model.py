from dataclasses import dataclass


@dataclass
class StatusModel:
    refresh_count: int = 0

    @property
    def text(self) -> str:
        return "Ready" if self.refresh_count == 0 else f"Ready - refresh {self.refresh_count}"

    def refresh(self) -> None:
        self.refresh_count += 1
