using System;
using System.Collections.Generic;
using System.Linq;
using Astronautica;
using Astronautica.TechTrees.Runtime;
using Astronautica.View;
using MarsHorizonAP.Ap;

namespace MarsHorizonAP.Game
{
    // État de la partie liée à Archipelago, stocké dans l'agence du joueur (sauvegardé avec la partie, donc
    // synchronisé par le cloud Steam et valable hors ligne) :
    //   memorySwitches : "AP.game"      la partie est une partie Archipelago
    //                    "AP.chk.<id>"  recherche faite (le check est parti ; l'arbre avance)
    //                    "AP.bld.<id>"  bâtiment construit au moins une fois
    //   memoryValues   : "AP.slot"      empreinte de la seed + du slot (évite de mélanger deux multiworlds)
    //                    "AP.idx"       nombre d'items déjà appliqués (rien n'est donné deux fois)
    // Agency.memorySwitches / memoryValues sont sérialisés par BinarySerializer (Agency.cs:195-197,
    // BinarySerializer.cs:3028-3029) ; un champ ajouté par le mod ne le serait pas.
    internal static class ApGame
    {
        public const string MarkerKey = "AP.game";
        public const string SlotKey = "AP.slot";
        public const string IndexKey = "AP.idx";
        public const string CheckedPrefix = "AP.chk.";
        public const string BuiltPrefix = "AP.bld.";

        public static readonly ApSession Session = new ApSession();

        // > 0 pendant les méthodes du jeu qui raisonnent sur l'avancement de l'arbre (« recherche faite »)
        // plutôt que sur le contenu débloqué (« item reçu »). Voir Patches/TreeMode.cs.
        [ThreadStatic] public static int TreeDepth;

        private static HashSet<string> nodeResearch;
        private static Simulation nodeResearchFor;

        // --- accès au jeu ---------------------------------------------------------------------------
        public static Simulation Sim => Controller.Instance != null ? Controller.Instance.simulation : null;

        public static Agency Human
        {
            get
            {
                Simulation sim = Sim;
                return sim?.universe?.agencies?.FirstOrDefault(a => !a.isAI);
            }
        }

        public static bool InGame => Human != null && Sim.universe.IsActive;

        public static bool IsBound(Agency agency) =>
            agency != null && !agency.isAI && agency.memorySwitches != null && agency.memorySwitches.Contains(MarkerKey);

        // --- liaison -----------------------------------------------------------------------------------
        public static float SlotHash(ApSession s)
        {
            // float exact jusqu'à 2^24 : on garde 24 bits.
            unchecked
            {
                int hash = 17;
                foreach (char c in s.SeedName + "|" + s.SlotName)
                {
                    hash = hash * 31 + c;
                }
                return hash & 0xFFFFFF;
            }
        }

        public static void Bind(Agency agency)
        {
            agency.memorySwitches.Add(MarkerKey);
            agency.memoryValues[SlotKey] = SlotHash(Session);
            agency.memoryValues[IndexKey] = 0;
            Plugin.Log.LogInfo($"Partie liée au slot {Session.SlotName} (agence {agency.type}).");
        }

        public static string SlotMismatch(Agency agency)
        {
            if (!IsBound(agency) || !Session.Connected)
            {
                return null;
            }
            if (agency.memoryValues.TryGetValue(SlotKey, out float stored) && Math.Abs(stored - SlotHash(Session)) > 0.5f)
            {
                return "Cette sauvegarde appartient à un autre multiworld ou à un autre slot.";
            }
            if (!string.Equals(Session.AgencyName, agency.type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return $"Le slot est configuré pour l'agence {Session.AgencyName}, la partie utilise {agency.type}.";
            }
            return null;
        }

        // --- recherches ---------------------------------------------------------------------------------
        // Recherches qui portent un check pour l'agence du joueur : les nœuds de l'arbre, hors recherches de départ.
        public static HashSet<string> NodeResearch(Agency agency)
        {
            Simulation sim = Sim;
            if (sim == null)
            {
                return new HashSet<string>();
            }
            if (nodeResearch != null && nodeResearchFor == sim)
            {
                return nodeResearch;
            }
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TechTreeData tree in sim.gamedata.techTrees)
            {
                foreach (TechNodeData node in tree.GetTechNodes())
                {
                    Data.Research research = node.GetResearch(agency.type, sim);
                    if (research != null && !research.unlockAtStart && ApIds.ResearchLocation.ContainsKey(research.id.ToLowerInvariant()))
                    {
                        set.Add(research.id.ToLowerInvariant());
                    }
                }
            }
            nodeResearch = set;
            nodeResearchFor = sim;
            return set;
        }

        public static bool IsNodeResearch(Agency agency, string id) =>
            !string.IsNullOrEmpty(id) && NodeResearch(agency).Contains(id.ToLowerInvariant());

        public static bool IsChecked(Agency agency, string id) =>
            agency.memorySwitches.Contains(CheckedPrefix + id.ToLowerInvariant());

        public static void MarkChecked(Agency agency, string id) =>
            agency.memorySwitches.Add(CheckedPrefix + id.ToLowerInvariant());

        // --- locations ------------------------------------------------------------------------------------
        public static void SendResearchCheck(string id)
        {
            if (ApIds.ResearchLocation.TryGetValue(id.ToLowerInvariant(), out long location))
            {
                Session.SendLocations(new[] { location });
            }
        }

        public static void SendBuildingCheck(string blueprintId)
        {
            if (ApIds.BuildingLocation.TryGetValue(blueprintId.ToLowerInvariant(), out long location))
            {
                Session.SendLocations(new[] { location });
            }
        }

        public static void SendMilestoneCheck(string milestoneId)
        {
            if (ApIds.MilestoneLocation.TryGetValue(milestoneId.ToLowerInvariant(), out long location))
            {
                Session.SendLocations(new[] { location });
            }
            CheckGoal(Human);
        }

        // Tout ce que la partie a déjà fait, rejoué à la connexion : le serveur ignore les doublons.
        public static void SyncAllLocations(Agency agency)
        {
            if (!IsBound(agency) || !Session.Connected)
            {
                return;
            }
            var ids = new List<long>();
            foreach (string id in NodeResearch(agency))
            {
                if (IsChecked(agency, id))
                {
                    ids.Add(ApIds.ResearchLocation[id]);
                }
            }
            foreach (string flag in agency.memorySwitches)
            {
                if (flag.StartsWith(BuiltPrefix) && ApIds.BuildingLocation.TryGetValue(flag.Substring(BuiltPrefix.Length), out long b))
                {
                    ids.Add(b);
                }
            }
            foreach (string milestone in agency.milestonesCompleted)
            {
                if (ApIds.MilestoneLocation.TryGetValue(milestone.ToLowerInvariant(), out long m))
                {
                    ids.Add(m);
                }
            }
            Session.SendLocations(ids);
            CheckGoal(agency);
        }

        // --- objectif -----------------------------------------------------------------------------------------
        private static bool goalSent;

        public static void CheckGoal(Agency agency)
        {
            if (goalSent || agency == null || !IsBound(agency) || !Session.Connected)
            {
                return;
            }
            bool reached;
            if (((int?)Session.SlotData["goal"] ?? 0) == 0)
            {
                reached = agency.HasCompletedMilestone("milestone_mars_final_mission");
            }
            else
            {
                int needed = (int?)Session.SlotData["milestone_goal_count"] ?? 20;
                reached = agency.milestonesCompleted.Count(m => ApIds.MilestoneLocation.ContainsKey(m.ToLowerInvariant())) >= needed;
            }
            if (reached)
            {
                goalSent = true;
                Session.SendGoal();
                Toast.Show("Objectif atteint !");
                Plugin.Log.LogInfo("Objectif atteint : victoire envoyée au serveur.");
            }
        }

        public static void ResetSessionState()
        {
            goalSent = false;
        }
    }
}
