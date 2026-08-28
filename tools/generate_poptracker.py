#!/usr/bin/env python3
"""Generate PopTracker pack data from the Hardspace Shipbreaker APWorld.

Run from repo root:
  python tools/generate_poptracker.py

Output: poptracker/HSSB/
Does not require an Archipelago install (stubs BaseClasses / Options).
"""

from __future__ import annotations

import json
import re
import struct
import sys
import types
import zlib
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORLD_PKG = ROOT / "worlds"
# Canonical PopTracker pack root (drop this folder into PopTracker packs/).
PACK = ROOT / "poptracker" / "HSSB"
OUT_SCRIPTS = PACK / "scripts" / "generated"
OUT_ITEMS = PACK / "items"
OUT_LOCS = PACK / "locations"
OUT_IMAGES = PACK / "images"
OUT_MAPS = PACK / "maps"
OUT_LAYOUTS = PACK / "layouts"

MAP_COLS = 3
MAP_COL_W = 170
MAP_ROW_H = 22
MAP_PAD = 20


def _stub_archipelago() -> None:
    base = types.ModuleType("BaseClasses")

    class ItemClassification:
        progression = "progression"
        useful = "useful"
        filler = "filler"
        trap = "trap"
        skip_balancing = "skip_balancing"

    class Location:
        pass

    class Item:
        pass

    base.ItemClassification = ItemClassification
    base.Location = Location
    base.Item = Item
    sys.modules["BaseClasses"] = base

    options = types.ModuleType("Options")

    class _Opt:
        def __init_subclass__(cls, **kwargs):
            super().__init_subclass__(**kwargs)

    class Choice(_Opt):
        pass

    class Range(_Opt):
        pass

    class Toggle(_Opt):
        pass

    class OptionGroup:
        def __init__(self, *args, **kwargs):
            pass

    class PerGameCommonOptions:
        pass

    options.Choice = Choice
    options.Range = Range
    options.Toggle = Toggle
    options.OptionGroup = OptionGroup
    options.PerGameCommonOptions = PerGameCommonOptions
    sys.modules["Options"] = options


def _import_world():
    _stub_archipelago()
    import importlib.util

    world_dir = ROOT / "worlds" / "HardspaceShipbreaker"

    def load(name: str, path: Path, package: str = "HardspaceShipbreaker"):
        spec = importlib.util.spec_from_file_location(
            f"{package}.{name}",
            path,
            submodule_search_locations=[str(world_dir)],
        )
        assert spec and spec.loader
        mod = importlib.util.module_from_spec(spec)
        sys.modules[f"{package}.{name}"] = mod
        # Ensure package stub exists for relative imports
        if package not in sys.modules:
            pkg = types.ModuleType(package)
            pkg.__path__ = [str(world_dir)]  # type: ignore[attr-defined]
            sys.modules[package] = pkg
        spec.loader.exec_module(mod)
        return mod

    equipment = load("equipment", world_dir / "equipment.py")
    items = load("items", world_dir / "items.py")
    salvage_tiers = load("salvage_tiers", world_dir / "salvage_tiers.py")
    load("options", world_dir / "options.py")
    locations = load("locations", world_dir / "locations.py")
    return equipment, items, locations, salvage_tiers


def slug(name: str) -> str:
    s = name.lower()
    s = s.replace("(", "").replace(")", "")
    s = re.sub(r"[^a-z0-9]+", "_", s)
    return s.strip("_")


def write_png(path: Path, w: int, h: int, rgba: tuple[int, int, int, int]) -> None:
    """Write a solid-color RGBA PNG."""
    path.parent.mkdir(parents=True, exist_ok=True)
    r, g, b, a = rgba
    raw = bytearray()
    pixel = bytes((r, g, b, a))
    for _ in range(h):
        raw.append(0)
        raw.extend(pixel * w)
    compressed = zlib.compress(bytes(raw), 9)

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", compressed)
        + chunk(b"IEND", b"")
    )


ICON_COLORS: dict[str, tuple[int, int, int, int]] = {
    "progressive_certification_rank": (230, 180, 40, 255),
    "unlock_mackerel": (90, 160, 220, 255),
    "tether": (70, 200, 170, 255),
    "grapple": (220, 120, 60, 255),
    "demo": (200, 60, 60, 255),
    "scanner": (100, 180, 255, 255),
    "suit": (140, 140, 160, 255),
    "cutter": (255, 140, 40, 255),
    "launcher": (180, 80, 200, 255),
    "o2": (80, 200, 120, 255),
    "thruster": (100, 140, 220, 255),
    "charged_push": (255, 220, 80, 255),
    "filler": (120, 120, 120, 255),
    "trap": (160, 40, 40, 255),
    "setting": (80, 80, 100, 255),
    "default": (160, 160, 160, 255),
    "chest_closed": (200, 160, 60, 255),
    "chest_open": (90, 90, 90, 255),
    "map_bg": (28, 32, 40, 255),
}


def icon_family(code: str) -> str:
    c = code.lower()
    if "certification" in c or c == "progressive_certification_rank":
        return "progressive_certification_rank"
    if "mackerel" in c:
        return "unlock_mackerel"
    if "tether" in c:
        return "tether"
    if "grapple" in c:
        return "grapple"
    if "demo" in c:
        return "demo"
    if "scanner" in c:
        return "scanner"
    if "suit" in c or "helmet" in c or "heat" in c or "cryo" in c or "electrical" in c or "audio" in c:
        return "suit"
    if "cutter" in c or "stinger" in c or "splitsaw" in c:
        return "cutter"
    if "launcher" in c:
        return "launcher"
    if "o2" in c:
        return "o2"
    if "thruster" in c:
        return "thruster"
    if "charged" in c or "push" in c:
        return "charged_push"
    if "credit" in c or "lynx" in c or "token" in c or "nothing" in c:
        return "filler"
    if "clone" in c or "trap" in c:
        return "trap"
    if c.startswith("setting_"):
        return "setting"
    return "default"


FILLER_NAMES = {
    "LYNX Token Pack (Small)",
    "LYNX Token Pack (Medium)",
    "Credit Pack (Small)",
    "Credit Pack (Medium)",
    "Credit Pack (Large)",
    "Clone Fee Tax",
    "Nothing",
}

# Legacy IDs kept for old seeds — track silently, omit from main grid.
LEGACY_NAMES = {
    "Progressive Ship Unlock",
    "Unlock Atlas",
    "Unlock Javelin",
    "Unlock Gecko",
    "Progressive Tethers",
    "Progressive Cutter",
}


def item_img_path(code: str) -> str:
    fam = icon_family(code)
    return f"images/icon_{fam}.png"


def access_rule_for(
    name: str,
    region: str,
    equipment,
    _salvage_tiers,
) -> list[str]:
    """PopTracker access_rules (OR of AND-groups). Codes use item slugs."""
    pcr = "progressive_certification_rank"
    grapple = "progressive_grapple_strength"
    tether_rules = [
        slug("Tether Module"),
        slug("Progressive Tether Amount"),
        slug("Progressive Tether Lifetime"),
    ]
    demo_rules = [slug("Demo Charge License"), slug("Progressive Demo Charges")]
    atlas = [f"{pcr}:1", slug("Unlock Atlas"), f"{slug('Progressive Ship Unlock')}:1"]
    javelin = [f"{pcr}:1", slug("Unlock Javelin"), f"{slug('Progressive Ship Unlock')}:2"]
    gecko = [f"{pcr}:2", slug("Unlock Gecko"), f"{slug('Progressive Ship Unlock')}:3"]
    mackerel = [slug("Unlock Mackerel"), pcr]

    if name == "Place First Tether":
        return tether_rules
    if name == "Salvage Power Cell":
        return [f"{m},{grapple}" for m in mackerel]
    if name == "Process Nanocarbon Panel":
        return demo_rules
    if name == "Clear Ship Grade 7":
        return [f"{r},{grapple}:2" for r in tether_rules]
    if name == "Salvage Quasar Thruster":
        return [f"{a},{grapple}" for a in atlas]
    if name == "Salvage ECU":
        return [f"{r},{grapple}:2" for r in tether_rules]
    if name in ("Salvage Class I Reactor", "Salvage Thruster Class I"):
        return [grapple]
    if name == "Clear a Ghost Ship":
        return [f"{pcr}:3"]
    if name == "Reach Certification Rank 10":
        return [f"{grapple}:2", f"{pcr}:2"]
    if name == "Reach Certification Rank 15":
        return [f"{pcr}:3"]
    if name == "Reach Certification Rank 20":
        return [f"{pcr}:4"]

    if name in equipment.HAB_LICENSE_GATES:
        lic = equipment.HAB_LICENSE_GATES[name]
        if lic == "Tether Module":
            return tether_rules
        if lic == "Demo Charge License":
            return demo_rules
        if lic == "Charged Push":
            return [slug("Charged Push"), slug("Progressive Charged Push Force")]
        if lic == "Unlock Launcher":
            return [slug("Unlock Launcher"), slug("Progressive Launcher Range")]
        if lic == "O2 Recharge Module":
            return [slug("O2 Recharge Module")]
        if lic == "Progressive Scanner":
            return [slug("Progressive Scanner")]
        return [slug(lic)]

    # Region / salvage family gates
    if region == "Mackerel" or name.startswith("Mackerel "):
        return mackerel
    if region == "Atlas" or name.startswith("Atlas "):
        return atlas
    if region == "Javelin" or name.startswith("Javelin "):
        return javelin
    if region == "Gecko" or name.startswith("Gecko "):
        return gecko

    return []


def density_flags(name: str, locations, equipment, salvage_tiers) -> dict[str, bool]:
    """Which density modes include this location (before shop filter)."""
    sparse = set(locations.SPARSE_LOCATIONS)
    standard = sparse | set(locations.STANDARD_EXTRA)
    full = set(locations.LOCATION_NAME_TO_ID.keys())
    shop = set(locations.SHOP_LOCATIONS)
    core = set(locations.HAB_CORE_SHOP)
    rental = set(locations.HAB_RENTAL_DURABILITY_SHOP)
    sparse_shop = {
        "Hab: Unlock Tethers",
        "Hab: Grapple Strength 1",
        "Hab: Grapple Strength 2",
        "Hab: Grapple Strength 3",
        "Hab: Unlock Demo Charge",
        "Hab: Purchase First Equipment Upgrade",
        "Hab: Tethers Amount 1",
        "Hab: Tethers Amount 2",
    }

    in_sparse_base = name in sparse
    in_standard_base = name in standard
    in_full_base = name in full

    is_shop = name in shop
    return {
        "sparse": (in_sparse_base and not is_shop) or (name in sparse_shop),
        "standard": (in_standard_base and not is_shop) or (name in core and name not in rental),
        "full": (in_full_base and not is_shop) or is_shop,
        "shop": is_shop,
        "sparse_shop": name in sparse_shop,
        "core_shop": name in core and name not in rental,
        "rental_shop": name in rental,
    }


def loc_region_bucket(name: str, locations) -> str:
    if name in locations.HAB_LOCS:
        return "Hab"
    if name in locations.BAY_LOCS:
        return "Bay"
    if name in locations.MACKEREL_LOCS:
        return "Mackerel"
    if name in locations.REACTOR_I_LOCS or name in locations.REACTOR_II_LOCS:
        return "Reactor"
    if name in locations.ATLAS_LOCS:
        return "Atlas"
    if name in locations.JAVELIN_LOCS:
        return "Javelin"
    if name in locations.GECKO_LOCS:
        return "Gecko"
    return "Other"


def loc_section_path(name: str, region: str, salvage_tiers) -> tuple[str, str, str]:
    """Return (region, group, leaf_display) for location tree path."""
    if name.startswith("Hab: "):
        leaf = name[len("Hab: ") :]
        return region, "Shop", leaf
    if "Salvage Tier" in name:
        for fam, variant in salvage_tiers.SHIP_VARIANTS:
            prefix = f"{fam} {variant} Salvage Tier"
            if name.startswith(prefix):
                return region, variant, name
        for fam in salvage_tiers.SHIP_FAMILIES:
            if name.startswith(f"{fam} Salvage Tier"):
                return region, "Family Tiers", name
    if name in (
        "Finish Basic Training",
        "Reach Certification Rank 5",
        "Reach Certification Rank 10",
        "Reach Certification Rank 15",
        "Reach Certification Rank 20",
        "Recover First Data Drive",
        "Recover 3 Data Drives",
        "Recover 5 Data Drives",
        "Survive First Clone",
    ):
        return region, "Milestones", name
    if region == "Bay":
        return region, "Bay Checks", name
    if region == "Reactor":
        group = "Reactor II" if name == "Salvage ECU" else "Reactor I"
        return region, group, name
    if name.startswith("Salvage "):
        return region, "Parts", name
    return region, "Checks", name


def lua_escape(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def emit_lua_mapping(path: Path, table_name: str, entries: dict[int, list], header: str) -> None:
    lines = [
        f"-- Generated by tools/generate_poptracker.py — do not edit by hand.",
        header,
        f"{table_name} = {{",
    ]
    for i in sorted(entries):
        vals = entries[i]
        # Lua table: { "a", "b" } or { {"code"}, "type" }
        if isinstance(vals[0], list):
            code, typ = vals[0][0], vals[1]
            lines.append(f'    [{i}] = {{{{"{lua_escape(code)}"}}, "{typ}"}},')
        else:
            inner = ", ".join(f'"{lua_escape(v)}"' for v in vals)
            lines.append(f"    [{i}] = {{{inner}}},")
    lines.append("}")
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def emit_visibility_lua(path: Path, meta: dict[int, dict]) -> None:
    lines = [
        "-- Generated by tools/generate_poptracker.py — do not edit by hand.",
        "-- Per-location density / shop-sanity visibility metadata.",
        "LOCATION_VISIBILITY = {",
    ]
    for i in sorted(meta):
        m = meta[i]
        lines.append(
            "    [%d] = {sparse=%s, standard=%s, full=%s, shop=%s, sparse_shop=%s, "
            "core_shop=%s, rental_shop=%s},"
            % (
                i,
                str(m["sparse"]).lower(),
                str(m["standard"]).lower(),
                str(m["full"]).lower(),
                str(m["shop"]).lower(),
                str(m["sparse_shop"]).lower(),
                str(m["core_shop"]).lower(),
                str(m["rental_shop"]).lower(),
            )
        )
    lines.append("}")
    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    equipment, items, locations, salvage_tiers = _import_world()
    OUT_SCRIPTS.mkdir(parents=True, exist_ok=True)
    OUT_ITEMS.mkdir(parents=True, exist_ok=True)
    OUT_LOCS.mkdir(parents=True, exist_ok=True)
    OUT_IMAGES.mkdir(parents=True, exist_ok=True)
    (OUT_IMAGES / "maps").mkdir(parents=True, exist_ok=True)
    OUT_MAPS.mkdir(parents=True, exist_ok=True)
    OUT_LAYOUTS.mkdir(parents=True, exist_ok=True)

    # --- Icons ---
    for fam, color in ICON_COLORS.items():
        write_png(OUT_IMAGES / f"icon_{fam}.png", 32, 32, color)
    write_png(OUT_IMAGES / "chest_closed.png", 16, 16, ICON_COLORS["chest_closed"])
    write_png(OUT_IMAGES / "chest_open.png", 16, 16, ICON_COLORS["chest_open"])

    # --- Items ---
    item_json: list[dict] = []
    item_mapping: dict[int, list] = {}
    main_grid_codes: list[str] = []
    filler_codes: list[str] = []

    # Settings (hosted)
    item_json.append(
        {
            "name": "Location Density",
            "type": "progressive",
            "allow_disabled": False,
            "loop": True,
            "initial_stage_idx": 1,
            "stages": [
                {
                    "name": "Sparse",
                    "img": item_img_path("setting_density"),
                    "codes": "setting_density_sparse,setting_density",
                    "inherit_codes": False,
                },
                {
                    "name": "Standard",
                    "img": item_img_path("setting_density"),
                    "codes": "setting_density_standard,setting_density",
                    "inherit_codes": False,
                },
                {
                    "name": "Full",
                    "img": item_img_path("setting_density"),
                    "codes": "setting_density_full,setting_density",
                    "inherit_codes": False,
                },
            ],
        }
    )
    item_json.append(
        {
            "name": "Hab Shop Sanity",
            "type": "toggle",
            "img": item_img_path("setting_hab"),
            "codes": "setting_hab_shop",
            "initial_active_state": True,
        }
    )
    item_json.append(
        {
            "name": "Death Link",
            "type": "toggle",
            "img": item_img_path("setting_death_link"),
            "codes": "setting_death_link",
            "initial_active_state": False,
        }
    )
    item_json.append(
        {
            "name": "Goal",
            "type": "progressive",
            "allow_disabled": False,
            "loop": True,
            "initial_stage_idx": 0,
            "stages": [
                {
                    "name": "Debt Payoff",
                    "img": item_img_path("setting_goal"),
                    "codes": "setting_goal_debt,setting_goal",
                    "inherit_codes": False,
                },
                {
                    "name": "Atlas Scout",
                    "img": item_img_path("setting_goal"),
                    "codes": "setting_goal_atlas,setting_goal",
                    "inherit_codes": False,
                },
                {
                    "name": "Rank 20",
                    "img": item_img_path("setting_goal"),
                    "codes": "setting_goal_rank20,setting_goal",
                    "inherit_codes": False,
                },
            ],
        }
    )

    counts = dict(items.PROGRESSION_ITEM_COUNTS)
    classifications = dict(items.DEFAULT_ITEM_CLASSIFICATIONS)

    for name, item_id in sorted(items.ITEM_NAME_TO_ID.items(), key=lambda x: x[1]):
        code = slug(name)
        cls = classifications.get(name, "useful")
        count = counts.get(name, 1)
        is_filler = name in FILLER_NAMES or name in LEGACY_NAMES
        img = item_img_path(code)

        if count > 1:
            # Progressive stages: inherit_codes so stage N provides codes for 1..N.
            stages = []
            for n in range(1, count + 1):
                stage_codes = f"{code}:{n}"
                if n == 1:
                    stage_codes = f"{code},{code}:1"
                stages.append(
                    {
                        "img": img,
                        "codes": stage_codes,
                        "inherit_codes": True,
                        "name": f"{name} ×{n}",
                    }
                )
            item_json.append(
                {
                    "name": name,
                    "type": "progressive",
                    "allow_disabled": True,
                    "loop": False,
                    "stages": stages,
                }
            )
            item_mapping[item_id] = [[code], "progressive"]
        else:
            item_json.append(
                {
                    "name": name,
                    "type": "toggle",
                    "img": img,
                    "codes": code,
                }
            )
            item_mapping[item_id] = [[code], "toggle"]

        if is_filler:
            filler_codes.append(code)
        elif cls in ("progression", "useful") or name == "Unlock Mackerel":
            # Skip pure legacy ship unlocks from main grid (still mapped)
            if name not in LEGACY_NAMES:
                main_grid_codes.append(code)

    # Unlock Mackerel starts collected in-world — still on grid
    OUT_ITEMS.joinpath("items.json").write_text(
        json.dumps(item_json, indent=2) + "\n", encoding="utf-8"
    )

    # Item layout grids
    prog_useful = [c for c in main_grid_codes]
    # Stable order: put PCR / mackerel / licenses first
    priority = [
        "progressive_certification_rank",
        "unlock_mackerel",
        "tether_module",
        "demo_charge_license",
        "charged_push",
        "o2_recharge_module",
        "unlock_launcher",
        "progressive_grapple_strength",
        "progressive_tether_amount",
        "progressive_scanner",
        "progressive_suit_integrity",
        "progressive_demo_charges",
    ]
    ordered = [c for c in priority if c in prog_useful]
    ordered += [c for c in prog_useful if c not in ordered]

    def chunk_rows(codes: list[str], width: int = 6) -> list[list[str]]:
        rows = []
        for i in range(0, len(codes), width):
            rows.append(codes[i : i + width])
        return rows

    layouts_items = {
        "items": {
            "type": "itemgrid",
            "item_margin": "2,2",
            "item_size": 36,
            "h_alignment": "left",
            "rows": chunk_rows(ordered, 6),
        },
        "settings": {
            "type": "itemgrid",
            "item_margin": "4,4",
            "item_size": 36,
            "h_alignment": "left",
            "rows": [
                ["setting_density", "setting_hab_shop", "setting_death_link", "setting_goal"],
            ],
        },
        "filler": {
            "type": "itemgrid",
            "item_margin": "2,2",
            "item_size": 28,
            "h_alignment": "left",
            "rows": chunk_rows(filler_codes, 6) if filler_codes else [[""]],
        },
    }
    OUT_LAYOUTS.joinpath("items.json").write_text(
        json.dumps(layouts_items, indent=2) + "\n", encoding="utf-8"
    )

    # --- Locations (per-group multi-column maps; no single tall pin strip) ---
    location_mapping: dict[int, list[str]] = {}
    visibility_meta: dict[int, dict] = {}
    tree: dict[str, dict[str, list]] = defaultdict(lambda: defaultdict(list))

    for name, loc_id in sorted(locations.LOCATION_NAME_TO_ID.items(), key=lambda x: x[1]):
        region = loc_region_bucket(name, locations)
        region, group, leaf = loc_section_path(name, region, salvage_tiers)
        path = f"@{region}/{group}/{leaf}/Check"
        location_mapping[loc_id] = [path]
        flags = density_flags(name, locations, equipment, salvage_tiers)
        visibility_meta[loc_id] = flags
        rules = access_rule_for(name, region, equipment, salvage_tiers)
        map_id = f"{region.lower()}_{slug(group)}"
        leaf_obj = {
            "name": leaf,
            "short_name": leaf[:28],
            "chest_unopened_img": "images/chest_closed.png",
            "chest_opened_img": "images/chest_open.png",
            "access_rules": rules,
            "visibility_rules": [f"$loc_visible|{loc_id}"],
            "sections": [{"name": "Check", "item_count": 1}],
            "_map_id": map_id,
        }
        tree[region][group].append(leaf_obj)

    maps: list[dict] = []
    region_group_maps: dict[str, list[tuple[str, str]]] = defaultdict(list)

    for region, groups in tree.items():
        children = []
        for group_name, leaves in groups.items():
            map_id = f"{region.lower()}_{slug(group_name)}"
            n = len(leaves)
            rows = max(1, (n + MAP_COLS - 1) // MAP_COLS)
            map_w = MAP_PAD * 2 + MAP_COLS * MAP_COL_W
            map_h = MAP_PAD * 2 + rows * MAP_ROW_H + 8
            map_img = f"images/maps/{map_id}.png"
            write_png(OUT_IMAGES / "maps" / f"{map_id}.png", map_w, map_h, ICON_COLORS["map_bg"])
            maps.append(
                {
                    "name": map_id,
                    "location_size": 16,
                    "location_border_thickness": 1,
                    "img": map_img,
                }
            )
            region_group_maps[region].append((group_name, map_id))

            for i, leaf_obj in enumerate(leaves):
                col = i % MAP_COLS
                row = i // MAP_COLS
                x = MAP_PAD + col * MAP_COL_W + 8
                y = MAP_PAD + row * MAP_ROW_H + 8
                leaf_obj["map_locations"] = [{"map": map_id, "x": x, "y": y}]
                leaf_obj.pop("_map_id", None)

            children.append({"name": group_name, "children": leaves})

        loc_file = [
            {
                "name": region,
                "chest_unopened_img": "images/chest_closed.png",
                "chest_opened_img": "images/chest_open.png",
                "children": children,
            }
        ]
        OUT_LOCS.joinpath(f"{region.lower()}.json").write_text(
            json.dumps(loc_file, indent=2) + "\n", encoding="utf-8"
        )

    OUT_MAPS.joinpath("maps.json").write_text(json.dumps(maps, indent=2) + "\n", encoding="utf-8")

    # Region tabs → nested group tabs → compact multi-column maps
    region_order = ("Hab", "Bay", "Mackerel", "Atlas", "Javelin", "Gecko", "Reactor")
    tabs = []
    for region in region_order:
        group_tabs = region_group_maps.get(region, [])
        if not group_tabs:
            continue
        if len(group_tabs) == 1:
            content = {"type": "map", "maps": [group_tabs[0][1]]}
        else:
            content = {
                "type": "tabbed",
                "tabs": [
                    {"title": gname, "content": {"type": "map", "maps": [mid]}}
                    for gname, mid in group_tabs
                ],
            }
        tabs.append({"title": region, "content": content})

    tracker = {
        "tracker_default": {
            "type": "container",
            "background": "#12151c",
            "content": {
                "type": "dock",
                "content": [
                    {
                        "type": "dock",
                        "dock": "left",
                        "content": [
                            {
                                "type": "group",
                                "header": "Items",
                                "dock": "top",
                                "content": {"type": "layout", "key": "items"},
                            },
                            {
                                "type": "group",
                                "header": "Seed Options",
                                "dock": "top",
                                "content": {"type": "layout", "key": "settings"},
                            },
                            {
                                "type": "group",
                                "header": "Filler / Traps",
                                "dock": "top",
                                "content": {"type": "layout", "key": "filler"},
                            },
                        ],
                    },
                    {"type": "tabbed", "tabs": tabs},
                ],
            },
        },
        "tracker_broadcast": {
            "type": "container",
            "background": "#12151c",
            "content": {
                "type": "group",
                "header": "Hardspace Shipbreaker",
                "content": {"type": "layout", "key": "items"},
            },
        },
    }
    OUT_LAYOUTS.mkdir(parents=True, exist_ok=True)
    OUT_LAYOUTS.joinpath("tracker.json").write_text(
        json.dumps(tracker, indent=2) + "\n", encoding="utf-8"
    )

    emit_lua_mapping(
        OUT_SCRIPTS / "item_mapping.lua",
        "ITEM_MAPPING",
        item_mapping,
        "-- AP item_id -> { {code}, type }",
    )
    emit_lua_mapping(
        OUT_SCRIPTS / "location_mapping.lua",
        "LOCATION_MAPPING",
        location_mapping,
        "-- AP location_id -> { \"@path\" }",
    )
    emit_visibility_lua(OUT_SCRIPTS / "location_visibility.lua", visibility_meta)

    loc_files = sorted(p.name for p in OUT_LOCS.glob("*.json"))
    (OUT_SCRIPTS / "location_files.lua").write_text(
        "-- Generated — location JSON basenames\nLOCATION_FILES = {\n"
        + "".join(f'    "locations/{n}",\n' for n in loc_files)
        + "}\n",
        encoding="utf-8",
    )

    print(f"Generated {len(item_mapping)} items, {len(location_mapping)} locations -> {PACK}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
