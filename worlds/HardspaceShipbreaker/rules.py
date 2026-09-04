from __future__ import annotations

from typing import TYPE_CHECKING

from . import equipment
from .options import Goal

if TYPE_CHECKING:
    from .world import HardspaceShipbreakerWorld


def _has_loc(world: HardspaceShipbreakerWorld, name: str) -> bool:
    try:
        world.get_location(name)
        return True
    except KeyError:
        return False


def _set(world: HardspaceShipbreakerWorld, name: str, rule) -> None:
    if _has_loc(world, name):
        world.get_location(name).access_rule = rule


def _has_atlas(state, player: int) -> bool:
    # Atlas Scout ~rank 4–5 → Progressive Cert Rank ×1 (ceiling 9). Legacy unlocks still count.
    return (
        state.has("Progressive Certification Rank", player, 1)
        or state.has("Progressive Ship Unlock", player, 1)
        or state.has("Unlock Atlas", player)
    )


def _has_javelin(state, player: int) -> bool:
    # Small Javelin ~rank 5–7 → same PCR ×1 band.
    return (
        state.has("Progressive Certification Rank", player, 1)
        or state.has("Progressive Ship Unlock", player, 2)
        or state.has("Unlock Javelin", player)
    )


def _has_gecko(state, player: int) -> bool:
    # Gecko Station Hopper ~rank 14 → Progressive Cert Rank ×2 (ceiling 14).
    return (
        state.has("Progressive Certification Rank", player, 2)
        or state.has("Progressive Ship Unlock", player, 3)
        or state.has("Unlock Gecko", player)
    )


def _has_tether(state, player: int) -> bool:
    return (
        state.has("Tether Module", player)
        or state.has("Progressive Tether Amount", player)
        or state.has("Progressive Tether Lifetime", player)
        or state.has("Progressive Tethers", player)
    )


def _has_demo(state, player: int) -> bool:
    return state.has("Demo Charge License", player) or state.has("Progressive Demo Charges", player)


def _has_charged_push(state, player: int) -> bool:
    return state.has("Charged Push", player) or state.has("Progressive Charged Push Force", player)


def _has_launcher(state, player: int) -> bool:
    return state.has("Unlock Launcher", player) or state.has("Progressive Launcher Range", player)


def _has_license_item(state, player: int, lic: str | None) -> bool:
    if lic is None:
        return True
    if lic == "Progressive Scanner":
        return state.has("Progressive Scanner", player)
    if lic == "Tether Module":
        return _has_tether(state, player)
    if lic == "Demo Charge License":
        return _has_demo(state, player)
    if lic == "Charged Push":
        return _has_charged_push(state, player)
    if lic == "Unlock Launcher":
        return _has_launcher(state, player)
    return state.has(lic, player)


def set_all_rules(world: HardspaceShipbreakerWorld) -> None:
    player = world.player

    _set(world, "Place First Tether", lambda state: _has_tether(state, player))
    _set(world, "Salvage Power Cell", lambda state: state.has("Progressive Grapple Strength", player))
    _set(world, "Process Nanocarbon Panel", lambda state: _has_demo(state, player))

    _set(
        world,
        "Clear Ship Grade 7",
        lambda state: (
            _has_tether(state, player)
            and state.has("Progressive Grapple Strength", player, 2)
        ),
    )

    _set(
        world,
        "Salvage Quasar Thruster",
        lambda state: (
            _has_atlas(state, player)
            and state.has("Progressive Grapple Strength", player)
        ),
    )
    _set(
        world,
        "Salvage ECU",
        lambda state: (
            _has_tether(state, player)
            and state.has("Progressive Grapple Strength", player, 2)
        ),
    )
    _set(
        world,
        "Clear a Ghost Ship",
        lambda state: state.has("Progressive Certification Rank", player, 3),
    )

    # Starting PCR ceiling is rank 4; Rank 5 is the first PCR milestone.
    _set(
        world,
        "Reach Certification Rank 5",
        lambda state: state.has("Progressive Certification Rank", player, 1),
    )
    _set(
        world,
        "Reach Certification Rank 10",
        lambda state: (
            state.has("Progressive Grapple Strength", player, 2)
            or state.has("Progressive Certification Rank", player, 2)
        ),
    )
    _set(
        world,
        "Reach Certification Rank 15",
        lambda state: state.has("Progressive Certification Rank", player, 3),
    )
    _set(
        world,
        "Reach Certification Rank 20",
        lambda state: state.has("Progressive Certification Rank", player, 4),
    )

    for shop in equipment.all_hab_equipment_location_names():
        license_item = equipment.HAB_LICENSE_GATES.get(shop)
        pcr_needed = equipment.pcr_copies_for_cert_rank(equipment.hab_shop_required_rank(shop))
        previous = equipment.hab_shop_previous_location(shop)

        def _shop_rule(
            state,
            p=player,
            lic=license_item,
            pcr=pcr_needed,
            prev=previous,
        ):
            if not _has_license_item(state, p, lic):
                return False
            if pcr and not state.has("Progressive Certification Rank", p, pcr):
                return False
            if prev and _has_loc(world, prev) and not state.can_reach(prev, "Location", p):
                return False
            return True

        _set(world, shop, _shop_rule)

    # Per-ship salvage tiers: need family unlock (Mackerel always via start item / PCR).
    from . import salvage_tiers as st

    for tier_name in st.salvage_tier_names_for_family("Mackerel"):
        _set(
            world,
            tier_name,
            lambda state, p=player: (
                state.has("Unlock Mackerel", p) or state.has("Progressive Certification Rank", p)
            ),
        )
    for tier_name in st.variant_salvage_tier_names_for_family("Mackerel"):
        _set(
            world,
            tier_name,
            lambda state, p=player: (
                state.has("Unlock Mackerel", p) or state.has("Progressive Certification Rank", p)
            ),
        )
    for tier_name in st.salvage_tier_names_for_family("Atlas"):
        _set(world, tier_name, lambda state, p=player: _has_atlas(state, p))
    for tier_name in st.variant_salvage_tier_names_for_family("Atlas"):
        _set(world, tier_name, lambda state, p=player: _has_atlas(state, p))
    for tier_name in st.salvage_tier_names_for_family("Javelin"):
        _set(world, tier_name, lambda state, p=player: _has_javelin(state, p))
    for tier_name in st.variant_salvage_tier_names_for_family("Javelin"):
        _set(world, tier_name, lambda state, p=player: _has_javelin(state, p))
    for tier_name in st.salvage_tier_names_for_family("Gecko"):
        _set(world, tier_name, lambda state, p=player: _has_gecko(state, p))
    for tier_name in st.variant_salvage_tier_names_for_family("Gecko"):
        _set(world, tier_name, lambda state, p=player: _has_gecko(state, p))

    atlas_scout_tier5 = "Atlas Scout Salvage Tier 5"

    if world.options.goal == Goal.option_atlas_scout:
        if _has_loc(world, "Atlas Scout Cleared"):
            world.get_location("Atlas Scout Cleared").access_rule = lambda state: state.can_reach(
                atlas_scout_tier5, "Location", player
            )
    elif world.options.goal == Goal.option_rank_20:
        if _has_loc(world, "Rank 20 Reached"):
            world.get_location("Rank 20 Reached").access_rule = lambda state: (
                state.has("Progressive Certification Rank", player, 4)
                or (
                    _has_loc(world, "Reach Certification Rank 20")
                    and state.can_reach("Reach Certification Rank 20", "Location", player)
                )
            )
    else:
        if _has_loc(world, "Debt Paid"):
            world.get_location("Debt Paid").access_rule = lambda state: (
                _has_tether(state, player)
                and state.has("Progressive Grapple Strength", player, 2)
                and _has_atlas(state, player)
                and state.can_reach(atlas_scout_tier5, "Location", player)
            )

    world.multiworld.completion_condition[player] = lambda state: state.has("Victory", player)
