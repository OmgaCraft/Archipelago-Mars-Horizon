using System.Collections.Generic;
using System.Reflection;
using Astronautica;
using Astronautica.View;
using HarmonyLib;

namespace MarsHorizonAP.Game
{
    // Dans une partie Archipelago, les triches intégrées au jeu (menu de debug : tout rechercher, fonds, jalons,
    // missions réussies, constructions gratuites...) sont bloquées : elles contourneraient les checks.
    // Seuls les réglages d'affichage restent permis.
    internal static class CheatBlocker
    {
        private static readonly HashSet<Simulation.Cheat> Allowed = new HashSet<Simulation.Cheat>
        {
            Simulation.Cheat.None, Simulation.Cheat.HideHUD, Simulation.Cheat.ShowFPS, Simulation.Cheat.UnlockCamera,
            Simulation.Cheat.ForceShowInstallationModel, Simulation.Cheat.AlwaysClearLaunches, Simulation.Cheat.AlwaysDawnLaunches,
            Simulation.Cheat.AlwaysDayLaunches, Simulation.Cheat.AlwaysNightLaunches, Simulation.Cheat.GameSummary,
            Simulation.Cheat.AIGameSummary,
        };

        public static void Apply(Harmony harmony)
        {
            // Simulation.cs:2913 — toutes les triches du menu de debug passent par ce point.
            harmony.Patch(AccessTools.Method(typeof(Simulation), nameof(Simulation.AgencyApplyCheat)),
                prefix: new HarmonyMethod(typeof(CheatBlocker), nameof(BlockCheat)));
            // Constructions et missions « cheat » (Simulation.cs:3303-3560).
            foreach (MethodInfo m in typeof(Simulation).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == "TryCheatMission" || m.Name == "TryBuildRandomBuildings" || m.Name == "TryBuildMissionBuildings"
                    || m.Name == "TryCheatBuildSelectedBuildings")
                {
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(CheatBlocker), nameof(BlockBool)));
                }
            }
        }

        private static bool BlockCheat(Agency agency, Simulation.Cheat cheat)
        {
            if (!ApGame.IsBound(agency) || Allowed.Contains(cheat))
            {
                return true;
            }
            Toast.Show($"Triche « {cheat} » bloquée : partie Archipelago.");
            return false;
        }

        private static bool BlockBool(Agency agency, ref bool __result)
        {
            if (!ApGame.IsBound(agency))
            {
                return true;
            }
            Toast.Show("Triche bloquée : partie Archipelago.");
            __result = false;
            return false;
        }
    }
}
