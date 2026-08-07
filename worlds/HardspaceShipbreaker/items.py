from __future__ import annotations

from typing import TYPE_CHECKING

from BaseClasses import Item, ItemClassification

from . import equipment

if TYPE_CHECKING:
    from .world import HardspaceShipbreakerWorld

BASE_ID = equipment.BASE_ID

ITEM_NAME_TO_ID = {
    # Legacy ship unlocks (ID aliases for old seeds — not placed; client ignores.
    # Ship families use Progressive Certification Rank + vanilla cert only.)
    "Progressive Ship Unlock": BASE_ID + 19,
    "Unlock Atlas": BASE_ID + 3,
    "Unlock Javelin": BASE_ID + 5,
    "Unlock Gecko": BASE_ID + 6,
    "LYNX Token Pack (Small)": BASE_ID + 11,
    "LYNX Token Pack (Medium)": BASE_ID + 13,
    "Credit Pack (Small)": BASE_ID + 10,
    "Credit Pack (Medium)": BASE_ID + 12,
    "Credit Pack (Large)": BASE_ID + 20,
    "Progressive Certification Rank": BASE_ID + 16,
    "Unlock Mackerel": BASE_ID + 17,
    "Clone Fee Tax": BASE_ID + 91,
    # Legacy ID kept for old seeds (not placed).
    "Nothing": BASE_ID + 90,
}
ITEM_NAME_TO_ID.update(equipment.equipment_item_name_to_id())

DEFAULT_ITEM_CLASSIFICATIONS = {
    "Progressive Ship Unlock": ItemClassification.progression,
    "Unlock Atlas": ItemClassification.progression,
    "Unlock Javelin": ItemClassification.progression,
    "Unlock Gecko": ItemClassification.progression,
    "LYNX Token Pack (Small)": ItemClassification.filler,
    "LYNX Token Pack (Medium)": ItemClassification.filler,
    "Credit Pack (Small)": ItemClassification.filler,
    "Credit Pack (Medium)": ItemClassification.filler,
    "Credit Pack (Large)": ItemClassification.filler,
    "Progressive Certification Rank": ItemClassification.progression,
    "Unlock Mackerel": ItemClassification.progression,
    "Clone Fee Tax": ItemClassification.trap,
    "Nothing": ItemClassification.filler,
}
DEFAULT_ITEM_CLASSIFICATIONS.update(equipment.equipment_classifications())

PROGRESSION_ITEM_COUNTS = {
    "Progressive Certification Rank": 4,
}
PROGRESSION_ITEM_COUNTS.update(equipment.equipment_item_counts())

FILLER_NAMES = [
    "LYNX Token Pack (Small)",
    "LYNX Token Pack (Medium)",
    "Credit Pack (Small)",
    "Credit Pack (Medium)",
    "Credit Pack (Large)",
]

TRAP_NAMES = [
    "Clone Fee Tax",
]


class HardspaceShipbreakerItem(Item):
    game = "Hardspace Shipbreaker"


def get_filler_item_name(world: HardspaceShipbreakerWorld) -> str:
    # Credit pack debt amounts come from options.credit_pack_value (default 1M / 3M / 8M).
    weights = [
        ("LYNX Token Pack (Small)", 3),
        ("LYNX Token Pack (Medium)", 1),
        ("Credit Pack (Small)", 4),
        ("Credit Pack (Medium)", 2),
        ("Credit Pack (Large)", 1),
    ]
    names: list[str] = []
    for name, w in weights:
        names.extend([name] * w)
    return world.random.choice(names)


def create_item_with_correct_classification(
    world: HardspaceShipbreakerWorld, name: str
) -> HardspaceShipbreakerItem:
    classification = DEFAULT_ITEM_CLASSIFICATIONS[name]
    return HardspaceShipbreakerItem(name, classification, ITEM_NAME_TO_ID[name], world.player)


def create_all_items(world: HardspaceShipbreakerWorld) -> None:
    """
    Place all true progression items, then as many useful equipment items as fit,
    then traps/filler to match unfilled location count.
    Useful gear is truncated when density/shop-sanity yields fewer locations than
    the full Hab equipment set (e.g. sparse, or shop-sanity off).
    """
    unfilled = len(world.multiworld.get_unfilled_locations(world.player))

    required: list[Item] = []
    useful: list[Item] = []
    for name, count in PROGRESSION_ITEM_COUNTS.items():
        classification = DEFAULT_ITEM_CLASSIFICATIONS[name]
        bucket = required if bool(classification & ItemClassification.progression) else useful
        for _ in range(count):
            bucket.append(world.create_item(name))

    if len(required) > unfilled:
        raise Exception(
            f"Hardspace Shipbreaker: {len(required)} progression items exceed "
            f"{unfilled} locations — reduce equipment progression or raise density."
        )

    world.random.shuffle(useful)
    room_for_useful = unfilled - len(required)
    itempool: list[Item] = required + useful[:room_for_useful]
    needed = unfilled - len(itempool)

    trap_weight = int(world.options.trap_percentage.value)
    trap_count = 0
    if needed > 0 and trap_weight > 0:
        trap_count = max(0, min(needed // 4, needed * trap_weight // 100))
        itempool += [world.create_item(world.random.choice(TRAP_NAMES)) for _ in range(trap_count)]
        needed -= trap_count

    itempool += [world.create_filler() for _ in range(max(0, needed))]
    world.multiworld.itempool += itempool
