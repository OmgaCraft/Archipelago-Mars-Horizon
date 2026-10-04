using System;
using System.IO;
using Astronautica.View;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MarsHorizonAP.Dump;
using UnityEngine;

namespace MarsHorizonAP
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "archipelago.marshorizon";
        public const string PluginName = "MarsHorizonAP";
        public const string PluginVersion = "0.0.2";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> DumpEnabled;
        internal static ConfigEntry<string> DumpDirectory;
        internal static ConfigEntry<bool> DumpQuitAfter;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Hello from {PluginName} {PluginVersion} | game v{Application.version} | Unity {Application.unityVersion}");

            DumpEnabled = Config.Bind("Dump", "Enabled", false,
                "Outil de développement (phase 1) : exporte les données du jeu en JSON à l'arrivée sur l'écran titre.");
            DumpDirectory = Config.Bind("Dump", "OutputDirectory", Path.Combine(Paths.BepInExRootPath, "MarsHorizonAP-dump"),
                "Dossier de sortie du dump.");
            DumpQuitAfter = Config.Bind("Dump", "QuitAfter", false,
                "Quitter le jeu une fois le dump écrit.");

            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Harmony patches applied.");
        }

        internal static void OnTitleScreen()
        {
            if (!DumpEnabled.Value || dumpDone)
            {
                return;
            }
            dumpDone = true;
            try
            {
                string path = GameDataDumper.Run(DumpDirectory.Value);
                Log.LogInfo($"Game data dumped to {path}");
            }
            catch (Exception e)
            {
                Log.LogError($"Game data dump failed: {e}");
            }
            if (DumpQuitAfter.Value)
            {
                Application.Quit();
            }
        }

        private static bool dumpDone;
    }

    // Écran titre atteint = données et localisation chargées (Controller.Load, Controller.cs:344).
    // Cible : Astronautica.View.TitleScreen.OnVisible (decompiled/.../Astronautica/View/TitleScreen.cs:310)
    [HarmonyPatch(typeof(TitleScreen), "OnVisible")]
    internal static class TitleScreenPatch
    {
        private static void Postfix()
        {
            Plugin.Log.LogInfo("TitleScreen.OnVisible reached.");
            Plugin.OnTitleScreen();
        }
    }
}
