"""Logique des missions et des bâtiments, calculée à partir des règles du jeu (voir data_model.py).

Principes (choisis avec l'utilisateur) :
  * l'arbre technologique avance dès qu'une recherche est *faite* : toutes les locations de recherche
    sont accessibles dès le départ ; seuls les bâtiments et les missions dépendent d'items ;
  * les récompenses d'ère sont automatiques (gratuites dans la logique) ;
  * une mission exige : sa recherche, un payload adapté (+ équipage), un véhicule capable et le pas de tir.
"""
from typing import Callable, Dict, FrozenSet, List, Optional, Set, Tuple

from .data_model import FREE, UNAVAILABLE, AgencyModel, building_chain, missions, vehicle_options

Rule = Callable[..., bool]

CREW_BUILDING = "Building_AstronautTraining"  # Simulation.GetAgencyRecruitTalentTier / MissionSelectScreen.cs:317

# Missions qui doivent être réussies avant une autre (la mission finale exige les missions « Mars requis »
# — Simulation.HasAgencyUnlockedFinalMarsMission — ; la 3e partie de la station modulaire suit les deux premières).
EXTRA_PREREQUISITES = {
    "milestone_modular_space_station_03": ["milestone_modular_space_station_01", "milestone_modular_space_station_02"],
}


class MissionLogic:
    """Exigences d'une mission, sous forme de noms d'items et d'événements."""

    def __init__(self, mission_id: str):
        self.mission_id = mission_id
        self.items: Set[str] = set()                      # tous requis
        self.alternatives: List[Tuple[Optional[str], str]] = []  # (item payload ou None, événement véhicule)
        self.prerequisites: List[str] = []
        self.possible = True


class LogicBuilder:
    def __init__(self, model: AgencyModel, item_name):
        self.model = model
        self.item_name = item_name            # clé de recherche -> nom d'item AP
        self.referenced: Set[str] = set()      # noms d'items cités par au moins une règle
        self.vehicle_classes: Dict[Tuple[int, int], List[FrozenSet[str]]] = {}
        self.missions = missions(model.name)
        self.mission_logic: Dict[str, MissionLogic] = {}

    # --- noms -----------------------------------------------------------------------------------
    def _need(self, research_id: str) -> Tuple[bool, Optional[str]]:
        """(possible, nom d'item ou None si gratuit)."""
        need = self.model.need(research_id)
        if need is FREE:
            return True, None
        if need is UNAVAILABLE:
            return False, None
        return True, self.item_name(need)

    def vehicle_event_name(self, distance: int, weight: int) -> str:
        return f"Vehicle Class d{distance} w{weight}"

    # --- missions ---------------------------------------------------------------------------------
    def build_missions(self) -> None:
        for mission_id, mission in self.missions.items():
            logic = MissionLogic(mission_id)
            possible, name = self._need(mission_id)
            if not possible:
                logic.possible = False
            elif name:
                logic.items.add(name)
            if mission.min_crew >= 1:
                for building in building_chain(CREW_BUILDING):
                    possible, name = self._need(building)
                    if not possible:
                        logic.possible = False
                    elif name:
                        logic.items.add(name)
            payloads = mission.payloads or ([] if mission_id != "milestone_sounding_rocket" else [("", 0)])
            if not mission.payloads and mission_id != "milestone_sounding_rocket":
                logic.possible = False  # aucune charge utile pour cette agence
            for rid, weight in payloads:
                if rid == "":
                    payload_item = None
                else:
                    ok, payload_item = self._need(rid)
                    if not ok:
                        continue
                key = (mission.distance, weight)
                if key not in self.vehicle_classes:
                    self.vehicle_classes[key] = [frozenset(self.item_name(k) for k in option)
                                                 for option in vehicle_options(self.model.name, *key)]
                if self.vehicle_classes[key]:
                    logic.alternatives.append((payload_item, self.vehicle_event_name(*key)))
            if not logic.alternatives:
                logic.possible = False
            if mission.is_final:
                logic.prerequisites += [m.id for m in self.missions.values() if m.is_required_for_mars]
            logic.prerequisites += EXTRA_PREREQUISITES.get(mission_id, [])
            self.mission_logic[mission_id] = logic

    def mission_possible(self, mission_id: str, _seen=None) -> bool:
        seen = _seen or set()
        if mission_id in seen:
            return True
        seen.add(mission_id)
        logic = self.mission_logic[mission_id]
        return logic.possible and all(self.mission_possible(p, seen) for p in logic.prerequisites if p in self.mission_logic)

    def _flatten(self, mission_id: str, seen: Set[str]) -> List[MissionLogic]:
        if mission_id in seen:
            return []
        seen.add(mission_id)
        logic = self.mission_logic[mission_id]
        out = [logic]
        for p in logic.prerequisites:
            if p in self.mission_logic:
                out += self._flatten(p, seen)
        return out

    def mission_rule(self, mission_id: str, player: int) -> Rule:
        chain = self._flatten(mission_id, set())
        all_items = sorted(set().union(*(l.items for l in chain)))
        groups = [tuple(l.alternatives) for l in chain]
        for l in chain:
            self.referenced |= l.items
            self.referenced |= {p for p, _ in l.alternatives if p}
        items = tuple(all_items)

        def rule(state) -> bool:
            if items and not state.has_all(items, player):
                return False
            for alts in groups:
                if not any((p is None or state.has(p, player)) and state.has(ev, player) for p, ev in alts):
                    return False
            return True

        return rule

    # --- bâtiments ---------------------------------------------------------------------------------
    def building_rule(self, building_id: str, player: int) -> Optional[Rule]:
        names: Set[str] = set()
        for b in building_chain(building_id):
            ok, name = self._need(b)
            if not ok:
                return None
            if name:
                names.add(name)
        self.referenced |= names
        items = tuple(sorted(names))
        if not items:
            return lambda state: True
        return lambda state: state.has_all(items, player)
