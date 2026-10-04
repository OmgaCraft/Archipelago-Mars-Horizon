from typing import Any, ClassVar, Dict, List

from BaseClasses import Item, ItemClassification, Location, Region, Tutorial
from worlds.AutoWorld import WebWorld, World

from . import data_model as dm
from .options import MarsHorizonOptions, option_groups
from .rules import LogicBuilder

GAME_NAME = "Mars Horizon"

_ITEMS = dm.item_table()
_LOCATIONS = dm.location_table()
ITEM_NAME_BY_KEY: Dict[str, str] = {i["key"]: i["name"] for i in _ITEMS}
ITEM_ID_BY_NAME: Dict[str, int] = {i["name"]: i["id"] for i in _ITEMS}
LOCATION_NAME_BY_KEY: Dict[str, Dict[str, str]] = {}
for _l in _LOCATIONS:
    LOCATION_NAME_BY_KEY.setdefault(_l["category"], {})[_l["key"]] = _l["name"]
LOCATION_ID_BY_NAME: Dict[str, int] = {l["name"]: l["id"] for l in _LOCATIONS}
FILLER_NAMES = {"Filler_Funds": "Funding Grant", "Filler_Science": "Research Data", "Filler_Support": "Public Support"}

VICTORY_EVENT = "Victory"
MILESTONE_EVENT = "Milestone Reached"


class MarsHorizonItem(Item):
    game = GAME_NAME


class MarsHorizonLocation(Location):
    game = GAME_NAME


class MarsHorizonWeb(WebWorld):
    theme = "ocean"
    option_groups = option_groups
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "Installer le plugin MarsHorizonAP et jouer à Mars Horizon dans un multiworld Archipelago.",
        "English",
        "setup_en.md",
        "setup/en",
        ["OmgaCraft"],
    )]


class MarsHorizonWorld(World):
    """Mars Horizon est une simulation de gestion d'agence spatiale : tu construis ta base, recherches
    l'arbre technologique, assembles des fusées et enchaînes les missions jusqu'au premier humain sur Mars,
    en course contre des agences rivales. Ici, technologies, bâtiments et missions sont mélangés dans le
    multiworld, et chaque recherche, construction et jalon est un check."""
    game = GAME_NAME
    web = MarsHorizonWeb()
    options_dataclass = MarsHorizonOptions
    options: MarsHorizonOptions
    topology_present = False

    item_name_to_id: ClassVar[Dict[str, int]] = ITEM_ID_BY_NAME
    location_name_to_id: ClassVar[Dict[str, int]] = LOCATION_ID_BY_NAME
    item_name_groups = {
        "Technology": {i["name"] for i in _ITEMS if i["category"] == "research"},
        "Buildings": {i["name"] for i in _ITEMS if i["category"] == "research" and i["node_class"].startswith("Building")},
        "Missions": {i["name"] for i in _ITEMS if i["category"] == "research" and i["node_class"].startswith("Mission")},
        "Payloads": {i["name"] for i in _ITEMS if i["category"] == "research" and i["node_class"].startswith("Payload")},
        "Rocket Parts": {i["name"] for i in _ITEMS if i["category"] == "research" and i["tree"] == "Vehicles"},
        "Filler": set(FILLER_NAMES.values()),
    }

    agency_name: str
    logic: LogicBuilder
    precollected_keys: List[str]

    # --- génération ---------------------------------------------------------------------------------
    def generate_early(self) -> None:
        self.agency_name = dm.AGENCIES[self.options.agency.value]
        self.model = dm.agency_model(self.agency_name)
        self.logic = LogicBuilder(self.model, lambda key: ITEM_NAME_BY_KEY[key])
        self.logic.build_missions()
        self.item_keys = dm.agency_item_keys(self.agency_name)
        self.building_ids = {b["id"] for b in dm.game_data()["blueprints"]}
        self.precollected_keys = []
        if self.options.starting_launchpad:
            self.precollected_keys.append("Building_LaunchPad_Small")
        if not self.options.shuffle_buildings:
            self.precollected_keys += [k for k in self.item_keys if k in self.building_ids]
        self.precollected_keys = [k for k in dict.fromkeys(self.precollected_keys) if k in self.item_keys]

    def create_regions(self) -> None:
        player = self.player
        menu = Region("Menu", player, self.multiworld)
        research = Region("Research Tree", player, self.multiworld)
        base = Region("Base", player, self.multiworld)
        space = Region("Space Program", player, self.multiworld)
        self.multiworld.regions += [menu, research, base, space]
        menu.connect(research)
        menu.connect(base)
        menu.connect(space)

        for key in dm.agency_research_location_keys(self.agency_name):
            name = LOCATION_NAME_BY_KEY["research"][key]
            research.locations.append(MarsHorizonLocation(player, name, LOCATION_ID_BY_NAME[name], research))

        for key in dm.agency_building_location_keys(self.agency_name):
            name = LOCATION_NAME_BY_KEY["building"][key]
            location = MarsHorizonLocation(player, name, LOCATION_ID_BY_NAME[name], base)
            rule = self.logic.building_rule(key, player)
            if rule is None:  # bâtiment inatteignable pour cette agence : pas de location
                continue
            location.access_rule = rule
            base.locations.append(location)

        mission_of_milestone = {m.milestone: m.id for m in self.logic.missions.values()}
        rules: Dict[str, Any] = {}
        for key in dm.milestone_location_keys():
            mission_id = mission_of_milestone[key]
            if not self.logic.mission_possible(mission_id):
                continue  # mission obsolète ou sans payload pour cette agence (ex. Sample Retrieval, remplacée)
            name = LOCATION_NAME_BY_KEY["milestone"][key]
            location = MarsHorizonLocation(player, name, LOCATION_ID_BY_NAME[name], space)
            rules[key] = self.logic.mission_rule(mission_id, player)
            location.access_rule = rules[key]
            space.locations.append(location)

        # Événements : classes de véhicules (calculées une fois par balayage) et objectif.
        for (distance, weight), options in sorted(self.logic.vehicle_classes.items()):
            if not options:
                continue
            for option in options:
                self.logic.referenced |= option
            event_name = self.logic.vehicle_event_name(distance, weight)
            location = MarsHorizonLocation(player, f"{event_name} Possible", None, space)
            option_tuples = tuple(tuple(sorted(o)) for o in options)
            location.access_rule = _any_all(option_tuples, player)
            location.place_locked_item(MarsHorizonItem(event_name, ItemClassification.progression, None, player))
            space.locations.append(location)

        self._create_goal(space, rules)

    def _create_goal(self, space: Region, rules: Dict[str, Any]) -> None:
        player = self.player
        if self.options.goal == 0:
            final = next(m for m in self.logic.missions.values() if m.is_final)
            goal = MarsHorizonLocation(player, "Crewed Mars Landing (Goal)", None, space)
            goal.access_rule = rules.get(final.milestone, lambda state: False)
            goal.place_locked_item(MarsHorizonItem(VICTORY_EVENT, ItemClassification.progression, None, player))
            space.locations.append(goal)
            self.multiworld.completion_condition[player] = lambda state: state.has(VICTORY_EVENT, player)
        else:
            count = self.options.milestone_goal_count.value
            for key, rule in rules.items():
                event = MarsHorizonLocation(player, f"{LOCATION_NAME_BY_KEY['milestone'][key]} (Event)", None, space)
                event.access_rule = rule
                event.place_locked_item(MarsHorizonItem(MILESTONE_EVENT, ItemClassification.progression, None, player))
                space.locations.append(event)
            self.multiworld.completion_condition[player] = lambda state: state.count(MILESTONE_EVENT, player) >= count

    def create_item(self, name: str) -> MarsHorizonItem:
        if name in FILLER_NAMES.values():
            classification = ItemClassification.filler
        elif name in self.logic.referenced:
            classification = ItemClassification.progression
        else:
            classification = ItemClassification.useful
        return MarsHorizonItem(name, classification, ITEM_ID_BY_NAME[name], self.player)

    def get_filler_item_name(self) -> str:
        weights = [self.options.filler_funds_weight.value, self.options.filler_science_weight.value,
                   self.options.filler_support_weight.value]
        if not any(weights):
            weights = [1, 1, 1]
        return self.random.choices(list(FILLER_NAMES.values()), weights)[0]

    def create_items(self) -> None:
        precollected = {ITEM_NAME_BY_KEY[k] for k in self.precollected_keys}
        for name in sorted(precollected):
            self.multiworld.push_precollected(self.create_item(name))
        pool = [self.create_item(ITEM_NAME_BY_KEY[k]) for k in self.item_keys if ITEM_NAME_BY_KEY[k] not in precollected]
        locations = [l for l in self.get_locations() if l.address is not None]
        filler = len(locations) - len(pool)
        assert filler >= 0, f"Plus d'items ({len(pool)}) que de locations ({len(locations)})"
        pool += [self.create_item(self.get_filler_item_name()) for _ in range(filler)]
        self.multiworld.itempool += pool

    def fill_slot_data(self) -> Dict[str, Any]:
        locations = {}
        for l in self.get_locations():
            if l.address is None:
                continue
            row = next(x for x in _LOCATIONS if x["id"] == l.address)
            locations[str(l.address)] = f"{row['category']}:{row['key']}"
        items = {str(ITEM_ID_BY_NAME[ITEM_NAME_BY_KEY[k]]): k for k in self.item_keys}
        data: Dict[str, Any] = self.options.as_dict(
            "agency", "goal", "milestone_goal_count", "starting_launchpad", "shuffle_buildings",
            "filler_strength", "death_link")
        data.update({
            "agency_name": self.agency_name,
            "locations": locations,
            "items": items,
            "filler": {str(ITEM_ID_BY_NAME[n]): k for k, n in FILLER_NAMES.items()},
            "game_version": dm.game_data()["meta"]["game_version"],
            "data_format": 1,
        })
        return data


def _any_all(option_tuples, player):
    def rule(state) -> bool:
        return any(state.has_all(option, player) for option in option_tuples)
    return rule
