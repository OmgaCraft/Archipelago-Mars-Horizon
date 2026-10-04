# Rapport de données (phase 1)

Généré par `tools/build_data.py` depuis le dump du plugin. Ne pas éditer à la main.

- Jeu : v1.4.2.1 (Unity 2019.4.21f1), dump du 2026-10-04T11:59:39 UTC
- Recherches : 268 · bâtiments : 30 · pièces : 114 · payloads : 89 · missions : 39 · jalons : 39
- Items candidats : 271 · locations candidates : 325

## Arbres technologiques

| Arbre | Nœuds techno | Connecteurs | Récompenses d'ère | Nœuds par ère |
|---|---|---|---|---|
| Base | 32 | 24 | 4 | ère 0: 3, ère 1: 8, ère 2: 12, ère 3: 7, ère 4: 2 |
| Missions | 64 | 46 | 4 | ère 0: 1, ère 1: 10, ère 2: 14, ère 3: 19, ère 4: 20 |
| Vehicles | 54 | 13 | 4 | ère 0: 1, ère 1: 13, ère 2: 13, ère 3: 16, ère 4: 11 |

Nœuds par type :

- Base / BuildingLimitTechNodeData : 3
- Base / BuildingTechNodeData : 29
- Missions / MissionTechNodeData : 37
- Missions / PayloadTechNodeData : 27
- Vehicles / BoosterTechNodeData : 17
- Vehicles / MechanicTechNodeData : 1
- Vehicles / SupplementaryTechNodeData : 11
- Vehicles / UpperTechNodeData : 23
- Vehicles / VehicleTechNodeData : 2

## Par agence

| Agence | Nœuds résolus | Recherches valides | Pièces utilisables | Bâtiments valides | Recherches au départ | Bâtiments au départ |
|---|---|---|---|---|---|---|
| USA | 144/150 | 156 | 48 | 30 | 4 | 2 |
| Russia | 144/150 | 156 | 48 | 30 | 4 | 2 |
| Europe | 149/150 | 161 | 53 | 30 | 4 | 2 |
| China | 145/150 | 157 | 49 | 30 | 4 | 2 |
| Japan | 145/150 | 157 | 49 | 30 | 4 | 2 |

### Variantes par agence

Nœuds dont la recherche diffère selon l'agence (même nœud, ids différents) :

- 48 nœuds sur 150 ont des variantes d'agence.
- Nœuds absents pour l'agence : USA 6, Russia 6, Europe 1, China 5, Japan 5

### État de départ (Scenario_Default)

- **USA** — recherches : Building_HQ, Building_VehicleHangar, Rocket_SoundingRocket, milestone_sounding_rocket ; bâtiments : Building_VehicleHangar, Building_HQ
- **Russia** — recherches : Building_HQ, Building_VehicleHangar, Rocket_SoundingRocket, milestone_sounding_rocket ; bâtiments : Building_HQ, Building_VehicleHangar
- **Europe** — recherches : Building_HQ, Building_VehicleHangar, Rocket_SoundingRocket, milestone_sounding_rocket ; bâtiments : Building_VehicleHangar, Building_HQ
- **China** — recherches : Building_HQ, Building_VehicleHangar, Rocket_SoundingRocket, milestone_sounding_rocket ; bâtiments : Building_HQ, Building_VehicleHangar
- **Japan** — recherches : Building_HQ, Building_VehicleHangar, Rocket_SoundingRocket, milestone_sounding_rocket ; bâtiments : Building_HQ, Building_VehicleHangar

## Missions

- 39 modèles, dont 0 sans recherche du même id (missions « request » ou débloquées autrement — hors logique par défaut).
- Missions requises pour Mars : milestone_mars_mission_engine, milestone_mars_mission_lander
- Mission finale : milestone_mars_final_mission
- Missions débloquées par recherche, par corps : Earth 15, Mars 11, Moon 2, Venus 2, Jupiter 2, Saturn 2, Mercury 1, Uranus 1, Neptune 1, Pluto 1, Comet 1

## Couverture

### payload_without_research (1)

- `payload_sojourner`

## Recoupement avec les fichiers de localisation

- named_in_localisation_but_not_in_dump (5) : `building_missioncontrol_expand_limit3`, `building_researchlab_expand_limit2`, `mechanic_advanced_upgrades`, `mechanic_upgrades`, `payload_commsat`
- in_dump_but_not_named_name_research (0) : aucun

## Table d'IDs

- items.research : 268 IDs (plage 7658100001–7658109999)
- items.bundle : 0 IDs (plage 7658110000–7658119999)
- items.filler : 3 IDs (plage 7658120000–7658129999)
- items.trap : 0 IDs (plage 7658130000–7658139999)
- locations.research : 256 IDs (plage 7658200000–7658209999)
- locations.building : 30 IDs (plage 7658210000–7658219999)
- locations.milestone : 39 IDs (plage 7658220000–7658229999)
- locations.other : 0 IDs (plage 7658290000–7658299999)

Changements lors de ce run : 3 (détail dans la sortie console).
