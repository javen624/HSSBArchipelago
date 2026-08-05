from __future__ import annotations

from typing import TYPE_CHECKING

from BaseClasses import ItemClassification, Location

from . import equipment
from . import salvage_tiers
from .items import BASE_ID, HardspaceShipbreakerItem
from .options import Goal, LocationDensity

if TYPE_CHECKING:
    from .world import HardspaceShipbreakerWorld

# Stable IDs — never renumber existing offsets.
# Reserved (dropped; do not reuse):
#   +120/158–160 Clear Mackerel Light Cargo / Station Hopper / Exolab / Heavy Cargo
#   +126/127/177 Clear Atlas Scout / Nomad / Roustabout
#   +128/179/180 Clear Javelin Small Refueling / Small Heavy Cargo / Medium Refueling
#   +129/181/182 Clear Gecko Station Hopper / Heavy Cargo / Stargazer
#   +150/151 Reduce Debt Below 50%/25%
#   +153/154 Earn 100/500 LYNX Tokens Lifetime
#   +155 Furnace Aluminum Panel
#   +161–166 Clear Ship Grade 2/3/5/6/8/10
#   +167–173 Class II Reactor, Thruster II, Atmosphere Regulator, Sensor Array,
#            Power Generator, Shipping Crate, Nacelle
#   +174–176 Flush a Fuel Line, Disarm/Cut with Demo Charge
#   +178 Stabilize Class II Reactor
#   +184 Clear Hazard Level 7 Ship
# Ship progress uses family/variant Salvage Tier 1–5 instead of Clear-* hull clears.
LOCATION_NAME_TO_ID = {
    # Hab / meta
    "Finish Basic Training": BASE_ID + 100,
    "Complete First Shift": BASE_ID + 101,
    "Reach Certification Rank 5": BASE_ID + 139,
    "Reach Certification Rank 10": BASE_ID + 140,
    "Reach Certification Rank 15": BASE_ID + 146,
    "Reach Certification Rank 20": BASE_ID + 147,
    "Recover First Data Drive": BASE_ID + 141,
    "Recover 3 Data Drives": BASE_ID + 148,
    "Recover 5 Data Drives": BASE_ID + 149,
    "Survive First Clone": BASE_ID + 152,
    # Bay basics
    "First Barge Deposit": BASE_ID + 110,
    "First Processor Deposit": BASE_ID + 111,
    "First Furnace Deposit": BASE_ID + 112,
    "Process Aluminum Structure": BASE_ID + 113,
    "Furnace Glass": BASE_ID + 114,
    "Process Titanium Structure": BASE_ID + 135,
    "Process Nanocarbon Panel": BASE_ID + 136,
    "Clear Ship Grade 1": BASE_ID + 121,
    "Clear Ship Grade 4": BASE_ID + 137,
    "Clear Ship Grade 7": BASE_ID + 138,
    "Salvage Fuel Tank": BASE_ID + 122,
    "Place First Tether": BASE_ID + 123,
    "Salvage Power Cell": BASE_ID + 124,
    "Salvage Class I Reactor": BASE_ID + 125,
    "Salvage Thruster Class I": BASE_ID + 130,
    "Salvage Coolant Tank": BASE_ID + 133,
    "Salvage Airlock": BASE_ID + 134,
    "Salvage Computer Terminal": BASE_ID + 143,
    "Salvage Communications Array": BASE_ID + 144,
    "Salvage Airlock Console": BASE_ID + 145,
    "Salvage Quasar Thruster": BASE_ID + 131,
    "Salvage ECU": BASE_ID + 132,
    "Clear a Ghost Ship": BASE_ID + 183,
}
LOCATION_NAME_TO_ID.update(equipment.equipment_location_name_to_id())
LOCATION_NAME_TO_ID.update(salvage_tiers.salvage_tier_location_name_to_id())

# Density buckets — sparse ⊂ standard; full == all LOCATION_NAME_TO_ID (== standard today).
SPARSE_LOCATIONS = {
    "Finish Basic Training",
    "Complete First Shift",
    "Reach Certification Rank 5",
    "Reach Certification Rank 10",
    "Recover First Data Drive",
    "First Barge Deposit",
    "First Processor Deposit",
    "First Furnace Deposit",
    "Process Aluminum Structure",
    "Furnace Glass",
    "Process Titanium Structure",
    "Process Nanocarbon Panel",
    "Clear Ship Grade 1",
    "Clear Ship Grade 4",
    "Clear Ship Grade 7",
    "Salvage Fuel Tank",
    "Place First Tether",
    "Salvage Power Cell",
    "Salvage Class I Reactor",
    "Salvage Thruster Class I",
    "Salvage Coolant Tank",
    "Salvage Airlock",
    "Salvage Computer Terminal",
    "Salvage Communications Array",
    "Salvage Airlock Console",
    "Salvage Quasar Thruster",
    "Salvage ECU",
} | set(salvage_tiers.salvage_tier_names_for_family("Mackerel")) | set(
    salvage_tiers.salvage_tier_names_for_variant("Mackerel", "Light Cargo")
) | set(salvage_tiers.salvage_tier_names_for_variant("Atlas", "Scout")) | set(
    salvage_tiers.salvage_tier_names_for_variant("Javelin", "Small Refueling")
) | set(salvage_tiers.salvage_tier_names_for_variant("Gecko", "Station Hopper"))

STANDARD_EXTRA = {
    "Reach Certification Rank 15",
    "Reach Certification Rank 20",
    "Recover 3 Data Drives",
    "Recover 5 Data Drives",
    "Survive First Clone",
    "Clear a Ghost Ship",
} | set(salvage_tiers.all_salvage_tier_location_names())

SHOP_LOCATIONS = set(equipment.all_hab_equipment_location_names())

HAB_LOCS = {
    "Finish Basic Training",
    "Reach Certification Rank 5",
    "Reach Certification Rank 10",
    "Reach Certification Rank 15",
    "Reach Certification Rank 20",
    "Recover First Data Drive",
    "Recover 3 Data Drives",
    "Recover 5 Data Drives",
    "Survive First Clone",
} | SHOP_LOCATIONS

BAY_LOCS = {
    "Complete First Shift",
    "First Barge Deposit",
    "First Processor Deposit",
    "First Furnace Deposit",
    "Process Aluminum Structure",
    "Furnace Glass",
    "Place First Tether",
    "Process Titanium Structure",
    "Process Nanocarbon Panel",
    "Clear Ship Grade 1",
    "Clear Ship Grade 4",
    "Clear Ship Grade 7",
    "Clear a Ghost Ship",
}

MACKEREL_LOCS = {
    "Salvage Fuel Tank",
    "Salvage Power Cell",
    "Salvage Coolant Tank",
    "Salvage Airlock",
    "Salvage Computer Terminal",
    "Salvage Communications Array",
    "Salvage Airlock Console",
} | set(salvage_tiers.salvage_tier_names_for_family("Mackerel")) | set(
    salvage_tiers.variant_salvage_tier_names_for_family("Mackerel")
)

REACTOR_I_LOCS = {
    "Salvage Class I Reactor",
    "Salvage Thruster Class I",
}

REACTOR_II_LOCS = {
    "Salvage ECU",
}

ATLAS_LOCS = {
    "Salvage Quasar Thruster",
} | set(salvage_tiers.salvage_tier_names_for_family("Atlas")) | set(
    salvage_tiers.variant_salvage_tier_names_for_family("Atlas")
)

JAVELIN_LOCS = set(salvage_tiers.salvage_tier_names_for_family("Javelin")) | set(
    salvage_tiers.variant_salvage_tier_names_for_family("Javelin")
)

GECKO_LOCS = set(salvage_tiers.salvage_tier_names_for_family("Gecko")) | set(
    salvage_tiers.variant_salvage_tier_names_for_family("Gecko")
)


class HardspaceShipbreakerLocation(Location):
    game = "Hardspace Shipbreaker"


def active_location_names(world: HardspaceShipbreakerWorld) -> set[str]:
    density = world.options.location_density
    if density == LocationDensity.option_sparse:
        names = set(SPARSE_LOCATIONS)
    elif density == LocationDensity.option_full:
        names = set(LOCATION_NAME_TO_ID.keys())
    else:
        names = set(SPARSE_LOCATIONS) | set(STANDARD_EXTRA)

    if bool(world.options.hab_shop_sanity):
        if density == LocationDensity.option_sparse:
            names |= {
                "Hab: Unlock Tethers",
                "Hab: Grapple Strength 1",
                "Hab: Grapple Strength 2",
                "Hab: Grapple Strength 3",
                "Hab: Unlock Demo Charge",
                "Hab: Purchase First Equipment Upgrade",
                "Hab: Tethers Amount 1",
                "Hab: Tethers Amount 2",
            }
        else:
            names |= set(SHOP_LOCATIONS)
    else:
        names -= set(SHOP_LOCATIONS)

    return names


def _add(region, names: set[str], active: set[str]) -> None:
    mapping = {n: LOCATION_NAME_TO_ID[n] for n in names if n in active}
    if mapping:
        region.add_locations(mapping, HardspaceShipbreakerLocation)


def create_all_locations(world: HardspaceShipbreakerWorld) -> None:
    active = active_location_names(world)
    _add(world.get_region("Hab"), HAB_LOCS, active)
    _add(world.get_region("Bay"), BAY_LOCS, active)
    _add(world.get_region("Mackerel"), MACKEREL_LOCS, active)
    _add(world.get_region("Reactor_I"), REACTOR_I_LOCS, active)
    _add(world.get_region("Reactor_II"), REACTOR_II_LOCS, active)
    _add(world.get_region("Atlas"), ATLAS_LOCS, active)
    _add(world.get_region("Javelin"), JAVELIN_LOCS, active)
    _add(world.get_region("Gecko"), GECKO_LOCS, active)

    goal_region = world.get_region("Goal")
    if world.options.goal == Goal.option_atlas_scout:
        event_name = "Atlas Scout Cleared"
    elif world.options.goal == Goal.option_rank_20:
        event_name = "Rank 20 Reached"
    else:
        event_name = "Debt Paid"
    victory_location = HardspaceShipbreakerLocation(world.player, event_name, None, goal_region)
    goal_region.locations.append(victory_location)
    victory_location.place_locked_item(
        HardspaceShipbreakerItem("Victory", ItemClassification.progression, None, world.player)
    )
