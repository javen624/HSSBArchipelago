from dataclasses import dataclass

from Options import Choice, OptionGroup, PerGameCommonOptions, Range, Toggle


class Goal(Choice):
    """
    How the player completes their seed.
    atlas_scout = reach Atlas Scout Salvage Tier 5 (not a bay Clear-* location).
    """

    display_name = "Goal"
    option_debt_payoff = 0
    option_atlas_scout = 1
    option_rank_20 = 2
    default = option_debt_payoff


class DeathLink(Toggle):
    """Player death (clone) sends and receives Death Link."""

    display_name = "Death Link"
    default = False


class HabShopSanity(Toggle):
    """
    Hab equipment upgrades are locations. Buying them sends checks (can contain other items).
    Receiving AP tool items still applies the upgrade in-game.
    """

    display_name = "Hab Shop Sanity"
    default = True


class LocationDensity(Choice):
    """
    How many locations to include.
    Sparse ≈ early Career slice.
    Standard and full are the same: the full implemented pool (Hab shop +
    salvage tiers + remaining Career checks). The old ~120 research draft is not used.
    """

    display_name = "Location Density"
    option_sparse = 0
    option_standard = 1
    option_full = 2
    default = option_standard


class TrapPercentage(Range):
    """Percent of remaining filler slots that become traps (0–25)."""

    display_name = "Trap Percentage"
    range_start = 0
    range_end = 25
    default = 0


@dataclass
class HardspaceShipbreakerOptions(PerGameCommonOptions):
    goal: Goal
    death_link: DeathLink
    hab_shop_sanity: HabShopSanity
    location_density: LocationDensity
    trap_percentage: TrapPercentage


option_groups = [
    OptionGroup("Goal", [Goal]),
    OptionGroup("Locations", [LocationDensity, HabShopSanity]),
    OptionGroup("Multiplayer", [DeathLink]),
    OptionGroup("Items", [TrapPercentage]),
]

option_presets = {
    "phase1_smoke": {
        "goal": Goal.option_debt_payoff,
        "death_link": False,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_sparse,
        "trap_percentage": 0,
    },
    "standard": {
        "goal": Goal.option_debt_payoff,
        "death_link": False,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_standard,
        "trap_percentage": 5,
    },
    "death_link": {
        "goal": Goal.option_debt_payoff,
        "death_link": True,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_standard,
        "trap_percentage": 5,
    },
}
