# Mars Horizon — notes internes (phase 0)

Exploration du code décompilé, en vue du randomizer Archipelago. Toutes les références
`fichier:ligne` sont relatives à `decompiled/` (régénérer avec `tools/decompile.ps1`).
`AC/` = `decompiled/Assembly-CSharp/`, `FP/` = `decompiled/Assembly-CSharp-firstpass/`.

## 0. Environnement vérifié

| Élément | Valeur |
|---|---|
| Version du jeu | **v1.4.2.1** (`Application.version`, lue par le plugin Hello). Les mods de Sheep-y visaient 1.4.1 |
| Moteur | Unity **2019.4.21f1**, Mono x64, profil .NET 4.x |
| Build Steam | appid 765810, buildid 20691746 |
| Loader | BepInEx **5.4.23.5** (win_x64) — plugin Hello chargé, patch Harmony OK |
| Code | `Assembly-CSharp.dll` (namespace `Astronautica`, 1 348 fichiers) + `Assembly-CSharp-firstpass.dll` (SaveLoad, `Messages.EventStream`, Localisation, Newtonsoft.Json embarqué) |
| Symboles | Les `.pdb` sont livrés avec le jeu → noms de méthodes et de champs d'origine |

## 1. Architecture générale

```
Controller (MonoBehaviour, singleton)      AC/Astronautica/View/Controller.cs
 ├─ host : Host                            AC/Astronautica/View/Host.cs
 │   └─ simulation : Simulation            AC/Astronautica/View/Simulation.cs  (11 k lignes, toute la règle du jeu)
 │        ├─ gamedata : Data               données statiques (§2)
 │        ├─ universe : Universe           état de la partie (agences, tour, diplomatie…)
 │        └─ history  : EventStream<NetMessages.Message>
 └─ clientViewer.client : Client           l'agence du joueur (activeClient.agency)
```

- Accès : `Controller.Instance` (`Controller.cs:109`), `.simulation` (`:95`), `.activeClient` (`:91`).
- **Event sourcing.** Toute action (joueur ou IA) est un `NetMessages.*` (`AC/Astronautica/View/NetMessages.cs`)
  envoyé par un `Client`, validé (`SetValidator`) puis appliqué par un listener dans
  `Simulation.SetupListeners()` (`Simulation.cs:378`). Exemples : `AgencySetActiveResearch`,
  `BuildingCreate`, `MissionCreate`, `MissionSucceedLaunch`, `MissionFailLaunch`, `MissionPhaseFinish`,
  `MissionFinish`, `UniverseAdvanceTurn`, `GameFinish`, `AgencyCheat`.
- `EventStream<T>.Listen<TEvent>(Action<TEvent>)` (`FP/Messages/EventStream.cs:106`) permet de s'abonner à
  n'importe quel message **sans Harmony**.
- `Host.onBeforeHistory` (`Host.cs:14`) est un délégué statique invoqué avec le flux `history` à chaque
  création de `Host` (nouvelle partie `Host.cs:52`, chargement `Host.cs:78`). Le jeu y ajoute ses propres
  abonnés par `Delegate.Combine` (`Controller.cs:270`, `:304`) → **point d'entrée idéal pour le plugin**.
- Événements statiques C# déjà exposés (`Simulation.cs:181-189`) : `onResearchComplete`,
  `onBuildingComplete`, `onVehiclePartLevelIncrease`, `onDiplomacyChanged`, `onMissionPhaseComplete`.
- `Host.isReplayingHistory` (`Host.cs:30`) est vrai pendant `Simulation.OnLoad()` : à ignorer pour ne pas
  renvoyer des checks au chargement.

## 2. Données statiques (`Data`)

- Classe `Astronautica.Data` (`AC/Astronautica/Data.cs:25`), accès global `Data.instance` (`Data.cs:6056`).
- Tableaux racine (`Data.cs:5980-6054`) : `researchRework` (recherches), `blueprints` (bâtiments),
  `vehicleParts`, `vehicleUpgrades`, `payloads`, `missionTemplates`, `milestones`, `techTrees`,
  `installations`, `scenarios`, `contractors`, `agencyTraits`, `eventPacks`, `historicalEvents`, `rules`.
- Chargement : `GameDataLoader.LoadGameData()` (`AC/Astronautica/View/GameDataLoader.cs:388`), appelé par
  `Controller.Load()` (`Controller.cs:350`). **À l'exécution**, le jeu parse des CSV embarqués
  (TextAssets `researchReworkCSV`, `buildingCSV`, `milestoneCSV`, `vehiclePartsCSV`…) et construit les
  arbres depuis des assets xNode `Resources.LoadAll<TechTree>("TechTrees")`.
- **Conséquence pour la phase 1** : la source la plus fiable et la plus simple est un *mode dump* du
  plugin qui sérialise `Data.instance` (+ libellés via `Localisation`) en JSON une fois le jeu chargé,
  plutôt que d'extraire les assets Unity hors ligne. Le graphe des arbres n'existe proprement qu'au
  runtime (`TechTreeData`).
- Pas de code DLC détecté. Le contenu ajouté par mises à jour est marqué `addedInVersion` /
  `universalBeforeVersion` (recherches, pièces) — à recenser en phase 1.

### Énumérations utiles

- `Agency.Type` (`AC/Astronautica/Agency.cs:38`) : `USA, Russia, Europe, China, Japan`.
- `Data.PlanetaryBody` (`Data.cs:58`) : Sun, Mercury, Venus, Earth, Moon, Mars, Phobos, Deimos,
  AsteroidBelt, Jupiter (+ lunes), Saturn (+ lunes), Uranus (+ lunes), Neptune, Triton, Pluto,
  KuiperBelt, Comet.
- `Data.Distance` (`Data.cs:97`) : `Orbit, Moon, InnerPlanets, OuterPlanets, SO, LEO, GTO, TLI, TMI, INT…`
  (portée max d'une pièce / exigence d'une mission).
- `TechTree.Type` (`AC/Astronautica/TechTrees/TechTree.cs:12`) : `None, Base, Missions, Vehicles`.
- `Data.VehiclePart.Type/Size/Fuel` (`Data.cs:1659-1684`) : Booster/Upper/Supplementary ;
  Small/Medium/Heavy ; Solid/Liquid/Hypergolic/Cryogenic.
- `Data.Effect.Type` (`Data.cs:353`) : effets de bâtiments, dont **`Launch_Small`, `Launch_Medium`,
  `Launch_Large`** (pas de tir), `Mission_Slots`, `Astronaut_Slots`, `Income_*`, `Research_Boost_*`.

## 3. Recherche et arbres technologiques

### Structure

- 3 arbres (`Data.techTrees`, `AC/Astronautica/TechTrees/Runtime/TechTreeData.cs`) : **Base** (bâtiments),
  **Missions** (missions / destinations / payloads), **Vehicles** (fusées).
- Nœuds (`TechTrees/Runtime/*TechNodeData.cs`) : `Booster`, `Upper`, `Supplementary`, `Vehicle`,
  `Payload`, `Mission`, `Building`, `BuildingLimit`, `Mechanic`. Chaque nœud a un `era` (0-4).
  - `TechNodeDataSingle` : un seul `id` de recherche (Building, Mechanic…).
  - `TechNodeDataMulti` : `ids[]`, **une variante par agence** ; la première valide pour l'agence est
    retenue (`TechNodeDataMulti.ValidForAgency`). → les IDs de recherche dépendent de l'agence jouée.
- Liaisons : nœud → `ConnectorNodeData` → nœuds enfants. Un connecteur exige `nodesRequired` parents
  terminés (`ConnectorNodeData.RequirementsMet`). Un nœud est recherchable si tous ses connecteurs
  parents sont satisfaits (`TechTreeData.IsNodeUnlockable`). → **logique ET / « N parmi M » native**,
  directement transposable dans `rules.py`.
- Récompenses d'ère (`EraCompletionReward`) : terminer toutes les recherches d'une ère d'un arbre
  (ou l'arbre entier) accorde automatiquement d'autres recherches
  (`Simulation.CheckForEraResearchCompletion`, `Simulation.cs:4163`).
- `Data.Research` (`Data.cs:623`) : `id, unlockAtStart, cost, expertise, value, validAgencies,
  addedInVersion…`. Les recherches `unlockAtStart` sont accordées à la création de l'agence
  (`Simulation.cs:453`).

### État et flux

- État par agence (`Agency.cs:181-187`) : `activeResearch`, `researchProgress` (science investie),
  `researchCompleted` (`HashSet<string>`, insensible à la casse), `researchTurnCompleted`.
- Choix : le joueur envoie `AgencySetActiveResearch` (`ResearchTreeScreen.cs:573`) → listener
  `Simulation.cs:785` (aucun validateur).
- Chaque tour : `PerformAgencyResearch()` (`Simulation.cs:4256`) verse la science, puis
  `AgencyCompleteResearch(Agency, Data.Research, bool, bool)` **privée** (`Simulation.cs:4099`) :
  ajoute à `researchCompleted`, gère expertise Mars, notification, ères, puis `onResearchComplete`.
- **Point d'étranglement unique** : `Agency.HasCompletedResearch(string)` (`Agency.cs:414`). Il sert à :
  - pièces de fusée : `Simulation.HasAgencyResearchedPart` (`Simulation.cs:4984`) — l'id de pièce **est**
    l'id de recherche ;
  - payloads : `Simulation.HasAgencyUnlockedPayload` (`Simulation.cs:7256`, via `Payload.ResearchId`) ;
  - bâtiments : `Simulation.GetAgencyIsBlueprintUnlocked` (`Simulation.cs:7199`) ;
  - missions : `Simulation.AgencyMeetMissionResearchRequirements` (`Simulation.cs:5257`) ;
  - progression de l'arbre : `ConnectorNodeData.RequirementsMet`, `TechTreeData.TreeCompleted`,
    `EraResearchesCompleted` ;
  - UI : `ResearchTreeNode.cs:248` (`completed || IsNodeUnlockable`).

### Implication design (à trancher en phase 2/4)

Le PDF veut que *terminer* un nœud envoie un check **sans** débloquer son contenu. Or le jeu utilise le
même ensemble `researchCompleted` pour « l'arbre avance » et « le contenu est utilisable ». Il faudra
donc deux ensembles côté plugin :

- `checked` (locations envoyées) → utilisé par la progression de l'arbre (connecteurs, ères, UI « terminé ») ;
- `received` (items AP reçus) → utilisé par `HasCompletedResearch` pour le contenu (pièces, payloads,
  bâtiments, missions).

Piste technique : patcher `AgencyCompleteResearch` (préfixe) pour l'agence humaine — enregistrer le check,
marquer la science comme dépensée, ne pas ajouter à `researchCompleted` — et faire lire `checked` aux
connecteurs. À valider par prototype en phase 3/4.

## 4. Bâtiments (base)

- `Data.Blueprint` (`Data.cs:3107`, champs `:3132-3158`) : `id, shape, category, buildCost, upkeepCost,
  buildTime, initialBuildLimit, isCritical, effect, adjacencyBonuses, dependencies` (+ `limitDependencies`).
- `Data.Building` (`Data.cs:2948`) : instance posée (`blueprintId, guid, position, rotation, age,
  buildTime`, `IsComplete => age >= buildTime`).
- Déblocage : `GetAgencyIsBlueprintUnlocked` (`Simulation.cs:7199`) = bâtiments prérequis
  (`blueprint.dependencies`) terminés **et** recherche `blueprint.id` terminée (nœud Building de l'arbre Base).
  → les « permis de construction » du PDF sont exactement les nœuds `BuildingTechNodeData`.
- Limite d'exemplaires : `GetAgencyBuildingLimit` (`Simulation.cs:7178`) = `initialBuildLimit` +
  bonus par recherche terminée (`limitDependencies`, nœuds `BuildingLimit` — ids en `*_LimitX`).
- Construction : message `BuildingCreate` (`Simulation.cs:591`) → `CreateBuilding` (`:4294`). Fin :
  immédiate, ou au fil des tours `AdvanceBuildingAge` → `AgencySetBuildingAge` →
  **`OnAgencyCompletedBuilding`** (`Simulation.cs:2743`) → `onBuildingComplete`.
- Pas de tir : effets `Launch_Small/Medium/Large`, testés par `AgencyHasVehicleLaunchpad`
  (`Simulation.cs:5335`).
- Pas de notion de « niveau d'amélioration » d'un bâtiment : les variantes sont des blueprints distincts
  (ex. `Building_ResearchLab_Expand`) et des recherches `*_LimitX`.

## 5. Missions et destinations

- `Data.MissionTemplate` (`Data.cs:3289`, champs `:4085-4163`) : `id, missionOrder, planetaryBody,
  originBody, distance, minCrew, requiredInstallations, defaultPayloads, primaryMilestone, phases[],
  isMarsPreparationMission, isMarsRequiredMission, installationId, requestMissionTypes…`.
  Constantes : `milestone_sounding_rocket`, `milestone_mars_final_mission` (`Data.cs:4165-4167`).
- Disponibilité d'une mission (`AgencyMeetMissionResearchRequirements`, `Simulation.cs:5257`) :
  recherche **`missionTemplate.id`** terminée (nœud Mission de l'arbre Missions) + recherche du payload
  par défaut. → les « destinations » du PDF correspondent aux nœuds `MissionTechNodeData`.
- Faisabilité véhicule (`IsAgencyVehicleDesignSuitableForMission`, `Simulation.cs:5285`) : portée max ≥
  `distance`, capacité booster ≥ masse de l'étage, capacité ≥ masse du payload, + pas de tir de la taille
  du booster (`AgencyHasVehicleLaunchpad`). C'est la règle de logique « destination + fusée capable +
  payload + infrastructure » du PDF.
- Cycle : `MissionCreate` → construction payload/véhicule → `MissionScheduleOrReschedule` →
  `MissionSucceedLaunch` / `MissionFailLaunch` → `MissionPhaseFinish` (×N) → `MissionFinish`
  (`Simulation.cs:1281`).
- **Missions « request »** (contrats) : générées aléatoirement (`GenerateAgencyRequestMissions`,
  `AgencyTryGenerateMissionRequestMessage`) → **à exclure de la logique** (non garanties).
- Installations (stations, base martienne) : `installationId`, `AgencyCreateNewInstallation`
  (`Simulation.cs:2026`), `IsAgencyInstallationComplete` (`:1970`).

## 6. Jalons (course à l'espace)

- `Data.Milestone` (`Data.cs:1776`) : `id, location, difficulty, spacepediaIDs`.
- Obtention : dernière phase réussie d'une mission → `ApplyPhaseCompleteRewards` (`Simulation.cs:1825`)
  → `AgencyAchieveMilestone(agency, template.primaryMilestone)` **privée** (`Simulation.cs:3800`).
- Stockage **par agence** : `agency.milestonesCompleted` (`Agency.cs:145`) ; global
  `universe.milestoneAgencyCompletion[id] = List<Agency>` (`Universe.cs:33`) ; rang via
  `GetAgencyMilestoneRank` (`Simulation.cs:3881`).
- → **Un rival qui atteint un jalon en premier ne l'empêche pas pour le joueur** (il sera seulement
  2ᵉ/3ᵉ, avec un bonus de récompense moindre). Le risque « jalon volé » du PDF est donc faible ;
  les locations « jalon » peuvent compter dès que le joueur l'atteint, quel que soit son rang.
- `Data.HistoricalEvent.EType` (`Data.cs:30`) : ArtificialSatellite, HumanInSpace, HumanEva,
  HumanMoonLanding, SpaceStation (événements historiques de calendrier, distincts des jalons).

## 7. Fin de partie / victoire

- `GameFinish` est émis à la réussite de `milestone_mars_final_mission` **seulement si aucune agence n'a
  déjà gagné** (`Simulation.cs:1485`, validateur `:1580`) ; il met `agency.victorious = true`.
- Si une IA gagne d'abord : écran de résumé avec bouton *Continuer* (`GameSummaryScreen.cs:195`,
  `universe.state = Continued`) → la partie peut continuer.
- Scénarios : `Data.Scenario.WinCondition` (`Data.cs:5709`) et `IsAgencyVictorious`
  (`Simulation.cs:4670`).
- **Recommandation** : l'objectif AP « premier humain sur Mars » = jalon
  `milestone_mars_final_mission` atteint par l'agence du joueur (via `AgencyAchieveMilestone`), pas
  `GameFinish`, pour rester atteignable même si une IA a gagné avant.

## 8. Ressources (filler) et tour de jeu

- Ressources (`Agency.cs:68-72`) : `science`, `funds`, `support` (+ `fundingTier`). Modifiables
  directement pour les items filler ; les revenus temporaires passent par `fundsIncomes` /
  `scienceIncomes` (`Agency.EventIncome`).
- Fin de tour : `UniverseAdvanceTurn` (`Simulation.cs:691`) → revenus, missions, âge des bâtiments /
  payloads / véhicules, `PerformAgencyResearch`, recrutement, défis, événements. Bon moment pour appliquer
  une file d'items reçus hors ligne (ou à la réception si une partie est chargée).
- Pièges possibles (phase 5) : retirer `funds`/`support`, `AgencyModifyBuildingAge` (retard de
  construction, `Simulation.cs:2728`), etc.

## 9. Sauvegarde et chargement

- `Saves.Save(...)` (`AC/Astronautica/View/Saves.cs:304`) écrit `saves/{info.name}.zip` contenant
  `universe.dat`, `stats.dat`, `info.dat` (et `data.dat` optionnel), sérialisés par **`BinarySerializer`**
  (`AC/BinarySerializer.cs`, code **généré** par type).
  - ⚠️ Un champ ajouté par le mod ne serait **pas** sauvegardé (le sérialiseur ne le connaît pas).
  - ✅ Les champs existants `memoryValues` (`Dictionary<string,float>`) et `memorySwitches`
    (`HashSet<string>`) de `Agency` (`Agency.cs:195-197`) et `Universe` sont sérialisés
    (`BinarySerializer.cs:3028-3029` pour Agency, `:970-971` pour Universe).
- Stockage physique (`FP/SaveLoad.cs`) :
  - build Steam → `MultiplatformSaveLoad` / `FilesystemSteam` = **Steam Remote Storage**
    (localement `Steam/userdata/<id>/765810/remote/saves/*.zip`, synchronisé au cloud) ;
  - sinon `StandaloneSaveLoad` (`FP/StandaloneSaveLoad.cs:7`) : `%LOCALAPPDATA%/Auroch Digital/Mars Horizon`.
- Chargement : `Controller.HostFromSave` (`Controller.cs:284`) → `new Host(controller, SaveData)`
  (`Host.cs:61`) : la sauvegarde est un **instantané** de l'`Universe` (pas un rejeu complet), seuls les
  `missionPaths` sont rejoués dans `Simulation.OnLoad()`.
- **Recommandation persistance AP** : stocker l'index du dernier item reçu (et l'identifiant de seed/slot)
  dans `agency.memoryValues["AP.ReceivedIndex"]` / `agency.memorySwitches` de l'agence humaine : sauvegardé
  nativement avec la partie, suit le cloud Steam, aucun fichier annexe à synchroniser. Le moteur
  d'événements ne lit ces dictionnaires que par clé nommée, un préfixe `AP.` évite toute collision.
  (Un `float` reste exact jusqu'à 16 777 216 items.)

## 10. Agences rivales (IA)

- Les IA passent par **exactement les mêmes méthodes** (`AIStrategy.cs:511`, `:1444` appellent
  `GetAgencyCanResearch`, `AIController.cs:288` envoie `AgencySetActiveResearch`).
- → **Chaque patch doit filtrer `agency.isAI`** (`Agency.cs:54`) pour ne pas randomiser / bloquer les
  rivaux.

## 11. Outils de debug intégrés

- `Simulation.Cheat` (`Simulation.cs:24`) : `CompleteAllResearch`, `IncreaseFunds`, `FinishCurrentBuildings`,
  `SucceedCurrentMissions`, `StartEra0..3`, `CompleteAllMilestones`… appliqués via le message
  `AgencyCheat` → `AgencyApplyCheat` (`Simulation.cs:2913`). Très utile pour tester vite en phase 3-4.
- `Controller.debug : DebugMenu`.

## 12. Points d'accroche candidats pour le plugin

| Besoin | Cible | Référence | Remarques |
|---|---|---|---|
| Brancher le plugin sur une partie | `Host.onBeforeHistory` + `EventStream.Listen<T>` | `Host.cs:14`, `FP/Messages/EventStream.cs:106` | Pas de Harmony nécessaire |
| Check « recherche terminée » | `Simulation.AgencyCompleteResearch(Agency, Data.Research, bool, bool)` (privée) ou `Simulation.onResearchComplete` | `Simulation.cs:4099`, `:181` | Ignorer `isAI`, `unlockAtStart`, récompenses d'ère, `Host.isReplayingHistory` |
| Check « bâtiment construit » | `Simulation.OnAgencyCompletedBuilding` ou `onBuildingComplete` | `Simulation.cs:2743`, `:183` | 1ʳᵉ fois par blueprint |
| Check « mission / jalon » | `Simulation.AgencyAchieveMilestone(Agency, Data.Milestone, …)` (privée) ou `Listen<MissionFinish>` | `Simulation.cs:3800`, `:1281` | Exclure les missions request |
| Objectif | jalon `milestone_mars_final_mission` du joueur | `Data.cs:4167` | Voir §7 |
| DeathLink sortant | `Listen<MissionFailLaunch>` / `MissionFinish.isFatal` | `NetMessages.cs:419`, `:455` | Définir « échec critique » |
| Verrou contenu | `Agency.HasCompletedResearch(string)` | `Agency.cs:414` | Point d'étranglement unique (§3) |
| Verrou progression d'arbre | `ConnectorNodeData.RequirementsMet`, `TechTreeData.IsNodeUnlockable` | `TechTrees/Runtime/ConnectorNodeData.cs`, `TechTreeData.cs` | Séparer `checked` / `received` |
| Appliquer un item | `Simulation.AgencyCompleteResearch(...)` (publique, par id) | `Simulation.cs:4056` | Contourner notre propre préfixe |
| Filler | `agency.funds / science / support` | `Agency.cs:68-72` | |
| Persistance | `agency.memoryValues` | `Agency.cs:197` | §9 |
| Version | `Application.version == "1.4.2.1"` au démarrage | `Controller.cs:89` | Avertir si différent |

## 13. Questions ouvertes (à trancher avant la phase 2)

1. **Agence** : les nœuds Multi ont des ids différents par agence et les pièces/payloads disponibles
   varient (`validAgencies`, `agencyTypeDependencies`). Une seule agence (plus simple) ou option
   « agence de départ » avec une table d'items par agence ?
2. **DLC** : aucun code DLC trouvé ; seul le contenu de mises à jour (`addedInVersion`) est à inclure.
3. **Granularité** : chaque nœud (≈ quelques centaines d'items) ou des paquets par branche/ère ? Chiffres
   exacts en phase 1.
4. **Jalons des rivaux** : d'après §6, inutile de les exclure — un jalon reste atteignable après un rival.
5. **Récompenses d'ère** : les laisser automatiques (hors pool), ou en faire des locations ?
6. **Ères** : `agency.era` monte avec le max des ères recherchées (`AgencyTryProgressEra`,
   `Simulation.cs:3724`) et conditionne des événements/missions. À surveiller quand la recherche sera
   découplée du déblocage.
7. **Bâtiments critiques / de départ** : layout et bâtiments initiaux viennent du scénario
   (`Data.Scenario.BuildingSetup`, `Data.cs:5767`) — garantir pas de tir + labo au départ.
