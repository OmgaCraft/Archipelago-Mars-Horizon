using Astronautica.View;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MarsHorizonAP
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "archipelago.marshorizon";
        public const string PluginName = "MarsHorizonAP";
        public const string PluginVersion = "0.0.1";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Hello from {PluginName} {PluginVersion} | game v{Application.version} | Unity {Application.unityVersion}");

            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Harmony patches applied.");
        }
    }

    // Phase 0 : preuve qu'Harmony s'accroche au code du jeu.
    // Cible : Astronautica.View.TitleScreen.OnVisible (decompiled/.../Astronautica/View/TitleScreen.cs:310)
    [HarmonyPatch(typeof(TitleScreen), "OnVisible")]
    internal static class TitleScreenHelloPatch
    {
        private static void Postfix()
        {
            Plugin.Log.LogInfo("Hello: TitleScreen.OnVisible reached (Harmony OK).");
        }
    }
}
