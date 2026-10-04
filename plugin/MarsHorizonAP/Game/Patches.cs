using System;
using System.Collections.Generic;
using System.Reflection;
using Astronautica;
using Astronautica.TechTrees.Runtime;
using Astronautica.View;
using Astronautica.View.Research;
using HarmonyLib;

namespace MarsHorizonAP.Game
{
    // ---------------------------------------------------------------------------------------------------
    // Séparation « recherche faite » / « contenu débloqué » (phase 4)
    //
    // Le jeu consulte Agency.researchCompleted pour deux choses :
    //   (a) le CONTENU : pièces de fusée, payloads, bâtiments, missions (Simulation.HasAgencyResearchedPart,
    //       HasAgencyUnlockedPayload, GetAgencyIsBlueprintUnlocked, AgencyMeetMissionResearchRequirements...) ;
    //   (b) l'AVANCEMENT de l'arbre : connecteurs, récompenses d'ère, état « terminé » des nœuds.
    // Pour la partie liée à Archipelago :
    //   * researchCompleted = contenu REÇU (item Archipelago) → (a) fonctionne sans autre patch ;
    //   * "AP.chk.<id>" = recherche FAITE (location envoyée) → (b) est redirigé vers cet ensemble par
    //     Agency.HasCompletedResearch pendant les méthodes listées dans TreeModePatches.
    // ---------------------------------------------------------------------------------------------------
    internal static class TreeModePatches
    {
        public static void Apply(Harmony harmony)
        {
            var enter = new HarmonyMethod(typeof(TreeModePatches), nameof(Enter));
            var exit = new HarmonyMethod(typeof(TreeModePatches), nameof(Exit));
            foreach (MethodBase target in Targets())
            {
                harmony.Patch(target, prefix: enter, finalizer: exit);
            }
        }

        private static IEnumerable<MethodBase> Targets()
        {
            // Astronautica/TechTrees/Runtime/ConnectorNodeData.cs:75 et :95
            yield return AccessTools.Method(typeof(ConnectorNodeData), nameof(ConnectorNodeData.RequirementsMet));
            yield return AccessTools.Method(typeof(ConnectorNodeData), nameof(ConnectorNodeData.RequirementsCount));
            // Astronautica/TechTrees/Runtime/TechTreeData.cs
            foreach (string name in new[] { "NodesUntilUnlockable", "IsNodeUnlockable" })
            {
                foreach (MethodInfo m in typeof(TechTreeData).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == name)
                    {
                        yield return m;
                    }
                }
            }
            yield return AccessTools.Method(typeof(TechTreeData), nameof(TechTreeData.TreeCompleted));
            yield return AccessTools.Method(typeof(TechTreeData), nameof(TechTreeData.EraResearchesCompleted));
            // Simulation.cs:4074 — achève les recherches dont la science est déjà payée (bonus de bâtiments)
            yield return AccessTools.Method(typeof(Simulation), "CheckTreeForCompletedResearches");
            // View/Research/ResearchTreeState.cs:162 et :170 — état « terminé » des nœuds à l'écran
            foreach (MethodInfo m in typeof(ResearchTreeState).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == "IsResearchCompleted")
                {
                    yield return m;
                }
            }
        }

        private static void Enter() => ApGame.TreeDepth++;

        private static void Exit() => ApGame.TreeDepth--;
    }

    // En mode « arbre », Agency.HasCompletedResearch répond « recherche faite » pour les nœuds qui portent un check.
    [HarmonyPatch(typeof(Agency), nameof(Agency.HasCompletedResearch), typeof(string))]
    internal static class HasCompletedResearchPatch
    {
        private static bool Prefix(Agency __instance, string id, ref bool __result)
        {
            try
            {
                if (ApGame.TreeDepth <= 0 || __instance.isAI || string.IsNullOrEmpty(id) || !ApGame.IsBound(__instance))
                {
                    return true;
                }
                if (!ApGame.IsNodeResearch(__instance, id))
                {
                    return true;
                }
                __result = ApGame.IsChecked(__instance, id);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"HasCompletedResearchPatch : {e}");
                return true;
            }
        }
    }

    // Fin d'une recherche (Simulation.cs:4099) : envoie le check, et ne débloque le contenu que s'il a déjà été reçu.
    [HarmonyPatch(typeof(Simulation), "AgencyCompleteResearch", typeof(Agency), typeof(Data.Research), typeof(bool), typeof(bool))]
    internal static class CompleteResearchPatch
    {
        private static readonly MethodInfo EraCheck = AccessTools.Method(typeof(Simulation), "CheckForEraResearchCompletion");

        private static void Prefix(Agency agency, Data.Research research, out bool __state)
        {
            ApGame.TreeDepth++;
            __state = agency != null && research != null && agency.researchCompleted.Contains(research.id.ToLowerInvariant());
        }

        private static void Postfix(Simulation __instance, Agency agency, Data.Research research, bool showNotification,
            bool checkEraCompletion, bool __result, bool __state)
        {
            try
            {
                OnCompleted(__instance, agency, research, showNotification, checkEraCompletion, __result, __state);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"CompleteResearchPatch : {e}");
            }
        }

        private static void OnCompleted(Simulation __instance, Agency agency, Data.Research research, bool showNotification,
            bool checkEraCompletion, bool __result, bool __state)
        {
            if (!__result || agency == null || research == null || !ApGame.IsBound(agency) || !ApGame.IsNodeResearch(agency, research.id))
            {
                return;
            }
            string lower = research.id.ToLowerInvariant();
            if (!__state)
            {
                // Le jeu vient de débloquer le contenu : on le retire, il viendra avec l'item Archipelago.
                agency.researchCompleted.Remove(lower);
                agency.researchTurnCompleted.Remove(lower);
            }
            ApGame.MarkChecked(agency, lower);
            ApGame.SendResearchCheck(lower);
            UiRefresh.ResearchTree();
            if (checkEraCompletion)
            {
                // Le nœud n'était pas encore « fait » quand le jeu a évalué la récompense d'ère : on réévalue.
                EraCheck.Invoke(__instance, new object[] { agency, research, showNotification });
            }
        }

        private static void Finalizer() => ApGame.TreeDepth--;
    }

    // Première construction d'un bâtiment (Simulation.cs:4294).
    [HarmonyPatch(typeof(Simulation), "CreateBuilding")]
    internal static class CreateBuildingPatch
    {
        private static void Postfix(Agency agency, Data.Building building)
        {
            try
            {
                if (!ApGame.IsBound(agency) || building == null || string.IsNullOrEmpty(building.blueprintId))
                {
                    return;
                }
                string id = building.blueprintId.ToLowerInvariant();
                if (agency.memorySwitches.Add(ApGame.BuiltPrefix + id))
                {
                    ApGame.SendBuildingCheck(id);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"CreateBuildingPatch : {e}");
            }
        }
    }

    // Jalon atteint (Simulation.cs:3800) : dernière phase d'une mission réussie.
    [HarmonyPatch(typeof(Simulation), "AgencyAchieveMilestone", typeof(Agency), typeof(Data.Milestone), typeof(Mission), typeof(Data.Date))]
    internal static class AchieveMilestonePatch
    {
        private static void Postfix(Agency agency, Data.Milestone milestone)
        {
            try
            {
                if (ApGame.IsBound(agency) && milestone != null)
                {
                    ApGame.SendMilestoneCheck(milestone.id);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"AchieveMilestonePatch : {e}");
            }
        }
    }
}
