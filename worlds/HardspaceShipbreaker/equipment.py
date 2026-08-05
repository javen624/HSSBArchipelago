"""Hab equipment: shop locations are per-upgrade; AP items are Progressive (or single unlocks)."""

from __future__ import annotations

from BaseClasses import ItemClassification

BASE_ID = 2_026_080_100

# Hab shop locations — one check per equipment-screen upgrade.
# (hab_loc_name, loc_offset)
HAB_SHOP_LOCATIONS: list[tuple[str, int]] = [
    ("Hab: Unlock Tethers", 200),
    ("Hab: Grapple Strength 1", 201),
    ("Hab: Grapple Strength 2", 202),
    ("Hab: Grapple Strength 3", 203),
    ("Hab: Grapple Strength 4", 204),
    ("Hab: Grapple Strength 5", 205),
    ("Hab: Tethers Amount 1", 206),
    ("Hab: Tethers Amount 2", 207),
    ("Hab: Tethers Amount 3", 208),
    ("Hab: Tethers Lifetime 1", 209),
    ("Hab: Tethers Lifetime 2", 210),
    ("Hab: Unlock Demo Charge", 211),
    ("Hab: Purchase First Equipment Upgrade", 212),
    ("Hab: Scanner Objects", 213),
    ("Hab: Scanner Systems", 214),
    ("Hab: Suit Integrity 1", 215),
    ("Hab: Suit Integrity 2", 216),
    ("Hab: Cutter Heat 1", 217),
    ("Hab: Demo Charges Capacity 1", 218),
    ("Hab: Tethers Lifetime 3", 219),
    ("Hab: Tethers Lifetime 4", 220),
    ("Hab: Charged Push", 221),
    ("Hab: Scanner Range 1", 222),
    ("Hab: Scanner Range 2", 223),
    ("Hab: Scanner Range 3", 224),
    ("Hab: Scanner Range 4", 225),
    ("Hab: Scanner Range 5", 226),
    ("Hab: Suit Integrity 3", 227),
    ("Hab: Suit Integrity 4", 228),
    ("Hab: Suit Integrity 5", 229),
    ("Hab: Heat Resistance 1", 230),
    ("Hab: Heat Resistance 2", 231),
    ("Hab: Heat Resistance 3", 232),
    ("Hab: Heat Resistance 4", 233),
    ("Hab: Heat Resistance 5", 234),
    ("Hab: Cryo Resistance 1", 235),
    ("Hab: Cryo Resistance 2", 236),
    ("Hab: Cryo Resistance 3", 237),
    ("Hab: Cryo Resistance 4", 238),
    ("Hab: Cryo Resistance 5", 239),
    ("Hab: Electrical Resistance 1", 240),
    ("Hab: Electrical Resistance 2", 241),
    ("Hab: Electrical Resistance 3", 242),
    ("Hab: Electrical Resistance 4", 243),
    ("Hab: Electrical Resistance 5", 244),
    ("Hab: Cutter Heat Capacity 2", 245),
    ("Hab: Cutter Heat Capacity 3", 246),
    ("Hab: Cutter Cooldown 1", 247),
    ("Hab: Cutter Cooldown 2", 248),
    ("Hab: Cutter Cooldown 3", 249),
    ("Hab: Stinger Range 1", 250),
    ("Hab: Stinger Range 2", 251),
    ("Hab: Stinger Range 3", 252),
    ("Hab: Stinger Range 4", 253),
    ("Hab: Stinger Range 5", 254),
    ("Hab: Splitsaw Range 1", 255),
    ("Hab: Splitsaw Range 2", 256),
    ("Hab: Splitsaw Range 3", 257),
    ("Hab: Splitsaw Range 4", 258),
    ("Hab: Splitsaw Range 5", 259),
    ("Hab: Grapple Range 1", 260),
    ("Hab: Grapple Range 2", 261),
    ("Hab: Grapple Range 3", 262),
    ("Hab: Grapple Range 4", 263),
    ("Hab: Grapple Range 5", 264),
    ("Hab: Charged Push Force 1", 265),
    ("Hab: Charged Push Force 2", 266),
    ("Hab: Charged Push Force 3", 267),
    ("Hab: Demo Charges Capacity 2", 268),
    ("Hab: Demo Charges Capacity 3", 269),
    ("Hab: Demo Charges Capacity 4", 270),
    ("Hab: Demo Charges Capacity 5", 271),
    ("Hab: Demo Disarming 1", 272),
    ("Hab: Demo Disarming 2", 273),
    ("Hab: Demo Disarming 3", 274),
    ("Hab: Demo Self Cleanup 1", 275),
    ("Hab: Demo Self Cleanup 2", 276),
    ("Hab: Demo Self Cleanup 3", 277),
    ("Hab: Demo Auto-Deploy", 278),
    ("Hab: O2 Capacity 1", 279),
    ("Hab: O2 Capacity 2", 280),
    ("Hab: O2 Capacity 3", 281),
    ("Hab: O2 Capacity 4", 282),
    ("Hab: O2 Capacity 5", 283),
    ("Hab: O2 Recharge Module", 284),
    ("Hab: O2 Recharge 1", 285),
    ("Hab: O2 Recharge 2", 286),
    ("Hab: O2 Recharge 3", 287),
    ("Hab: Thruster Top Speed 1", 288),
    ("Hab: Thruster Top Speed 2", 289),
    ("Hab: Thruster Top Speed 3", 290),
    ("Hab: Thruster Braking 1", 291),
    ("Hab: Thruster Braking 2", 292),
    ("Hab: Thruster Braking 3", 293),
    ("Hab: Thruster Fuel Capacity 1", 294),
    ("Hab: Thruster Fuel Capacity 2", 295),
    ("Hab: Thruster Fuel Capacity 3", 296),
    ("Hab: Audio Resynth 1", 297),
    ("Hab: Audio Resynth 2", 298),
    ("Hab: Audio Resynth 3", 299),
    # Rental fees + durability (shop-sanity) — IDs 320+; do not renumber.
    ("Hab: Thruster Rental", 320),
    ("Hab: Thruster Durability 1", 321),
    ("Hab: Thruster Durability 2", 322),
    ("Hab: Thruster Durability 3", 323),
    ("Hab: Thruster Durability 4", 324),
    ("Hab: Thruster Durability 5", 325),
    ("Hab: Cutter Rental", 326),
    ("Hab: Cutter Durability 1", 327),
    ("Hab: Cutter Durability 2", 328),
    ("Hab: Cutter Durability 3", 329),
    ("Hab: Cutter Durability 4", 330),
    ("Hab: Cutter Durability 5", 331),
    ("Hab: Grapple Rental", 332),
    ("Hab: Grapple Durability 1", 333),
    ("Hab: Grapple Durability 2", 334),
    ("Hab: Grapple Durability 3", 335),
    ("Hab: Grapple Durability 4", 336),
    ("Hab: Grapple Durability 5", 337),
    ("Hab: Scanner Rental", 338),
    ("Hab: Scanner Durability 1", 339),
    ("Hab: Scanner Durability 2", 340),
    ("Hab: Scanner Durability 3", 341),
    ("Hab: Scanner Durability 4", 342),
    ("Hab: Scanner Durability 5", 343),
    ("Hab: Helmet Rental", 344),
    ("Hab: Suit Rental", 345),
    ("Hab: Suit Durability 1", 346),
    ("Hab: Suit Durability 2", 347),
    ("Hab: Suit Durability 3", 348),
    ("Hab: Demo Charge Rental", 349),
    ("Hab: Demo Durability 1", 350),
    ("Hab: Demo Durability 2", 351),
    ("Hab: Demo Durability 3", 352),
    ("Hab: Demo Durability 4", 353),
    ("Hab: Demo Durability 5", 354),
]

# Progressive AP items: (name, item_offset, count, classification, tier_label_prefix)
# Receiving copy N unlocks in-game tier N matching Hab catalog / client needles.
# Stable IDs: reuse classic progressive offsets where they existed.
PROGRESSIVE_EQUIPMENT: list[tuple[str, int, int, ItemClassification, str]] = [
    ("Progressive Grapple Strength", 2, 5, ItemClassification.progression, "Grapple Strength"),
    ("Progressive Tether Amount", 24, 3, ItemClassification.progression, "Tethers Amount"),
    ("Progressive Tether Lifetime", 27, 4, ItemClassification.useful, "Tethers Lifetime"),
    ("Progressive Scanner", 8, 2, ItemClassification.progression, "Scanner"),  # Objects then Systems
    ("Progressive Scanner Range", 33, 5, ItemClassification.useful, "Scanner Range"),
    ("Progressive Suit Integrity", 9, 5, ItemClassification.progression, "Suit Integrity"),
    ("Progressive Heat Resistance", 43, 5, ItemClassification.useful, "Heat Resistance"),
    ("Progressive Cryo Resistance", 48, 5, ItemClassification.useful, "Cryo Resistance"),
    ("Progressive Electrical Resistance", 53, 5, ItemClassification.useful, "Electrical Resistance"),
    ("Progressive Cutter Heat", 58, 3, ItemClassification.useful, "Cutter Heat Capacity"),
    ("Progressive Cutter Cooldown", 61, 3, ItemClassification.useful, "Cutter Cooldown"),
    ("Progressive Stinger Range", 64, 5, ItemClassification.useful, "Stinger Range"),
    ("Progressive Splitsaw Range", 69, 5, ItemClassification.useful, "Splitsaw Range"),
    ("Progressive Grapple Range", 74, 5, ItemClassification.useful, "Grapple Range"),
    ("Progressive Charged Push Force", 79, 3, ItemClassification.useful, "Charged Push Force"),
    ("Progressive Demo Charges", 15, 5, ItemClassification.progression, "Demo Charges Capacity"),
    ("Progressive Demo Disarming", 87, 3, ItemClassification.useful, "Demo Disarming"),
    ("Progressive Demo Self Cleanup", 92, 3, ItemClassification.useful, "Demo Self Cleanup"),
    ("Progressive O2 Capacity", 96, 5, ItemClassification.useful, "O2 Capacity"),
    ("Progressive O2 Recharge", 102, 3, ItemClassification.useful, "O2 Recharge"),
    ("Progressive Thruster Top Speed", 105, 3, ItemClassification.useful, "Thruster Top Speed"),
    ("Progressive Thruster Braking", 108, 3, ItemClassification.useful, "Thruster Braking"),
    ("Progressive Thruster Fuel", 111, 3, ItemClassification.useful, "Thruster Fuel Capacity"),
    ("Progressive Audio Resynth", 114, 3, ItemClassification.useful, "Audio Resynth"),
    ("Progressive Thruster Durability", 121, 5, ItemClassification.useful, "Thruster Durability"),
    ("Progressive Cutter Durability", 123, 5, ItemClassification.useful, "Cutter Durability"),
    ("Progressive Grapple Durability", 125, 5, ItemClassification.useful, "Grapple Durability"),
    ("Progressive Scanner Durability", 127, 5, ItemClassification.useful, "Scanner Durability"),
    ("Progressive Suit Durability", 130, 3, ItemClassification.useful, "Suit Durability"),
    ("Progressive Demo Durability", 132, 5, ItemClassification.useful, "Demo Durability"),
]

# Single (non-progressive) equipment unlocks / licenses.
# (name, item_offset, classification)
SINGLE_EQUIPMENT: list[tuple[str, int, ItemClassification]] = [
    ("Tether Module", 1, ItemClassification.progression),
    ("Demo Charge License", 7, ItemClassification.progression),
    ("Charged Push", 18, ItemClassification.progression),
    ("Demo Auto-Deploy", 95, ItemClassification.useful),
    ("O2 Recharge Module", 101, ItemClassification.progression),
    ("Thruster Rental", 120, ItemClassification.useful),
    ("Cutter Rental", 122, ItemClassification.useful),
    ("Grapple Rental", 124, ItemClassification.useful),
    ("Scanner Rental", 126, ItemClassification.useful),
    ("Helmet Rental", 128, ItemClassification.useful),
    ("Suit Rental", 129, ItemClassification.useful),
    ("Demo Charge Rental", 131, ItemClassification.useful),
]

# Hab shop location → license/unlock item that must be owned first.
# License shops themselves are omitted (only need equipment shop / PCR).
HAB_LICENSE_GATES: dict[str, str] = {}
for _loc, _ in HAB_SHOP_LOCATIONS:
    if _loc.startswith("Hab: Tethers Amount") or _loc.startswith("Hab: Tethers Lifetime"):
        HAB_LICENSE_GATES[_loc] = "Tether Module"
    elif _loc.startswith("Hab: Demo Charges") or _loc.startswith("Hab: Demo Disarming") \
            or _loc.startswith("Hab: Demo Self") or _loc == "Hab: Demo Auto-Deploy" \
            or _loc.startswith("Hab: Demo Durability") or _loc == "Hab: Demo Charge Rental":
        HAB_LICENSE_GATES[_loc] = "Demo Charge License"
    elif _loc.startswith("Hab: Charged Push Force"):
        HAB_LICENSE_GATES[_loc] = "Charged Push"
    elif _loc.startswith("Hab: O2 Recharge ") and _loc != "Hab: O2 Recharge Module":
        HAB_LICENSE_GATES[_loc] = "O2 Recharge Module"
    elif _loc == "Hab: Scanner Systems":
        # Mode unlock order: Objects before Systems (Progressive Scanner ×1).
        HAB_LICENSE_GATES[_loc] = "Progressive Scanner"

# Progressive / single AP items that must not apply until their license is owned.
ITEM_LICENSE_GATES: dict[str, str] = {
    "Progressive Tether Amount": "Tether Module",
    "Progressive Tether Lifetime": "Tether Module",
    "Progressive Tethers": "Tether Module",
    "Progressive Demo Charges": "Demo Charge License",
    "Progressive Demo Disarming": "Demo Charge License",
    "Progressive Demo Self Cleanup": "Demo Charge License",
    "Progressive Demo Durability": "Demo Charge License",
    "Demo Auto-Deploy": "Demo Charge License",
    "Demo Charge Rental": "Demo Charge License",
    "Progressive Charged Push Force": "Charged Push",
    "Progressive O2 Recharge": "O2 Recharge Module",
}

# Hab locations that ARE the license purchases (no prior license item required).
LICENSE_HAB_LOCATIONS = {
    "Hab: Unlock Tethers",
    "Hab: Unlock Demo Charge",
    "Hab: Charged Push",
    "Hab: O2 Recharge Module",
    "Hab: Scanner Objects",
}

# Legacy alias kept in ID map for old seeds (not placed in new pools).
LEGACY_PROGRESSIVE_ALIASES = {
    "Progressive Tethers": BASE_ID + 4,  # old combined tether progressive
    "Progressive Cutter": BASE_ID + 14,  # old combined cutter progressive
}


def equipment_location_name_to_id() -> dict[str, int]:
    return {name: BASE_ID + off for name, off in HAB_SHOP_LOCATIONS}


def equipment_item_name_to_id() -> dict[str, int]:
    out = {name: BASE_ID + off for name, off, _, _, _ in PROGRESSIVE_EQUIPMENT}
    out.update({name: BASE_ID + off for name, off, _ in SINGLE_EQUIPMENT})
    out.update(LEGACY_PROGRESSIVE_ALIASES)
    return out


def equipment_classifications() -> dict[str, ItemClassification]:
    out = {name: cls for name, _, _, cls, _ in PROGRESSIVE_EQUIPMENT}
    out.update({name: cls for name, _, cls in SINGLE_EQUIPMENT})
    out["Progressive Tethers"] = ItemClassification.progression
    out["Progressive Cutter"] = ItemClassification.useful
    return out


def equipment_item_counts() -> dict[str, int]:
    out = {name: count for name, _, count, _, _ in PROGRESSIVE_EQUIPMENT}
    out.update({name: 1 for name, _, _ in SINGLE_EQUIPMENT})
    return out


def all_hab_equipment_location_names() -> list[str]:
    return [name for name, _ in HAB_SHOP_LOCATIONS]


def progressive_tier_prefix(item_name: str) -> str | None:
    for name, _, _, _, prefix in PROGRESSIVE_EQUIPMENT:
        if name == item_name:
            return prefix
    return None
