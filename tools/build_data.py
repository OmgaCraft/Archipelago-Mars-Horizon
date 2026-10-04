#!/usr/bin/env python3
"""Phase 1 : construit les données de l'APWorld à partir du dump du jeu.

Entrée :
  tools/output/dump/game_data.json      écrit par le plugin (section [Dump] de sa config,
                                        voir tools/dump_game_data.ps1)
Sorties :
  apworld/mars_horizon/data/game_data.json   données nettoyées pour l'APWorld (logique, noms)
  apworld/mars_horizon/data/ids.json         table d'IDs stable : ajout seul, jamais de réutilisation
  apworld/mars_horizon/data/items.json       liste candidate des items
  apworld/mars_horizon/data/locations.json   liste candidate des locations
  docs/data-report.md                        rapport de couverture

Usage :
  python tools/build_data.py            régénère tout
  python tools/build_data.py --check    vérifie sans écrire (code retour 1 si la table d'IDs changerait)
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / "tools" / "output" / "dump" / "game_data.json"
DATA_DIR = ROOT / "apworld" / "mars_horizon" / "data"
REPORT = ROOT / "docs" / "data-report.md"

GAME = "Mars Horizon"
# Steam appid 765810 x 10 000 : base propre au jeu, loin des petits entiers.
BASE_ID = 7_658_100_000
# Plages (décalages depuis BASE_ID). Une catégorie ne déborde jamais sur une autre.
RANGES: dict[tuple[str, str], tuple[int, int]] = {
    ("items", "research"): (1, 9_999),          # déblocage d'une recherche (nœud techno, variante d'agence)
    ("items", "bundle"): (10_000, 19_999),      # réservé : paquets par branche/ère (phase 2)
    ("items", "filler"): (20_000, 29_999),      # réservé : argent, science, soutien (phase 2)
    ("items", "trap"): (30_000, 39_999),        # réservé : pièges (phase 5)
    ("locations", "research"): (100_000, 109_999),   # recherche terminée
    ("locations", "building"): (110_000, 119_999),   # première construction d'un bâtiment
    ("locations", "milestone"): (120_000, 129_999),  # jalon atteint (mission réussie)
    ("locations", "other"): (190_000, 199_999),      # réservé
}
# Filler (phase 2) : clé stable -> nom. Le montant appliqué en jeu est décidé côté plugin / slot_data.
FILLER_ITEMS = {
    "Filler_Funds": "Funding Grant",
    "Filler_Science": "Research Data",
    "Filler_Support": "Public Support",
}
TREE_ORDER = ["Base", "Missions", "Vehicles"]
AGENCIES = ["USA", "Russia", "Europe", "China", "Japan"]
DEFAULT_SCENARIO = "Scenario_Default"


# --------------------------------------------------------------------------- lecture

def load_dump() -> dict:
    if not DUMP.exists():
        sys.exit(f"Dump introuvable : {DUMP}\nLancer tools/dump_game_data.ps1 (ou activer [Dump] dans la config du plugin).")
    with DUMP.open(encoding="utf-8") as f:
        return json.load(f)


def name_of(entry: dict, lang: str = "en") -> str | None:
    name = entry.get("name") or {}
    return name.get(lang)


# --------------------------------------------------------------------------- modèle

class GameModel:
    """Index du dump : recherches, nœuds, variantes par agence."""

    def __init__(self, dump: dict):
        self.dump = dump
        self.research = {r["id"]: r for r in dump["research"]}
        self.research_ci = {r["id"].lower(): r["id"] for r in dump["research"]}
        self.trees = {t["type"]: t for t in dump["tech_trees"]}
        self.blueprints = {b["id"]: b for b in dump["blueprints"]}
        self.parts = {p["id"]: p for p in dump["vehicle_parts"]}
        self.payloads = {p["id"]: p for p in dump["payloads"]}
        self.missions = {m["id"]: m for m in dump["mission_templates"]}
        self.milestones = {m["id"]: m for m in dump["milestones"]}
        self.agencies = dump["agencies"]

        # nœud techno -> infos ; recherche -> nœud
        self.tech_nodes: list[dict] = []
        self.node_of_research: dict[str, dict] = {}
        for tree_type in self.tree_types():
            tree = self.trees[tree_type]
            for node in tree["nodes"]:
                if "ids" not in node:
                    continue
                node = dict(node, tree=tree_type, key=f"{tree_type}:{node['index']}")
                self.tech_nodes.append(node)
                for rid in node["ids"]:
                    self.node_of_research.setdefault(self.canon(rid), node)

        # récompenses d'ère : recherche -> récompense
        self.era_reward_of_research: dict[str, dict] = {}
        for tree_type in self.tree_types():
            for reward in self.trees[tree_type]["era_rewards"]:
                for rid in reward.get("ids") or []:
                    self.era_reward_of_research.setdefault(self.canon(rid), dict(reward, tree=tree_type))

    def tree_types(self) -> list[str]:
        known = [t for t in TREE_ORDER if t in self.trees]
        return known + sorted(t for t in self.trees if t not in TREE_ORDER)

    def canon(self, rid: str) -> str:
        """Id de recherche dans la casse du CSV (le jeu compare sans tenir compte de la casse)."""
        return self.research_ci.get(rid.lower(), rid)

    def node_sort_key(self, node: dict):
        return (self.tree_types().index(node["tree"]), node.get("era", 0), node.get("row", 0), node.get("column", 0), node["index"])

    def agencies_for_research(self, rid: str) -> list[str]:
        out = []
        for agency in AGENCIES:
            data = self.agencies.get(agency, {})
            if rid in data.get("research_valid", []):
                out.append(agency)
        return out

    def node_research_for(self, agency: str, node: dict) -> str | None:
        return self.agencies.get(agency, {}).get("node_research", {}).get(node["key"])


# --------------------------------------------------------------------------- couverture

def coverage(model: GameModel) -> dict[str, list]:
    issues: dict[str, list] = defaultdict(list)
    d = model.dump

    in_nodes = set(model.node_of_research)
    in_rewards = set(model.era_reward_of_research)
    for rid in model.research:
        if rid not in in_nodes and rid not in in_rewards:
            issues["research_orphan"].append(rid)
    for node in model.tech_nodes:
        for rid in node["ids"]:
            if rid.lower() not in model.research_ci:
                issues["node_id_without_research"].append(f"{node['key']} {rid}")
        resolved = [model.node_research_for(a, node) for a in AGENCIES]
        if not any(resolved):
            issues["node_without_any_agency"].append(f"{node['key']} {node['ids']}")
    for rid in in_rewards:
        if rid not in model.research:
            issues["era_reward_without_research"].append(rid)

    for pid, part in model.parts.items():
        if pid.lower() not in model.research_ci:
            issues["part_without_research"].append(f"{pid} ({part['type']})")
    for pid, payload in model.payloads.items():
        if (payload.get("research_id") or pid).lower() not in model.research_ci:
            issues["payload_without_research"].append(pid)
    for bid in model.blueprints:
        if bid.lower() not in model.research_ci:
            issues["blueprint_without_research"].append(bid)
    for mid, mission in model.missions.items():
        if mid.lower() not in model.research_ci:
            issues["mission_without_research"].append(mid)
        milestone = mission.get("primary_milestone")
        if milestone and milestone not in model.milestones:
            issues["mission_milestone_unknown"].append(f"{mid} -> {milestone}")
    missions_by_milestone = {m.get("primary_milestone") for m in model.missions.values()}
    for mid in model.milestones:
        if mid not in missions_by_milestone:
            issues["milestone_without_mission"].append(mid)

    for kind, entries in (("research", d["research"]), ("blueprint", d["blueprints"]),
                          ("milestone", d["milestones"]), ("mission", d["mission_templates"])):
        for e in entries:
            if not name_of(e):
                issues[f"{kind}_without_english_name"].append(e["id"])

    for tree_type in model.tree_types():
        nodes = model.trees[tree_type]["nodes"]
        roots = [n for n in nodes if n["class"] == "RootNodeData"]
        if len(roots) != 1:
            issues["tree_root_count"].append(f"{tree_type}: {len(roots)}")
        # atteignabilité depuis la racine (arêtes parent -> enfant)
        seen, stack = set(), [n["index"] for n in roots]
        while stack:
            i = stack.pop()
            if i in seen or i < 0:
                continue
            seen.add(i)
            stack.extend(nodes[i].get("children", []))
        for n in nodes:
            if n["index"] not in seen:
                issues["node_unreachable"].append(f"{tree_type}:{n['index']} {n.get('ids', n['class'])}")
            if n["class"] == "ConnectorNodeData" and n.get("nodes_required", 0) > len(n.get("parents", [])):
                issues["connector_requirement_impossible"].append(f"{tree_type}:{n['index']}")
            for child in n.get("children", []):
                if child < 0 or n["index"] not in nodes[child].get("parents", []):
                    issues["edge_not_symmetric"].append(f"{tree_type}:{n['index']}->{child}")

    lower = Counter(r.lower() for r in model.research)
    for rid, n in lower.items():
        if n > 1:
            issues["research_duplicate_case_insensitive"].append(rid)

    issues["dump_errors"] = list(d.get("errors", []))
    return {k: sorted(v) for k, v in issues.items() if v}


# --------------------------------------------------------------------------- clés items / locations

def localisation_crosscheck(model: GameModel, game_dir: Path | None) -> dict[str, list] | None:
    """Compare les tags Name_Research_* des CSV de traduction du jeu aux recherches du dump."""
    if game_dir is None:
        return None
    folder = game_dir / "Mars Horizon_Data" / "StreamingAssets" / "Localisations"
    if not folder.is_dir():
        return None
    tags: set[str] = set()
    for path in sorted(folder.glob("Base*.csv")):
        with path.open(encoding="utf-8-sig", newline="") as f:
            for row in csv.reader(f):
                if row and row[0].lower().startswith("name_research_"):
                    tags.add(row[0][len("name_research_"):].lower())
    known = set(model.research_ci)
    return {
        "named_in_localisation_but_not_in_dump": sorted(tags - known),
        "in_dump_but_not_named_name_research": sorted(known - tags),
    }


def research_keys(model: GameModel) -> list[str]:
    """Toutes les recherches portées par un nœud, dans l'ordre de l'arbre, puis les récompenses d'ère."""
    keys: list[str] = []
    seen: set[str] = set()
    for node in sorted(model.tech_nodes, key=model.node_sort_key):
        for rid in node["ids"]:
            rid = model.canon(rid)
            if rid in model.research and rid not in seen:
                seen.add(rid)
                keys.append(rid)
    rewards = sorted(model.era_reward_of_research.items(),
                     key=lambda kv: (model.tree_types().index(kv[1]["tree"]), kv[1]["era"], kv[0]))
    for rid, _ in rewards:
        if rid in model.research and rid not in seen:
            seen.add(rid)
            keys.append(rid)
    return keys


def node_research_keys(model: GameModel) -> list[str]:
    """Recherches des nœuds (hors récompenses d'ère, accordées automatiquement par le jeu)."""
    return [k for k in research_keys(model) if k in model.node_of_research]


def building_keys(model: GameModel) -> list[str]:
    order = {rid: i for i, rid in enumerate(research_keys(model))}
    return sorted(model.blueprints, key=lambda b: (order.get(model.canon(b), len(order)), b))


def milestone_keys(model: GameModel) -> list[str]:
    order: dict[str, int] = {}
    for m in sorted(model.missions.values(), key=lambda m: (m.get("mission_order", 0), m["id"])):
        if m.get("primary_milestone"):
            order.setdefault(m["primary_milestone"], len(order))
    return sorted(model.milestones, key=lambda mid: (order.get(mid, len(order)), mid))


# --------------------------------------------------------------------------- table d'IDs

def empty_table() -> dict:
    return {
        "game": GAME,
        "base_id": BASE_ID,
        "note": "Table append-only. Ne jamais modifier ni réutiliser un ID existant. Généré par tools/build_data.py.",
        "ranges": {f"{k}.{c}": [BASE_ID + lo, BASE_ID + hi] for (k, c), (lo, hi) in RANGES.items()},
        "items": {},
        "locations": {},
        "retired": {"items": {}, "locations": {}},
    }


def assign_ids(table: dict, kind: str, category: str, keys: list[str], log: list[str]) -> None:
    lo, hi = RANGES[(kind, category)]
    lo, hi = BASE_ID + lo, BASE_ID + hi
    section: dict[str, int] = table[kind].setdefault(category, {})
    retired: dict[str, int] = table["retired"][kind].setdefault(category, {})

    for key, value in list(section.items()) + list(retired.items()):
        if not lo <= value <= hi:
            raise SystemExit(f"ID hors plage : {kind}.{category} {key} = {value}")

    wanted = set(keys)
    for key in list(section):
        if key not in wanted:
            retired[key] = section.pop(key)
            log.append(f"retiré   {kind}.{category} {key} ({retired[key]})")

    used = set(section.values()) | set(retired.values())
    next_id = max(used, default=lo - 1) + 1
    for key in keys:
        if key in section:
            continue
        if key in retired:
            section[key] = retired.pop(key)
            log.append(f"restauré {kind}.{category} {key} ({section[key]})")
            continue
        if next_id > hi:
            raise SystemExit(f"Plage pleine : {kind}.{category}")
        section[key] = next_id
        log.append(f"ajouté   {kind}.{category} {key} ({next_id})")
        next_id += 1

    values = list(section.values()) + list(retired.values())
    if len(values) != len(set(values)):
        raise SystemExit(f"ID dupliqué dans {kind}.{category}")


# --------------------------------------------------------------------------- noms

def unique_names(entries: list[dict], label) -> None:
    """Fixe entry['name'] ; départage les doublons par catégorie de nœud, puis agence, ère, id du jeu."""
    for e in entries:
        e["name"] = label(e)

    def by_agencies(e: dict) -> str | None:
        agencies = e.get("agencies") or []
        return "/".join(agencies) if 0 < len(agencies) < len(AGENCIES) else None

    qualifiers = (
        lambda e: e.get("node_category"),
        by_agencies,
        lambda e: f"Era {e['era']}" if e.get("era") is not None else None,
        lambda e: e["key"],
    )
    for qualifier in qualifiers:
        groups: dict[str, list[dict]] = defaultdict(list)
        for e in entries:
            groups[e["name"]].append(e)
        for name, group in groups.items():
            if len(group) < 2:
                continue
            quals = [qualifier(e) for e in group]
            if len(set(quals)) > 1 and all(quals):
                for e, q in zip(group, quals):
                    e["name"] = f"{name} ({q})"
    if len({e["name"] for e in entries}) != len(entries):
        raise SystemExit("Noms en double après désambiguïsation")


# --------------------------------------------------------------------------- sorties

def build_game_data(model: GameModel) -> dict:
    d = model.dump
    trees = []
    for tree_type in model.tree_types():
        tree = model.trees[tree_type]
        trees.append({
            "type": tree_type,
            "nodes": [{k: n[k] for k in ("index", "class", "category", "era", "ids", "parents", "children", "nodes_required", "row", "column") if k in n}
                      for n in tree["nodes"]],
            "era_rewards": tree["era_rewards"],
        })

    scenario = next((s for s in d["scenarios"] if s["id"] == DEFAULT_SCENARIO), None)
    start: dict[str, dict] = {}
    for agency in AGENCIES:
        setup = next((a for a in (scenario or {}).get("agencies", []) if a["type"] == agency), {})
        at_start = sorted({
            model.node_research_for(agency, n)
            for n in model.tech_nodes
            if model.node_research_for(agency, n)
            and model.research.get(model.node_research_for(agency, n), {}).get("unlock_at_start")
        })
        start[agency] = {
            "is_active": setup.get("is_active"),
            "available_to_player": setup.get("available_to_player"),
            "unlock_at_start_research": at_start,
            "scenario_completed_research": setup.get("completed_research") or [],
            "buildings": (setup.get("base_layout_buildings") or []) + (setup.get("buildings") or []),
            "milestones": setup.get("milestones") or [],
            "funds": setup.get("funds"),
            "support": setup.get("support"),
        }

    def strip(entry: dict, keys: tuple[str, ...]) -> dict:
        out = {k: entry.get(k) for k in keys}
        out["name"] = name_of(entry)
        out["name_fr"] = name_of(entry, "fr")
        return out

    return {
        "meta": d["meta"],
        "rules": d["rules"],
        "enums": d["enums"],
        "tech_trees": trees,
        "research": [strip(r, ("id", "unlock_at_start", "cost", "expertise", "valid_agencies", "added_in_version"))
                     | {"agencies": model.agencies_for_research(r["id"])} for r in d["research"]],
        "blueprints": [strip(b, ("id", "category", "build_cost", "upkeep_cost", "build_time", "initial_build_limit",
                                 "is_critical", "effect", "dependencies", "agency_type_dependencies", "limit_dependencies"))
                       for b in d["blueprints"]],
        "vehicle_parts": [strip(p, ("id", "type", "size", "fuel", "max_distance", "mass", "capacity", "reliability",
                                    "supplementary_count", "valid_supplementaries", "launchpad", "agency_type_dependencies"))
                          for p in d["vehicle_parts"]],
        "payloads": [strip(p, ("id", "research_id", "agency_type_dependencies", "is_mars_mission_payload", "weight",
                               "max_crew", "power_capacity", "min_version"))
                     for p in d["payloads"]],
        "missions": [strip(m, ("id", "mission_order", "planetary_body", "origin_body", "distance", "min_crew",
                               "required_installations", "default_payloads", "all_default_payloads", "primary_milestone",
                               "is_mars_preparation_mission", "is_mars_required_mission", "is_final_mars_mission",
                               "installation_id", "phase_count", "request_weighting"))
                     for m in d["mission_templates"]],
        "milestones": [strip(m, ("id", "location", "difficulty")) for m in d["milestones"]],
        "installations": d["installations"],
        "agencies": {a: {"node_research": d["agencies"][a]["node_research"],
                         "era_reward_valid": d["agencies"][a]["era_reward_valid"],
                         "parts_usable": d["agencies"][a]["parts_usable"],
                         "blueprints_valid": d["agencies"][a]["blueprints_valid"],
                         "mission_default_payloads": d["agencies"][a]["mission_default_payloads"],
                         "start": start[a]}
                     for a in AGENCIES if a in d["agencies"]},
    }


def category_name(node: dict) -> str:
    return (node.get("category") or {}).get("en") or node["class"].replace("TechNodeData", "")


def build_lists(model: GameModel, table: dict) -> tuple[list[dict], list[dict]]:
    def research_label(e: dict) -> str:
        return name_of(model.research[e["key"]]) or e["key"]

    items = []
    for rid, item_id in table["items"]["research"].items():
        node = model.node_of_research.get(rid)
        reward = model.era_reward_of_research.get(rid)
        items.append({
            "id": item_id, "key": rid, "category": "research",
            "tree": node["tree"] if node else reward["tree"],
            "node_class": node["class"] if node else f"EraReward:{reward['type']}",
            "era": node["era"] if node else reward["era"],
            "era_reward": node is None,
            "node_category": category_name(node) if node else "Era Reward",
            "agencies": model.agencies_for_research(rid),
        })
    unique_names(items, research_label)
    for key, item_id in table["items"]["filler"].items():
        items.append({"id": item_id, "key": key, "category": "filler", "name": FILLER_ITEMS[key]})
    if len({i["name"] for i in items}) != len(items):
        raise SystemExit("Nom de filler en conflit avec un item de recherche")

    locations = []
    for rid, loc_id in table["locations"]["research"].items():
        node = model.node_of_research[rid]
        locations.append({"id": loc_id, "key": rid, "category": "research", "tree": node["tree"], "era": node["era"],
                          "node_category": category_name(node), "agencies": model.agencies_for_research(rid)})
    unique_names(locations, lambda e: f"Research: {research_label(e)}")
    buildings = []
    for bid, loc_id in table["locations"]["building"].items():
        agencies = [a for a in AGENCIES if bid in model.agencies.get(a, {}).get("blueprints_valid", [])]
        buildings.append({"id": loc_id, "key": bid, "category": "building", "agencies": agencies})
    unique_names(buildings, lambda e: f"Build: {name_of(model.blueprints[e['key']]) or e['key']}")
    milestones = []
    for mid, loc_id in table["locations"]["milestone"].items():
        m = model.milestones[mid]
        milestones.append({"id": loc_id, "key": mid, "category": "milestone", "location": m["location"]})
    unique_names(milestones, lambda e: f"Milestone: {name_of(model.milestones[e['key']]) or e['key']}")
    locations += buildings + milestones
    return items, locations


def write_report(model: GameModel, issues: dict, crosscheck: dict | None, items: list[dict], locations: list[dict], game_data: dict,
                 table: dict, id_log: list[str]) -> str:
    d = model.dump
    lines = [
        "# Rapport de données (phase 1)",
        "",
        "Généré par `tools/build_data.py` depuis le dump du plugin. Ne pas éditer à la main.",
        "",
        f"- Jeu : v{d['meta']['game_version']} (Unity {d['meta']['unity_version']}), dump du {d['meta']['dumped_at_utc'][:19]} UTC",
        f"- Recherches : {len(d['research'])} · bâtiments : {len(d['blueprints'])} · pièces : {len(d['vehicle_parts'])}"
        f" · payloads : {len(d['payloads'])} · missions : {len(d['mission_templates'])} · jalons : {len(d['milestones'])}",
        f"- Items candidats : {len(items)} · locations candidates : {len(locations)}",
        "",
        "## Arbres technologiques",
        "",
        "| Arbre | Nœuds techno | Connecteurs | Récompenses d'ère | Nœuds par ère |",
        "|---|---|---|---|---|",
    ]
    for tree_type in model.tree_types():
        tree = model.trees[tree_type]
        techs = [n for n in tree["nodes"] if "ids" in n]
        conns = [n for n in tree["nodes"] if n["class"] == "ConnectorNodeData"]
        per_era = Counter(n["era"] for n in techs)
        lines.append(f"| {tree_type} | {len(techs)} | {len(conns)} | {len(tree['era_rewards'])} | "
                     + ", ".join(f"ère {e}: {c}" for e, c in sorted(per_era.items())) + " |")
    lines += ["", "Nœuds par type :", ""]
    by_class = Counter((n["tree"], n["class"]) for n in model.tech_nodes)
    for (tree, cls), c in sorted(by_class.items()):
        lines.append(f"- {tree} / {cls} : {c}")

    lines += ["", "## Par agence", "",
              "| Agence | Nœuds résolus | Recherches valides | Pièces utilisables | Bâtiments valides | Recherches au départ | Bâtiments au départ |",
              "|---|---|---|---|---|---|---|"]
    for agency in AGENCIES:
        a = d["agencies"].get(agency)
        if not a:
            continue
        resolved = sum(1 for v in a["node_research"].values() if v)
        st = game_data["agencies"][agency]["start"]
        n_start = len(set(st["unlock_at_start_research"]) | set(st["scenario_completed_research"]))
        lines.append(f"| {agency} | {resolved}/{len(a['node_research'])} | {len(a['research_valid'])} | {len(a['parts_usable'])} "
                     f"| {len(a['blueprints_valid'])} | {n_start} | {len(st['buildings'])} |")

    lines += ["", "### Variantes par agence", "",
              "Nœuds dont la recherche diffère selon l'agence (même nœud, ids différents) :", ""]
    variant_nodes = 0
    for node in sorted(model.tech_nodes, key=model.node_sort_key):
        resolved = {a: model.node_research_for(a, node) for a in AGENCIES}
        if len({v for v in resolved.values() if v}) > 1:
            variant_nodes += 1
    lines.append(f"- {variant_nodes} nœuds sur {len(model.tech_nodes)} ont des variantes d'agence.")
    absent = Counter()
    for node in model.tech_nodes:
        for a in AGENCIES:
            if not model.node_research_for(a, node):
                absent[a] += 1
    lines.append("- Nœuds absents pour l'agence : " + ", ".join(f"{a} {absent[a]}" for a in AGENCIES))

    lines += ["", "### État de départ (Scenario_Default)", ""]
    for agency in AGENCIES:
        st = game_data["agencies"].get(agency, {}).get("start")
        if not st:
            continue
        research = sorted(set(st["unlock_at_start_research"]) | set(st["scenario_completed_research"]))
        lines.append(f"- **{agency}** — recherches : {', '.join(research) or '—'} ; bâtiments : {', '.join(st['buildings']) or '—'}")

    lines += ["", "## Missions", ""]
    request_only = [m for m in d["mission_templates"] if m["id"].lower() not in model.research_ci]
    lines.append(f"- {len(d['mission_templates'])} modèles, dont {len(request_only)} sans recherche du même id "
                 "(missions « request » ou débloquées autrement — hors logique par défaut).")
    lines.append(f"- Missions requises pour Mars : {', '.join(m['id'] for m in d['mission_templates'] if m['is_mars_required_mission']) or '—'}")
    lines.append(f"- Mission finale : {', '.join(m['id'] for m in d['mission_templates'] if m['is_final_mars_mission']) or '—'}")
    by_body = Counter(m["planetary_body"] for m in d["mission_templates"] if m["id"].lower() in model.research_ci)
    lines.append("- Missions débloquées par recherche, par corps : " + ", ".join(f"{b} {c}" for b, c in by_body.most_common()))

    lines += ["", "## Couverture", ""]
    if not issues:
        lines.append("Aucun problème détecté.")
    for key, values in issues.items():
        lines.append(f"### {key} ({len(values)})")
        lines.append("")
        for v in values[:200]:
            lines.append(f"- `{v}`")
        if len(values) > 200:
            lines.append(f"- … {len(values) - 200} de plus")
        lines.append("")

    lines += ["## Recoupement avec les fichiers de localisation", ""]
    if crosscheck is None:
        lines.append("Non effectué (passer `--game-dir` ou définir `MARS_HORIZON_DIR`).")
    else:
        for key, values in crosscheck.items():
            lines.append(f"- {key} ({len(values)}) : " + (", ".join(f"`{v}`" for v in values) or "aucun"))

    lines += ["", "## Table d'IDs", ""]
    for kind in ("items", "locations"):
        for category, (lo, hi) in ((c, r) for (k, c), r in RANGES.items() if k == kind):
            n = len(table[kind].get(category, {}))
            lines.append(f"- {kind}.{category} : {n} IDs (plage {BASE_ID + lo}–{BASE_ID + hi})")
    if id_log:
        lines += ["", f"Changements lors de ce run : {len(id_log)} (détail dans la sortie console)."]
    return "\n".join(lines) + "\n"


def dump_json(path: Path, data) -> str:
    return json.dumps(data, ensure_ascii=False, indent=1) + "\n"


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="ne rien écrire ; échoue si la table d'IDs changerait")
    parser.add_argument("--game-dir", type=Path, default=Path(os.environ["MARS_HORIZON_DIR"]) if os.environ.get("MARS_HORIZON_DIR") else None,
                        help="dossier du jeu, pour recouper avec les CSV de localisation (défaut : MARS_HORIZON_DIR)")
    args = parser.parse_args()

    model = GameModel(load_dump())
    issues = coverage(model)
    crosscheck = localisation_crosscheck(model, args.game_dir)

    ids_path = DATA_DIR / "ids.json"
    table = json.loads(ids_path.read_text(encoding="utf-8")) if ids_path.exists() else empty_table()
    id_log: list[str] = []
    assign_ids(table, "items", "research", research_keys(model), id_log)
    assign_ids(table, "items", "filler", list(FILLER_ITEMS), id_log)
    assign_ids(table, "locations", "research", node_research_keys(model), id_log)
    assign_ids(table, "locations", "building", building_keys(model), id_log)
    assign_ids(table, "locations", "milestone", milestone_keys(model), id_log)

    game_data = build_game_data(model)
    items, locations = build_lists(model, table)
    report = write_report(model, issues, crosscheck, items, locations, game_data, table, id_log)

    outputs = {
        ids_path: dump_json(ids_path, table),
        DATA_DIR / "game_data.json": dump_json(DATA_DIR / "game_data.json", game_data),
        DATA_DIR / "items.json": dump_json(DATA_DIR / "items.json", items),
        DATA_DIR / "locations.json": dump_json(DATA_DIR / "locations.json", locations),
        REPORT: report,
    }

    for line in id_log:
        print(line)
    print(f"items : {len(items)} · locations : {len(locations)} · problèmes de couverture : "
          + (", ".join(f"{k}={len(v)}" for k, v in issues.items()) or "aucun"))

    if args.check:
        current = ids_path.read_text(encoding="utf-8").replace("\r\n", "\n") if ids_path.exists() else ""
        if current != outputs[ids_path]:
            print("La table d'IDs n'est pas à jour.", file=sys.stderr)
            return 1
        return 0

    DATA_DIR.mkdir(parents=True, exist_ok=True)
    for path, text in outputs.items():
        path.write_text(text, encoding="utf-8", newline="\n")
        print(f"écrit {path.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
