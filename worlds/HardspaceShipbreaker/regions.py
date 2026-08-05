from __future__ import annotations

from typing import TYPE_CHECKING

from BaseClasses import Region

if TYPE_CHECKING:
    from .world import HardspaceShipbreakerWorld


def create_and_connect_regions(world: HardspaceShipbreakerWorld) -> None:
    player = world.player
    hab = Region("Hab", player, world.multiworld)
    bay = Region("Bay", player, world.multiworld)
    mackerel = Region("Mackerel", player, world.multiworld)
    atlas = Region("Atlas", player, world.multiworld)
    javelin = Region("Javelin", player, world.multiworld)
    gecko = Region("Gecko", player, world.multiworld)
    reactor_i = Region("Reactor_I", player, world.multiworld)
    reactor_ii = Region("Reactor_II", player, world.multiworld)
    goal = Region("Goal", player, world.multiworld)

    world.multiworld.regions += [
        hab,
        bay,
        mackerel,
        atlas,
        javelin,
        gecko,
        reactor_i,
        reactor_ii,
        goal,
    ]

    hab.connect(bay, "Hab to Bay")
    # Ship families appear on the job board via Certification Rank (vanilla).
    # Progressive Certification Rank raises the MP ceiling; map regions to those bands.
    # ×1 → ranks through 9 (Atlas + early Javelin); ×2 → through 14 (Gecko Hopper).
    bay.connect(
        mackerel,
        "Bay to Mackerel",
        lambda state: (
            state.has("Unlock Mackerel", player)
            or state.has("Progressive Certification Rank", player)
        ),
    )
    bay.connect(
        atlas,
        "Bay to Atlas",
        lambda state: (
            state.has("Progressive Certification Rank", player, 1)
            or state.has("Progressive Ship Unlock", player, 1)  # legacy seeds
            or state.has("Unlock Atlas", player)
        ),
    )
    bay.connect(
        javelin,
        "Bay to Javelin",
        lambda state: (
            state.has("Progressive Certification Rank", player, 1)
            or state.has("Progressive Ship Unlock", player, 2)
            or state.has("Unlock Javelin", player)
        ),
    )
    bay.connect(
        gecko,
        "Bay to Gecko",
        lambda state: (
            state.has("Progressive Certification Rank", player, 2)
            or state.has("Progressive Ship Unlock", player, 3)
            or state.has("Unlock Gecko", player)
        ),
    )

    mackerel.connect(
        reactor_i,
        "Mackerel to Reactor_I",
        lambda state: state.has("Progressive Grapple Strength", player),
    )
    atlas.connect(
        reactor_i,
        "Atlas to Reactor_I",
        lambda state: state.has("Progressive Grapple Strength", player),
    )
    javelin.connect(
        reactor_i,
        "Javelin to Reactor_I",
        lambda state: state.has("Progressive Grapple Strength", player),
    )
    reactor_i.connect(
        reactor_ii,
        "Reactor_I to Reactor_II",
        lambda state: (
            state.has("Progressive Grapple Strength", player, 2)
            and state.has("Tether Module", player)
        ),
    )
    bay.connect(goal, "Bay to Goal")
