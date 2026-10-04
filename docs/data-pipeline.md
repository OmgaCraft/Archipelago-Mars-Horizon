# Pipeline de données (phase 1)

```
jeu (Data.instance)  ──plugin [Dump]──▶  tools/output/dump/game_data.json   (brut, ignoré par git)
                                                  │
                                       tools/build_data.py
                                                  ▼
             apworld/mars_horizon/data/{ids,game_data,items,locations}.json   (commités)
             docs/data-report.md                                             (commité)
```

## Régénérer

```bash
dotnet build plugin/MarsHorizonAP/MarsHorizonAP.csproj -c Release
powershell -File tools/dump_game_data.ps1 -GameDir "D:\SteamApp\steamapps\common\Mars Horizon"
python tools/build_data.py --game-dir "D:\SteamApp\steamapps\common\Mars Horizon"
python -m unittest discover -s tools/tests
```

- `dump_game_data.ps1` active `[Dump]` dans `BepInEx/config/archipelago.marshorizon.cfg`, lance le jeu
  via Steam, attend le JSON (le jeu se ferme seul, `QuitAfter = true`), puis remet `Enabled = false`.
- `build_data.py --check` ne réécrit rien et échoue si la table d'IDs n'est pas à jour (pour la CI).

## Le dump (`plugin/MarsHorizonAP/Dump/GameDataDumper.cs`)

- Déclenché au premier `TitleScreen.OnVisible` : les données et la localisation sont chargées.
- Exporte `Data.instance` : recherches, 3 arbres techno (nœuds, connecteurs, `nodes_required`,
  récompenses d'ère), bâtiments, pièces, améliorations, payloads, missions, jalons, installations,
  scénario, règles utiles, noms **en** et **fr**.
- **Résolution par agence avec le code du jeu** (`TechNodeData.GetResearch`, `Research.IsValidForAgency`,
  `VehiclePart.CanAgencyUsePart`, `Simulation.GetAgencyIsBlueprintValid`,
  `MissionTemplate.GetDefaultPayloads`). Ces méthodes lisent `Controller.Instance.simulation` : le
  dumper installe un `Host` temporaire autour d'une `Simulation` sans univers, et règle
  `data.version = Application.version` comme `Host.cs:48`.
- Noms : renvois `{Tag}` résolus comme `Localisation.Interpolate` (`FP/Localisation.cs:386`).

## Table d'IDs (`apworld/mars_horizon/data/ids.json`)

- Base **7 658 100 000** (appid Steam 765810 × 10 000). Plages par catégorie, sans chevauchement :

  | Catégorie | Plage | Contenu |
  |---|---|---|
  | `items.research` | 7658100001–7658109999 | déblocage d'une recherche (clé = id de recherche du jeu) |
  | `items.bundle` | 7658110000–7658119999 | réservé : paquets (phase 2) |
  | `items.filler` | 7658120000–7658129999 | réservé : argent / science / soutien |
  | `items.trap` | 7658130000–7658139999 | réservé : pièges (phase 5) |
  | `locations.research` | 7658200000–7658209999 | recherche d'un nœud terminée |
  | `locations.building` | 7658210000–7658219999 | première construction d'un bâtiment |
  | `locations.milestone` | 7658220000–7658229999 | jalon atteint |
  | `locations.other` | 7658290000–7658299999 | réservé |

- **Clés = ids internes du jeu**, pas les noms : la table reste valable quelle que soit la décision
  sur l'agence ou la granularité. Les variantes d'agence d'un même nœud ont chacune leur ID ; un slot
  n'utilisera que celles de son agence.
- **Ajout seul.** Un ID attribué ne change jamais. Une clé qui disparaît du jeu passe dans `retired` et
  son ID n'est jamais réattribué (il revient si la clé réapparaît). Vérifié par `tools/tests/test_ids.py`.
- Superset volontaire : récompenses d'ère et bâtiments de départ ont un ID même si l'APWorld peut les
  exclure.

## Fichiers générés

- `game_data.json` : tout ce dont l'APWorld a besoin pour la logique (arbres, connecteurs, pièces avec
  `size`/`max_distance`/`capacity`, payloads avec `weight`, missions avec `distance`/`min_crew`/payloads
  par agence, état de départ par agence).
- `items.json` / `locations.json` : listes candidates (id, clé, nom anglais unique, agences, arbre, ère).
  Doublons départagés par la catégorie du jeu (« Booster », « Upper Stage »…), puis l'agence, puis l'ère.
- `docs/data-report.md` : comptes, couverture, variantes, état de départ.

## Constats de la phase 1

- 268 recherches, toutes portées par un nœud (256) ou une récompense d'ère (12) — aucune orpheline.
  150 nœuds techno (Base 32, Missions 64, Vehicles 54), arbres sains (tout nœud atteignable, connecteurs
  satisfiables).
- 48 nœuds ont des variantes d'agence (surtout fusées et payloads). Par agence : ~140 items de
  recherche (hors départ) pour ~207 locations → **~67 places de filler** par slot.
- 39 missions, toutes débloquées par une recherche du même id ; 39 jalons, un par mission.
- Départ (`Scenario_Default`, toutes agences) : HQ + Vehicle Hangar construits ; recherches
  `Building_HQ`, `Building_VehicleHangar`, `Rocket_SoundingRocket`, `milestone_sounding_rocket`.
  **Pas de pas de tir** : `Building_LaunchPad_Small` (100 science) est la première recherche du jeu de
  base → à garantir en sphère 1 (option « techno de départ garantie » du PDF).
- Mission finale débloquée quand toutes les missions `isMarsRequiredMission` sont réussies
  (`Simulation.HasAgencyUnlockedFinalMarsMission`, `Simulation.cs:3922`) :
  `milestone_mars_mission_engine`, `milestone_mars_mission_lander`.
- Contenu inutilisé : `payload_sojourner` (ni recherche ni mission) ; 5 tags de traduction sans donnée
  (`mechanic_upgrades`, `payload_commsat`…).
- La version des données vaut `1.4.2.0` à l'écran titre et `1.4.2.1` (`Application.version`) en partie.
