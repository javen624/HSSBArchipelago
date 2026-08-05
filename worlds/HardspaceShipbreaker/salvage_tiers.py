"""Per-family and per-variant salvage goal tiers (1–5) as AP locations."""

from __future__ import annotations

from .items import BASE_ID

# Hull families with Career salvage tiers.
SHIP_FAMILIES = ("Mackerel", "Atlas", "Javelin", "Gecko")

# Type variants (role / hull config). These are the ship-progress checks (Clear-* hull
# clears were removed; use Salvage Tier 1–5 per family/variant instead).
# (family, variant_label) — variant_label is used in location names and client matching.
SHIP_VARIANTS: tuple[tuple[str, str], ...] = (
    ("Mackerel", "Light Cargo"),
    ("Mackerel", "Station Hopper"),
    ("Mackerel", "Exolab"),
    ("Mackerel", "Heavy Cargo"),
    ("Atlas", "Scout"),
    ("Atlas", "Nomad"),
    ("Atlas", "Roustabout"),
    ("Javelin", "Small Refueling"),
    ("Javelin", "Small Heavy Cargo"),
    ("Javelin", "Medium Refueling"),
    ("Javelin", "Medium Heavy Cargo"),
    ("Javelin", "Large Refueling"),
    ("Gecko", "Station Hopper"),
    ("Gecko", "Heavy Cargo"),
    ("Gecko", "Stargazer"),
    ("Gecko", "Salvage Runner"),
)

# Family location IDs: BASE_ID + 300 … 319 (20 locs). Do not renumber.
# Mackerel 300-304, Atlas 305-309, Javelin 310-314, Gecko 315-319.
_FAMILY_TIER_BASE = 300

# Variant location IDs: BASE_ID + 350 … 349+80 (16×5). Do not renumber.
_VARIANT_TIER_BASE = 350


def salvage_tier_location_name(family: str, tier: int) -> str:
    return f"{family} Salvage Tier {tier}"


def variant_salvage_tier_location_name(family: str, variant: str, tier: int) -> str:
    return f"{family} {variant} Salvage Tier {tier}"


def salvage_tier_location_name_to_id() -> dict[str, int]:
    out: dict[str, int] = {}
    for fi, family in enumerate(SHIP_FAMILIES):
        for tier in range(1, 6):
            offset = _FAMILY_TIER_BASE + fi * 5 + (tier - 1)
            out[salvage_tier_location_name(family, tier)] = BASE_ID + offset

    for vi, (family, variant) in enumerate(SHIP_VARIANTS):
        for tier in range(1, 6):
            offset = _VARIANT_TIER_BASE + vi * 5 + (tier - 1)
            out[variant_salvage_tier_location_name(family, variant, tier)] = BASE_ID + offset
    return out


def all_salvage_tier_location_names() -> list[str]:
    return list(salvage_tier_location_name_to_id().keys())


def family_salvage_tier_location_names() -> list[str]:
    names: list[str] = []
    for family in SHIP_FAMILIES:
        names.extend(salvage_tier_names_for_family(family))
    return names


def salvage_tier_names_for_family(family: str) -> list[str]:
    return [salvage_tier_location_name(family, t) for t in range(1, 6)]


def salvage_tier_names_for_variant(family: str, variant: str) -> list[str]:
    return [variant_salvage_tier_location_name(family, variant, t) for t in range(1, 6)]


def all_variant_salvage_tier_location_names() -> list[str]:
    names: list[str] = []
    for family, variant in SHIP_VARIANTS:
        names.extend(salvage_tier_names_for_variant(family, variant))
    return names


def variant_salvage_tier_names_for_family(family: str) -> list[str]:
    names: list[str] = []
    for fam, variant in SHIP_VARIANTS:
        if fam == family:
            names.extend(salvage_tier_names_for_variant(fam, variant))
    return names


def variant_index(family: str, variant: str) -> int:
    for i, (fam, var) in enumerate(SHIP_VARIANTS):
        if fam == family and var == variant:
            return i
    raise KeyError(f"{family} / {variant}")


def variant_location_id(family: str, variant: str, tier: int) -> int:
    return BASE_ID + _VARIANT_TIER_BASE + variant_index(family, variant) * 5 + (tier - 1)
