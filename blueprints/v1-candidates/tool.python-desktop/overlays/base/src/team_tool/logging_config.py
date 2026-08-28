import logging.config

from team_tool.config import AppConfig


def configure_logging(config: AppConfig) -> None:
    logging.config.dictConfig(
        {
            "version": 1,
            "disable_existing_loggers": False,
            "formatters": {
                "standard": {
                    "format": "%(asctime)s %(levelname)s %(name)s %(message)s",
                },
            },
            "handlers": {
                "console": {
                    "class": "logging.StreamHandler",
                    "formatter": "standard",
                    "level": config.log_level,
                },
            },
            "root": {
                "handlers": ["console"],
                "level": config.log_level,
            },
        }
    )
