extern alias fp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Astronautica;
using Astronautica.TechTrees.Runtime;
using Astronautica.View;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace MarsHorizonAP.Dump
{
    // Phase 1 : exporte les données statiques du jeu (Data.instance) en JSON.
    //
    // Les variantes par agence sont résolues par la logique du jeu elle-même :
    //   TechNodeData.GetResearch, Data.Research.IsValidForAgency, Data.VehiclePart.CanAgencyUsePart,
    //   Simulation.GetAgencyIsBlueprintValid, Data.MissionTemplate.GetDefaultPayloads.
    // Ces méthodes lisent Controller.Instance.simulation (Data.cs:649, :1742) : on installe donc un Host
    // temporaire autour d'une Simulation sans univers, le temps du dump (écran titre uniquement).
    internal static class GameDataDumper
    {
        private const string DefaultScenarioId = "Scenario_Default"; // NewGameScreen.cs:192

        private static readonly Agency.Type[] AgencyTypes =
        {
            Agency.Type.USA, Agency.Type.Russia, Agency.Type.Europe, Agency.Type.China, Agency.Type.Japan
        };

        private static readonly List<string> Errors = new List<string>();

        public static string Run(string outputDirectory)
        {
            Controller controller = Controller.Instance;
            if (controller == null)
            {
                throw new InvalidOperationException("Controller not ready");
            }
            if (controller.host != null)
            {
                throw new InvalidOperationException("Dump must run from the title screen (a game is hosted)");
            }

            Data data = controller.GameData;
            string originalVersion = data.version;
            // Host.cs:48 fait la même chose à chaque nouvelle partie : la résolution des variantes
            // compare addedInVersion à cette version.
            data.version = Application.version;

            Errors.Clear();
            string json;
            var simulation = new Simulation(data);
            var host = (Host)FormatterServices.GetUninitializedObject(typeof(Host));
            AccessTools.Field(typeof(Host), nameof(Host.simulation)).SetValue(host, simulation);
            controller.SetHost(host);
            try
            {
                Dictionary<string, object> root = Build(data, simulation, originalVersion);
                // Sérialiser ici : certaines valeurs dépendent du Host temporaire.
                json = JsonConvert.SerializeObject(root, Formatting.Indented);
            }
            finally
            {
                controller.SetHost(null);
                data.version = originalVersion;
            }

            Directory.CreateDirectory(outputDirectory);
            string path = Path.Combine(outputDirectory, "game_data.json");
            File.WriteAllText(path, json);
            return path;
        }

        private static Dictionary<string, object> Build(Data data, Simulation sim, string originalVersion)
        {
            var loc = new Loc();
            var root = new Dictionary<string, object>();

            root["meta"] = new Dictionary<string, object>
            {
                ["game_version"] = Application.version,
                ["unity_version"] = Application.unityVersion,
                ["data_version_at_title"] = originalVersion,
                ["plugin_version"] = Plugin.PluginVersion,
                ["dumped_at_utc"] = DateTime.UtcNow.ToString("o"),
                ["agencies"] = AgencyTypes.Select(a => a.ToString()).ToList(),
                ["default_scenario"] = DefaultScenarioId,
            };

            root["rules"] = DumpRules(data);
            // Valeurs numériques : le jeu compare les portées avec >= sur l'enum (Simulation.cs:5287).
            root["enums"] = new Dictionary<string, object>
            {
                ["Distance"] = EnumValues<Data.Distance>(),
                ["VehiclePartSize"] = EnumValues<Data.VehiclePart.Size>(),
                ["VehiclePartType"] = EnumValues<Data.VehiclePart.Type>(),
                ["PlanetaryBody"] = EnumValues<Data.PlanetaryBody>(),
            };
            root["research"] = data.researchRework.Select(r => new Dictionary<string, object>
            {
                ["id"] = r.id,
                ["unlock_at_start"] = r.unlockAtStart,
                ["cost"] = r.cost,
                ["expertise"] = r.expertise,
                ["value"] = r.value,
                ["valid_agencies"] = Names(r.validAgencies),
                ["added_in_version"] = r.addedInVersion,
                ["universal_before_version"] = r.universalBeforeVersion,
                ["name"] = loc.Name("Name_Research_" + r.id, "Name_" + r.id),
            }).ToList();

            root["tech_trees"] = data.techTrees.Select(t => DumpTree(t, loc)).ToList();
            root["blueprints"] = data.blueprints.Select(b => DumpBlueprint(b, loc)).ToList();
            root["vehicle_parts"] = data.vehicleParts.Select(p => DumpPart(p, loc)).ToList();
            root["vehicle_upgrades"] = (data.vehicleUpgrades ?? new Data.VehicleUpgrade[0]).Select(u => new Dictionary<string, object>
            {
                ["id"] = u.id,
                ["effect"] = u.effect == null ? null : u.effect.type.ToString(),
                ["era"] = u.era,
                ["points"] = u.points,
                ["cost"] = u.cost,
                ["research_dependencies"] = u.researchDependencies,
            }).ToList();
            root["payloads"] = data.payloads.Select(p => DumpPayload(p, loc)).ToList();
            root["mission_templates"] = data.missionTemplates.Select(m => DumpMission(m, loc)).ToList();
            root["milestones"] = data.milestones.Select(m => new Dictionary<string, object>
            {
                ["id"] = m.id,
                ["location"] = m.location.ToString(),
                ["difficulty"] = m.difficulty,
                ["name"] = loc.Name("Name_" + m.id),
            }).ToList();
            root["installations"] = (data.installations ?? new Installation[0]).Select(i => new Dictionary<string, object>
            {
                ["id"] = i.id,
                ["milestone_dependencies"] = i.milestoneDependencies,
                ["lifetime"] = i.data.lifetime,
                ["upkeep"] = i.data.upkeep,
                ["science"] = i.data.science,
                ["support"] = i.data.support,
            }).ToList();
            root["scenarios"] = data.scenarios.Select(s => DumpScenario(s, data)).ToList();
            root["agencies"] = AgencyTypes.ToDictionary(t => t.ToString(), t => (object)DumpAgency(t, data, sim));
            root["errors"] = Errors.ToList();
            return root;
        }

        private static Dictionary<string, object> DumpRules(Data data)
        {
            Data.Rules rules = data.rules;
            return new Dictionary<string, object>
            {
                ["starting_date"] = rules.startingDate == null ? null : new Dictionary<string, object>
                {
                    ["turn"] = rules.startingDate.turn,
                    ["month"] = rules.startingDate.month.ToString(),
                    ["year"] = rules.startingDate.year,
                },
                ["fall_of_soviet_union_turn"] = rules.fallOfSovietUnionTurn,
                ["era_unlock_percentage"] = rules.research == null ? (object)null : rules.research.eraUnlockPercentage,
                ["maximum_mission_slots"] = rules.maximumMissionSlots,
                ["funding_review_interval"] = rules.fundingReviewInterval,
                ["contractor_unlock_research"] = rules.contractorUnlockResearch,
                ["joint_mission_prompt_unlock_building_id"] = rules.jointMissionPromptUnlockBuildingId,
                ["mars_unlock_era"] = rules.marsUnlockEra,
#pragma warning disable CS0612 // champ marqué [Obsolete] par le jeu, exporté pour vérification
                ["mars_required_research"] = rules.marsRequiredResearch,
#pragma warning restore CS0612
                ["mars_required_diplomacy_points"] = rules.marsRequiredDiplomacyPoints,
                ["era_request_limit"] = rules.eraRequestLimit,
            };
        }

        private static Dictionary<string, object> DumpTree(TechTreeData tree, Loc loc)
        {
            var index = new Dictionary<BaseNodeData, int>();
            for (int i = 0; i < tree.nodes.Count; i++)
            {
                index[tree.nodes[i]] = i;
            }
            List<int> Indices(IEnumerable<BaseNodeData> nodes) =>
                nodes.Select(n => n != null && index.TryGetValue(n, out int i) ? i : -1).ToList();

            var nodes = new List<Dictionary<string, object>>();
            for (int i = 0; i < tree.nodes.Count; i++)
            {
                BaseNodeData node = tree.nodes[i];
                var entry = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["class"] = node.GetType().Name,
                    ["row"] = node.row,
                    ["column"] = node.column,
                };
                switch (node)
                {
                    case TechNodeData tech:
                        entry["era"] = tech.era;
                        entry["ids"] = tech.GetIds();
                        entry["category"] = loc.Name(tech.GetCategoryLocalisation());
                        entry["parents"] = Indices(tech.inNodes);
                        entry["children"] = Indices(tech.outNodes.Cast<BaseNodeData>());
                        break;
                    case ConnectorNodeData connector:
                        entry["era"] = connector.era;
                        entry["nodes_required"] = connector.nodesRequired;
                        entry["parents"] = Indices(connector.inNodes.Cast<BaseNodeData>());
                        entry["children"] = Indices(connector.outNodes.Cast<BaseNodeData>());
                        break;
                    case RootNodeData rootNode:
                        entry["children"] = Indices(rootNode.outNodes.Cast<BaseNodeData>());
                        break;
                }
                nodes.Add(entry);
            }

            return new Dictionary<string, object>
            {
                ["type"] = tree.type.ToString(),
                ["nodes"] = nodes,
                ["era_rewards"] = (tree.eraRewards ?? new EraCompletionReward[0]).Select(e => new Dictionary<string, object>
                {
                    ["id"] = e.id,
                    ["ids"] = e.ids,
                    ["era"] = e.era,
                    ["type"] = e.type.ToString(),
                    ["qualifier"] = e.qualifier.ToString(),
                }).ToList(),
            };
        }

        private static Dictionary<string, object> DumpBlueprint(Data.Blueprint b, Loc loc)
        {
            return new Dictionary<string, object>
            {
                ["id"] = b.id,
                ["category"] = b.category.ToString(),
                ["build_cost"] = b.buildCost,
                ["upkeep_cost"] = b.upkeepCost,
                ["build_time"] = b.buildTime,
                ["initial_build_limit"] = b.initialBuildLimit,
                ["is_critical"] = b.isCritical,
                ["effect"] = DumpEffect(b.effect),
                ["adjacency_bonuses"] = b.adjacencyBonuses.Select(a => new Dictionary<string, object>
                {
                    ["building"] = a.buildingID,
                    ["effect"] = DumpEffect(a.effect),
                }).ToList(),
                ["dependencies"] = b.dependencies,
                ["agency_type_dependencies"] = Names(b.agencyTypeDependencies),
                ["limit_dependencies"] = b.limitDependencies,
                ["cell_count"] = b.shape?.cells?.Count ?? 0,
                ["name"] = loc.Name("Name_" + b.id),
            };
        }

        private static Dictionary<string, object> DumpEffect(Data.Effect effect)
        {
            if (effect == null)
            {
                return null;
            }
            return new Dictionary<string, object>
            {
                ["type"] = effect.type.ToString(),
                ["strength"] = effect.strength,
            };
        }

        private static Dictionary<string, object> DumpPart(Data.VehiclePart p, Loc loc)
        {
            return new Dictionary<string, object>
            {
                ["id"] = p.id,
                ["type"] = p.type.ToString(),
                ["size"] = p.size.ToString(),
                ["fuel"] = p.fuel.ToString(),
                ["max_distance"] = p.maxDistance.ToString(),
                ["mass"] = p.mass,
                ["capacity"] = p.capacity,
                ["reliability"] = p.reliability,
                ["build_cost"] = p.buildCost,
                ["build_time"] = p.buildTime,
                ["supplementary_count"] = p.supplementaryCount,
                ["valid_supplementaries"] = p.validSupplementaries,
                ["launchpad"] = p.launchpad,
                ["agency_type_dependencies"] = Names(p.agencyTypeDependencies),
                ["upgrade_points"] = p.upgradePoints,
                ["added_in_version"] = p.addedInVersion,
                ["universal_before_version"] = p.universalBeforeVersion,
                ["name"] = loc.Name("Name_Research_" + p.id, "Name_" + p.id),
            };
        }

        private static Dictionary<string, object> DumpPayload(Data.Payload p, Loc loc)
        {
            return new Dictionary<string, object>
            {
                ["id"] = p.id,
                ["research_id"] = p.ResearchId,
                ["agency_type_dependencies"] = Names(p.agencyTypeDependencies),
#pragma warning disable CS0612
                ["research_dependencies"] = p.researchDependencies,
#pragma warning restore CS0612
                ["module_ids"] = p.moduleIds,
                ["is_mars_mission_payload"] = p.isMarsMissionPayload,
                ["build_time"] = p.buildTime,
                ["cost"] = p.cost,
                ["weight"] = p.weight,
                ["reliability"] = p.reliability,
                ["max_crew"] = p.maxCrew,
                ["power_capacity"] = p.powerCapacity,
                ["min_version"] = p.minVersion,
                ["universal_in_version"] = p.universalInVersion,
                ["variant_count"] = p.variants?.Length ?? 0,
                ["name"] = loc.Name("Name_Research_" + p.ResearchId, "Name_" + p.id, "Name_Research_" + p.id),
            };
        }

        private static Dictionary<string, object> DumpMission(Data.MissionTemplate m, Loc loc)
        {
            return new Dictionary<string, object>
            {
                ["id"] = m.id,
                ["mission_order"] = m.missionOrder,
                ["planetary_body"] = m.planetaryBody.ToString(),
                ["origin_body"] = m.originBody.ToString(),
                ["distance"] = m.distance.ToString(),
                ["min_crew"] = m.minCrew,
                ["required_installations"] = m.requiredInstallations,
                ["default_payloads"] = m.defaultPayloads,
                ["all_default_payloads"] = m.GetAllDefaultPayloadIds(),
                ["primary_milestone"] = m.primaryMilestone,
                ["is_mars_preparation_mission"] = m.isMarsPreparationMission,
                ["is_mars_required_mission"] = m.isMarsRequiredMission,
                ["is_final_mars_mission"] = m.IsFinalMarsMission,
                ["installation_id"] = m.installationId,
                ["crew_remain_on_station"] = m.crewRemainOnStation,
                ["phase_count"] = m.phases?.Length ?? 0,
                ["request_weighting"] = m.requestWeighting,
                ["request_mission_type_count"] = m.requestMissionTypes?.Length ?? 0,
                ["allow_joint_missions"] = m.allowJointMissions,
                ["name"] = loc.Name("Name_" + m.id),
            };
        }

        private static Dictionary<string, object> DumpScenario(Data.Scenario s, Data data)
        {
            return new Dictionary<string, object>
            {
                ["id"] = s.id,
                ["start_turn"] = s.startTurn,
                ["end_turn"] = s.endTurn,
                ["start_funds"] = s.startFunds,
                ["target_points"] = s.targetPoints,
                ["target_milestones"] = s.targetMilestones,
                ["win_conditions"] = s.winConditions.Select(w => new Dictionary<string, object>
                {
                    ["condition"] = w.condition.ToString(),
                    ["requirement"] = w.requirement,
                    ["target_milestones"] = w.targetMilestones,
                }).ToList(),
                ["event_packs"] = s.eventPacks,
                ["agencies"] = s.agencies.Where(a => a != null).Select(a => new Dictionary<string, object>
                {
                    ["type"] = a.type.ToString(),
                    ["is_active"] = a.isActive,
                    ["available_to_player"] = a.availableToPlayer,
                    ["completed_research"] = a.completedResearch,
                    ["buildings"] = a.buildings.Select(b => b.blueprintID).ToList(),
                    ["milestones"] = a.milestones,
                    ["funds"] = a.funds,
                    ["support"] = a.support,
                    ["base_layout"] = a.baseLayout,
                    ["base_layout_buildings"] = string.IsNullOrEmpty(a.baseLayout)
                        ? null
                        : data.GetBaseLayout(a.baseLayout)?.buildings.Select(b => b.blueprintId).ToList(),
                }).ToList(),
            };
        }

        private static Dictionary<string, object> DumpAgency(Agency.Type type, Data data, Simulation sim)
        {
            var agency = new Agency { type = type };

            var nodes = new Dictionary<string, string>();
            var eraRewards = new Dictionary<string, bool>();
            foreach (TechTreeData tree in data.techTrees)
            {
                for (int i = 0; i < tree.nodes.Count; i++)
                {
                    if (tree.nodes[i] is TechNodeData tech)
                    {
                        nodes[$"{tree.type}:{i}"] = Safe(() => tech.GetResearch(type, sim)?.id, $"node {tree.type}:{i} {type}");
                    }
                }
                foreach (EraCompletionReward reward in tree.eraRewards ?? new EraCompletionReward[0])
                {
                    foreach (string id in reward.ids ?? new string[0])
                    {
                        eraRewards[id] = Safe(() => sim.GetResearch(id, silent: true)?.IsValidForAgency(type, sim) ?? false, $"era reward {id} {type}");
                    }
                }
            }

            var missionPayloads = new Dictionary<string, List<string>>();
            foreach (Data.MissionTemplate m in data.missionTemplates)
            {
                missionPayloads[m.id] = Safe(() => m.GetDefaultPayloads(agency, null).Select(p => p.id).ToList(), $"payloads {m.id} {type}");
            }

            return new Dictionary<string, object>
            {
                ["node_research"] = nodes,
                ["era_reward_valid"] = eraRewards,
                ["research_valid"] = data.researchRework
                    .Where(r => Safe(() => r.IsValidForAgency(type, sim), $"research {r.id} {type}"))
                    .Select(r => r.id).ToList(),
                ["parts_usable"] = data.vehicleParts
                    .Where(p => Safe(() => p.CanAgencyUsePart(type, sim), $"part {p.id} {type}"))
                    .Select(p => p.id).ToList(),
                ["blueprints_valid"] = data.blueprints
                    .Where(b => sim.GetAgencyIsBlueprintValid(agency, b))
                    .Select(b => b.id).ToList(),
                ["mission_default_payloads"] = missionPayloads,
            };
        }

        private static T Safe<T>(Func<T> func, string context)
        {
            try
            {
                return func();
            }
            catch (Exception e)
            {
                Errors.Add($"{context}: {e.GetType().Name}: {e.Message}");
                return default;
            }
        }

        private static Dictionary<string, int> EnumValues<T>() where T : Enum
        {
            return Enum.GetValues(typeof(T)).Cast<T>().ToDictionary(v => v.ToString(), v => Convert.ToInt32(v));
        }

        private static List<string> Names(IEnumerable<Agency.Type> agencies)
        {
            return agencies == null ? new List<string>() : agencies.Select(a => a.ToString()).OrderBy(a => a).ToList();
        }

        // Textes bruts des CSV de localisation (StreamingAssets/Localisations), en anglais et en français.
        private sealed class Loc
        {
            private readonly fp::Localisation localisation = fp::ScriptableObjectSingleton<fp::Localisation>.instance;
            private readonly int en;
            private readonly int fr;

            public Loc()
            {
                en = Array.IndexOf(localisation.locales, "en");
                fr = Array.IndexOf(localisation.locales, "fr");
            }

            public Dictionary<string, object> Name(params string[] tags)
            {
                foreach (string tag in tags)
                {
                    fp::Localisation.Entry entry = localisation.GetEntry(tag);
                    string english = Text(entry, en);
                    if (english != null)
                    {
                        return new Dictionary<string, object>
                        {
                            ["tag"] = tag,
                            ["en"] = Resolve(english, en),
                            ["fr"] = Resolve(Text(entry, fr), fr),
                        };
                    }
                }
                return null;
            }

            // Même résolution des renvois « {Tag} » que Localisation.Interpolate (FP/Localisation.cs:386).
            private string Resolve(string text, int locale)
            {
                if (text == null)
                {
                    return null;
                }
                text = text.Replace("\\n", "\n");
                for (int i = 0; i < 32; i++)
                {
                    System.Text.RegularExpressions.Match match = fp::Localisation.variablePattern.Match(text);
                    if (!match.Success)
                    {
                        break;
                    }
                    string tag = match.Groups[1].Value;
                    text = text.Replace(match.Value, Text(localisation.GetEntry(tag), locale) ?? $"[{tag} MISSING]");
                }
                return text.Trim();
            }

            private static string Text(fp::Localisation.Entry entry, int locale)
            {
                if (entry?.texts == null || locale < 0 || locale >= entry.texts.Length)
                {
                    return null;
                }
                string text = entry.texts[locale];
                return string.IsNullOrEmpty(text) ? null : text.Trim();
            }
        }
    }
}
