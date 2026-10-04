using System;
using System.Collections.Generic;
using Astronautica;
using Astronautica.View;
using MarsHorizonAP.Ap;

namespace MarsHorizonAP.Game
{
    // Applique les items reçus à l'agence du joueur, une seule fois chacun : l'index du dernier item appliqué
    // est enregistré dans la sauvegarde (ApGame.IndexKey), même si le serveur renvoie toute la liste à chaque connexion.
    internal static class ItemApplier
    {
        // Montants des items de remplissage par ère (0 à 3+). Repères : une recherche médiane coûte 3000 de
        // science, un bâtiment 50 à 10 000 de fonds ; l'agence démarre avec 100 de fonds et 50 de soutien.
        private static readonly int[] Funds = { 150, 400, 1200, 3500 };
        private static readonly int[] Science = { 150, 400, 1200, 3000 };
        private static readonly int[] Support = { 8, 12, 20, 30 };

        public static int AppliedCount { get; private set; }

        public static bool Apply(Agency agency)
        {
            bool changed = false;
            ApSession session = ApGame.Session;
            if (!ApGame.IsBound(agency) || !session.Connected || ApGame.SlotMismatch(agency) != null)
            {
                return false;
            }
            int applied = (int)agency.memoryValues[ApGame.IndexKey];
            AppliedCount = applied;
            if (!session.HasPendingItems)
            {
                return false;
            }
            foreach (ReceivedItem item in session.DrainItems(applied))
            {
                if (item.Index != applied)
                {
                    // Rafale incomplète (ne devrait pas arriver) : on attend la prochaine pour ne rien sauter.
                    Plugin.Log.LogWarning($"Item {item.Index} reçu alors que {applied} était attendu.");
                    return changed;
                }
                ApplyOne(agency, item);
                applied++;
                agency.memoryValues[ApGame.IndexKey] = applied;
                changed = true;
            }
            AppliedCount = applied;
            return changed;
        }

        private static void ApplyOne(Agency agency, ReceivedItem item)
        {
            string from = string.IsNullOrEmpty(item.From) || item.From == session().SlotName ? "" : $" ({item.From})";
            if (ApIds.ResearchItem.TryGetValue(item.ItemId, out string key))
            {
                GiveResearch(agency, key);
                Toast.Show($"{item.Name}{from}");
            }
            else if (ApIds.FillerItem.TryGetValue(item.ItemId, out string filler))
            {
                string text = GiveFiller(agency, filler);
                Toast.Show($"{item.Name}{from} : {text}");
            }
            else
            {
                Plugin.Log.LogWarning($"Item inconnu : {item.ItemId} {item.Name}");
            }
        }

        private static ApSession session() => ApGame.Session;

        // Débloque le contenu d'une recherche sans la « faire » : elle entre dans researchCompleted, que le jeu
        // consulte partout pour le contenu (pièces, payloads, bâtiments, missions). L'avancement de l'arbre
        // reste piloté par les checks (voir Patches/TreeMode.cs).
        public static void GiveResearch(Agency agency, string researchId)
        {
            Simulation sim = ApGame.Sim;
            Data.Research research = sim.GetResearch(researchId, silent: true);
            if (research == null)
            {
                Plugin.Log.LogWarning($"Recherche inconnue pour cette version du jeu : {researchId}");
                return;
            }
            string lower = research.id.ToLowerInvariant();
            if (agency.researchCompleted.Add(lower))
            {
                agency.researchTurnCompleted[lower] = sim.universe.turn;
            }
        }

        private static string GiveFiller(Agency agency, string key)
        {
            int era = Math.Max(0, Math.Min(3, agency.era));
            float scale = ((int?)ApGame.Session.SlotData["filler_strength"] ?? 100) / 100f;
            switch (key)
            {
                case "Filler_Funds":
                    int funds = (int)(Funds[era] * scale);
                    agency.funds += funds;
                    return $"+{funds} fonds";
                case "Filler_Science":
                    int science = (int)(Science[era] * scale);
                    agency.science += science;
                    return $"+{science} science";
                case "Filler_Support":
                    int support = Math.Max(1, (int)(Support[era] * scale));
                    agency.support += support;
                    return $"+{support} soutien";
                default:
                    return "";
            }
        }
    }
}
