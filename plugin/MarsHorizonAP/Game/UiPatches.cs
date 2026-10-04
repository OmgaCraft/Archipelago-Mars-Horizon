using System;
using Astronautica;
using Astronautica.TechTrees.Runtime;
using Astronautica.View.Research;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MarsHorizonAP.Game
{
    // Arbre de recherche : jaune = recherche faite (check envoyé à Archipelago), vert = contenu débloqué en jeu
    // (item reçu). Un nœud reçu et fait est vert ; un nœud reçu mais pas encore recherché est vert aussi.
    [HarmonyPatch(typeof(ResearchTreeNode), "SetNodeVisuals")]
    internal static class NodeColorPatch
    {
        private static readonly Color Sent = new Color(1f, 0.85f, 0.25f);
        private static readonly Color Unlocked = new Color(0.45f, 0.92f, 0.55f);

        private static void Postfix(ResearchTreeNode __instance)
        {
            try
            {
                var trav = Traverse.Create(__instance);
                Graphic graphic = trav.Field("button").GetValue<Selectable>()?.targetGraphic;
                if (graphic == null)
                {
                    return;
                }
                Color color = Color.white;
                Agency agency = ApGame.Human;
                var node = trav.Field("node").GetValue<TechNodeData>();
                if (agency != null && node != null && ApGame.IsBound(agency))
                {
                    Data.Research research = node.GetResearch(agency.type, ApGame.Sim);
                    if (research != null && ApGame.IsNodeResearch(agency, research.id))
                    {
                        if (agency.researchCompleted.Contains(research.id.ToLowerInvariant()))
                        {
                            color = Unlocked;
                        }
                        else if (ApGame.IsChecked(agency, research.id))
                        {
                            color = Sent;
                        }
                    }
                }
                graphic.color = color;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"NodeColorPatch : {e}");
            }
        }
    }

    internal static class UiRefresh
    {
        // Redessine les nœuds visibles (après un item reçu, par exemple).
        public static void ResearchTree()
        {
            foreach (ResearchTreeNode node in UnityEngine.Object.FindObjectsOfType<ResearchTreeNode>())
            {
                node.Refresh();
            }
        }
    }
}
