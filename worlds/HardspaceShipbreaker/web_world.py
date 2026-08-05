from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld

from .options import option_groups, option_presets


class HardspaceShipbreakerWebWorld(WebWorld):
    theme = "ocean"
    # Prefer GitHub Issues when the project repo is public; see docs/BUG_REPORT.md.
    bug_report_page = "https://github.com/HardspaceArchipelago/HardspaceArchipelgao/issues"
    tutorials = [
        Tutorial(
            "Multiworld Setup Guide",
            "A guide to setting up Hardspace: Shipbreaker for Archipelago.",
            "English",
            "setup_en.md",
            "setup/en",
            ["HardspaceArchipelago"],
        )
    ]
    option_groups = option_groups
    options_presets = option_presets
