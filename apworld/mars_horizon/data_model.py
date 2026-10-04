"""Chargement des données générées (data/*.json) et modèle de jeu par agence.

Les JSON sont produits par tools/build_data.py à partir du dump du jeu ; rien n'est écrit à la main ici.
"""
import json
import pkgutil
from dataclasses import dataclass, field
from functools import lru_cache
from typing import Dict, FrozenSet, List, Optional, Set, Tuple

AGENCIES = ["USA", "Russia", "Europe", "China", "Japan"]


def _load(name: str):
    raw = pkgutil.get_data(__name__.rpartition(".")[0], f"data/{name}")
    return json.loads(raw.decode("utf-8"))


@lru_cache(maxsize=None)
def game_data() -> dict:
    return _load("game_data.json")


@lru_cache(maxsize=None)
def item_table() -> List[dict]:
    return _load("items.json")


@lru_cache(maxsize=None)
def location_table() -> List[dict]:
    return _load("locations.json")


# Résultat de "ressource de recherche" pour la logique :
FREE = None          # disponible sans item (départ, ou récompense d'ère)
UNAVAILABLE = False  # inatteignable (aucun item ne le donne)


@dataclass
class VehicleOption:
    """Ensemble minimal d'items (noms) qui permet un véhicule pour (distance, masse)."""
    items: FrozenSet[str]


@dataclass
class MissionInfo:
    id: str
    distance: int
    min_crew: int
    payloads: List[Tuple[str, int]]  # (research id du payload, masse)
    milestone: str
    is_final: bool
    is_required_for_mars: bool
    order: int = 0


@dataclass
class AgencyModel:
    """Tout ce que la logique doit savoir pour une agence."""
    name: str
    start_research: Set[str]
    free_research: Set[str]               # départ + récompenses d'ère valides
    node_research: Set[str]               # recherches portées par un nœud (= items et locations de recherche)
    start_buildings: Set[str]
    research_name: Dict[str, str] = field(default_factory=dict)
    _vehicle_cache: Dict[Tuple[int, int], List[VehicleOption]] = field(default_factory=dict)

    # --- disponibilité d'une recherche dans la logique ---------------------------------------
    def need(self, research_id: str):
        """None si gratuit, UNAVAILABLE si impossible, sinon la clé de l'item de recherche."""
        rid = _canon(research_id)
        if rid in self.free_research:
            return FREE
        if rid in self.node_research:
            return rid
        return UNAVAILABLE


_RESEARCH_CI: Dict[str, str] = {}


def _canon(rid: str) -> str:
    if not _RESEARCH_CI:
        for r in game_data()["research"]:
            _RESEARCH_CI[r["id"].lower()] = r["id"]
    return _RESEARCH_CI.get(rid.lower(), rid)


@lru_cache(maxsize=None)
def agency_model(agency: str) -> AgencyModel:
    g = game_data()
    a = g["agencies"][agency]
    start = set(a["start"]["unlock_at_start_research"]) | set(a["start"]["scenario_completed_research"])
    start = {_canon(r) for r in start}
    era_free = {_canon(r) for r, valid in a["era_reward_valid"].items() if valid}
    nodes = {_canon(r) for r in a["node_research"].values() if r}
    free = start | era_free
    return AgencyModel(
        name=agency,
        start_research=start,
        free_research=free,
        node_research=nodes - start,
        start_buildings=set(a["start"]["buildings"]),
        research_name={r["id"]: r["name"] for r in g["research"]},
    )


# --- sélection des items / locations d'une agence ---------------------------------------------

def agency_item_keys(agency: str) -> List[str]:
    """Clés (ids de recherche) des items de recherche de l'agence, dans l'ordre des tables."""
    model = agency_model(agency)
    return [i["key"] for i in item_table() if i["category"] == "research" and i["key"] in model.node_research]


def agency_research_location_keys(agency: str) -> List[str]:
    model = agency_model(agency)
    return [l["key"] for l in location_table() if l["category"] == "research" and l["key"] in model.node_research]


def agency_building_location_keys(agency: str) -> List[str]:
    g = game_data()
    model = agency_model(agency)
    valid = set(g["agencies"][agency]["blueprints_valid"])
    return [l["key"] for l in location_table()
            if l["category"] == "building" and l["key"] in valid and l["key"] not in model.start_buildings]


def milestone_location_keys() -> List[str]:
    return [l["key"] for l in location_table() if l["category"] == "milestone"]


# --- missions ----------------------------------------------------------------------------------

@lru_cache(maxsize=None)
def missions(agency: str) -> Dict[str, MissionInfo]:
    g = game_data()
    enums = g["enums"]["Distance"]
    payload_by_id = {p["id"]: p for p in g["payloads"]}
    a = g["agencies"][agency]
    out: Dict[str, MissionInfo] = {}
    for m in g["missions"]:
        if m["is_final_mars_mission"]:
            # Les variantes dépendent de la préparation martienne : on prend toutes celles de l'agence.
            ids = m["all_default_payloads"]
        else:
            ids = a["mission_default_payloads"].get(m["id"], [])
        crew_needed = max(m["min_crew"], 0)
        payloads: Dict[str, int] = {}
        for pid in ids:
            p = payload_by_id[pid]
            deps = p["agency_type_dependencies"]
            if deps and agency not in deps:
                continue
            if m["is_final_mars_mission"] or p["max_crew"] >= crew_needed:
                rid = _canon(p["research_id"])
                payloads[rid] = max(payloads.get(rid, 0), p["weight"])
        out[m["id"]] = MissionInfo(
            id=m["id"],
            distance=enums[m["distance"]],
            min_crew=crew_needed,
            payloads=sorted(payloads.items()),
            milestone=m["primary_milestone"],
            is_final=m["is_final_mars_mission"],
            is_required_for_mars=m["is_mars_required_mission"],
            order=m["mission_order"],
        )
    return out


@lru_cache(maxsize=None)
def launchpad_chain(size: str) -> Tuple[str, ...]:
    """Bâtiments de pas de tir nécessaires pour une taille (Medium dépend de Small, Large de Medium)."""
    return {
        "None": ("Building_LaunchPad_Small",),
        "Small": ("Building_LaunchPad_Small",),
        "Medium": ("Building_LaunchPad_Medium", "Building_LaunchPad_Small"),
        "Heavy": ("Building_LaunchPad_Large", "Building_LaunchPad_Medium", "Building_LaunchPad_Small"),
    }[size]


def building_chain(building_id: str) -> List[str]:
    """Le bâtiment et ses dépendances de construction (transitives)."""
    deps = {b["id"]: b["dependencies"] for b in game_data()["blueprints"]}
    out: List[str] = []
    stack = [building_id]
    while stack:
        b = stack.pop()
        if b in out:
            continue
        out.append(b)
        stack.extend(deps.get(b, []))
    return out


def _minimize(options: List[FrozenSet[str]]) -> List[FrozenSet[str]]:
    """Retire les ensembles qui contiennent strictement un autre ensemble."""
    unique = sorted(set(options), key=lambda s: (len(s), sorted(s)))
    kept: List[FrozenSet[str]] = []
    for s in unique:
        if not any(k <= s for k in kept):
            kept.append(s)
    return kept


def vehicle_options(agency: str, distance: int, weight: int) -> List[FrozenSet[str]]:
    """Combinaisons minimales d'items rendant possible un véhicule pour (distance, masse de payload).

    Reprend Simulation.IsAgencyVehicleDesignSuitableForMission (Simulation.cs:5285) :
      portée max >= distance ; capacité du booster (+ supplémentaire) >= masse de l'étage supérieur ;
      capacité de l'étage supérieur (ou de la fusée monobloc) >= masse du payload ;
      pas de tir de la taille du booster (Simulation.AgencyHasVehicleLaunchpad).
    """
    g = game_data()
    model = agency_model(agency)
    usable = set(g["agencies"][agency]["parts_usable"])
    parts = {p["id"]: p for p in g["vehicle_parts"] if p["id"] in usable}
    enums = g["enums"]["Distance"]

    def comp(part_id: str) -> Optional[str]:
        """Item nécessaire pour la pièce ; '' si gratuite ; None si inatteignable."""
        need = model.need(part_id)
        if need is FREE:
            return ""
        if need is UNAVAILABLE:
            return None
        return need

    def pad_items(size: str):
        out = []
        for building in launchpad_chain(size):
            need = model.need(building)
            if need is UNAVAILABLE:
                return None
            if need is not FREE:
                out.append(need)
        return out

    def finish(components: List[Optional[str]], size: str) -> Optional[FrozenSet[str]]:
        if any(c is None for c in components):
            return None
        pad = pad_items(size)
        if pad is None:
            return None
        return frozenset([c for c in components if c] + pad)

    options: List[FrozenSet[str]] = []
    for p in parts.values():
        if p["type"] == "Rocket" and enums[p["max_distance"]] >= distance and p["capacity"] >= weight:
            opt = finish([comp(p["id"])], p["size"])
            if opt is not None:
                options.append(opt)
    uppers = [p for p in parts.values() if p["type"] == "Upper" and enums[p["max_distance"]] >= distance and p["capacity"] >= weight]
    boosters = [p for p in parts.values() if p["type"] == "Booster"]
    for upper in uppers:
        for booster in boosters:
            if booster["capacity"] >= upper["mass"]:
                opt = finish([comp(upper["id"]), comp(booster["id"])], booster["size"])
                if opt is not None:
                    options.append(opt)
            for sup_id in booster["valid_supplementaries"]:
                sup = parts.get(sup_id)
                if sup and sup["type"] == "Supplementary" and booster["capacity"] + sup["capacity"] >= upper["mass"] > booster["capacity"]:
                    opt = finish([comp(upper["id"]), comp(booster["id"]), comp(sup_id)], booster["size"])
                    if opt is not None:
                        options.append(opt)
    return _minimize(options)
