from collections.abc import Mapping
from typing import Any

from worlds.AutoWorld import World

from . import items, locations, regions, rules, web_world
from . import options as hs_options


class HardspaceShipbreakerWorld(World):
    """
    Hardspace: Shipbreaker — Career salvage multiworld (public RC 0.6.0).
    """

    game = "Hardspace Shipbreaker"
    web = web_world.HardspaceShipbreakerWebWorld()
    options_dataclass = hs_options.HardspaceShipbreakerOptions
    options: hs_options.HardspaceShipbreakerOptions

    location_name_to_id = locations.LOCATION_NAME_TO_ID
    item_name_to_id = items.ITEM_NAME_TO_ID

    origin_region_name = "Hab"

    def create_regions(self) -> None:
        regions.create_and_connect_regions(self)
        locations.create_all_locations(self)

    def set_rules(self) -> None:
        rules.set_all_rules(self)

    def create_items(self) -> None:
        # Starting Unlock Mackerel so early Career is always reachable.
        self.multiworld.push_precollected(self.create_item("Unlock Mackerel"))
        items.create_all_items(self)

    def create_item(self, name: str) -> items.HardspaceShipbreakerItem:
        return items.create_item_with_correct_classification(self, name)

    def get_filler_item_name(self) -> str:
        return items.get_filler_item_name(self)

    def fill_slot_data(self) -> Mapping[str, Any]:
        small, medium, large = hs_options.credit_pack_amounts_for(self.options.credit_pack_value.value)
        return {
            "goal": int(self.options.goal.value),
            "death_link": bool(self.options.death_link.value),
            "hab_shop_sanity": bool(self.options.hab_shop_sanity.value),
            "location_density": int(self.options.location_density.value),
            "credit_pack_value": int(self.options.credit_pack_value.value),
            "credit_pack_small": small,
            "credit_pack_medium": medium,
            "credit_pack_large": large,
        }
