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
    Sparse ≈ early Career slice + starter ship variants.
    Standard adds meta milestones, per-family salvage tiers, and Hab gear unlocks
    (not every hull variant; Hab rentals/durability omitted).
    Full = all Career checks, all variant salvage tiers, and full Hab shop-sanity.
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


class CreditPackValue(Choice):
    """
    How much LYNX debt each Credit Pack filler pays (Small / Medium / Large).
    low ≈ 250k / 1M / 2.5M — AP credits are a small nudge.
    normal ≈ 1M / 3M / 8M — default; salvage remains the main debt engine.
    high ≈ 5M / 20M / 40M — old generous values.
    """

    display_name = "Credit Pack Value"
    option_low = 0
    option_normal = 1
    option_high = 2
    default = option_normal


# Small / Medium / Large debt payments matching CreditPackValue.
CREDIT_PACK_AMOUNTS: dict[int, tuple[int, int, int]] = {
    CreditPackValue.option_low: (250_000, 1_000_000, 2_500_000),
    CreditPackValue.option_normal: (1_000_000, 3_000_000, 8_000_000),
    CreditPackValue.option_high: (5_000_000, 20_000_000, 40_000_000),
}


def credit_pack_amounts_for(option_value: int) -> tuple[int, int, int]:
    return CREDIT_PACK_AMOUNTS.get(int(option_value), CREDIT_PACK_AMOUNTS[CreditPackValue.option_normal])


@dataclass
class HardspaceShipbreakerOptions(PerGameCommonOptions):
    goal: Goal
    death_link: DeathLink
    hab_shop_sanity: HabShopSanity
    location_density: LocationDensity
    trap_percentage: TrapPercentage
    credit_pack_value: CreditPackValue


option_groups = [
    OptionGroup("Goal", [Goal]),
    OptionGroup("Locations", [LocationDensity, HabShopSanity]),
    OptionGroup("Multiplayer", [DeathLink]),
    OptionGroup("Items", [TrapPercentage, CreditPackValue]),
]

option_presets = {
    "phase1_smoke": {
        "goal": Goal.option_debt_payoff,
        "death_link": False,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_sparse,
        "trap_percentage": 0,
        "credit_pack_value": CreditPackValue.option_normal,
    },
    "standard": {
        "goal": Goal.option_debt_payoff,
        "death_link": False,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_standard,
        "trap_percentage": 5,
        "credit_pack_value": CreditPackValue.option_normal,
    },
    "death_link": {
        "goal": Goal.option_debt_payoff,
        "death_link": True,
        "hab_shop_sanity": True,
        "location_density": LocationDensity.option_standard,
        "trap_percentage": 5,
        "credit_pack_value": CreditPackValue.option_normal,
    },
}
