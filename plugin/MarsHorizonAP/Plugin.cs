using System;
using System.IO;
using Astronautica;
using Astronautica.View;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MarsHorizonAP.Dev;
using MarsHorizonAP.Dump;
using MarsHorizonAP.Game;
using MarsHorizonAP.Ap;
using MarsHorizonAP.Ui;
using UnityEngine;

namespace MarsHorizonAP
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "archipelago.marshorizon";
        public const string PluginName = "MarsHorizonAP";
        public const string PluginVersion = "0.1.0";
        public const string SupportedGameVersion = "1.4.2.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<string> Server;
        internal static ConfigEntry<string> SlotName;
        internal static ConfigEntry<string> Password;
        internal static ConfigEntry<bool> AutoConnect;
        internal static ConfigEntry<KeyCode> WindowKey;

        internal static ConfigEntry<bool> TestEnabled;
        internal static bool TitleReached;
        private bool testStarted;

        internal static ConfigEntry<bool> DumpEnabled;
        internal static ConfigEntry<string> DumpDirectory;
        internal static ConfigEntry<bool> DumpQuitAfter;

        private ApWindow window;
        private float nextTick;
        private float nextReconnect;
        private bool wasConnected;
        private bool needSync;
        private bool dumpDone;
        private bool autoConnectDone;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo($"{PluginName} {PluginVersion} | jeu v{Application.version} | Unity {Application.unityVersion}");
            if (Application.version != SupportedGameVersion)
            {
                Log.LogWarning($"Version du jeu {Application.version} : le plugin vise {SupportedGameVersion}, des patches peuvent échouer.");
            }

            Server = Config.Bind("Connection", "Server", "archipelago.gg:38281", "Adresse du serveur Archipelago (hôte:port).");
            SlotName = Config.Bind("Connection", "Slot", "", "Nom du slot (joueur) dans le YAML.");
            Password = Config.Bind("Connection", "Password", "", "Mot de passe de la salle, s'il y en a un.");
            AutoConnect = Config.Bind("Connection", "AutoConnect", true, "Se connecter automatiquement au lancement si le slot est renseigné.");
            WindowKey = Config.Bind("Interface", "WindowKey", KeyCode.F8, "Touche d'ouverture de la fenêtre de connexion.");

            TestEnabled = Config.Bind("Test", "Enabled", false,
                "Outil de développement : lance un test automatique de bout en bout (nouvelle partie, checks, items, persistance).");
            DumpEnabled = Config.Bind("Dump", "Enabled", false,
                "Outil de développement : exporte les données du jeu en JSON à l'arrivée sur l'écran titre.");
            DumpDirectory = Config.Bind("Dump", "OutputDirectory", Path.Combine(Paths.BepInExRootPath, "MarsHorizonAP-dump"),
                "Dossier de sortie du dump.");
            DumpQuitAfter = Config.Bind("Dump", "QuitAfter", false, "Quitter le jeu une fois le dump écrit.");

            var harmony = new Harmony(PluginGuid);
            TreeModePatches.Apply(harmony);
            harmony.PatchAll(typeof(Plugin).Assembly);
            NewGameBinder.Install();
            window = new ApWindow();
            Log.LogInfo("Patches Harmony appliqués.");
        }

        internal void Connect()
        {
            if (string.IsNullOrWhiteSpace(SlotName.Value))
            {
                Toast.Show("Renseigne le nom du slot.");
                return;
            }
            ApGame.Session.Connect(Server.Value, SlotName.Value, Password.Value);
        }

        internal void Disconnect() => ApGame.Session.Disconnect();

        private void OnGUI() => window?.OnGUI();

        private void Update()
        {
            if (Input.GetKeyDown(WindowKey.Value))
            {
                window.Toggle();
            }
            ApSession session = ApGame.Session;
            foreach (string message in session.DrainMessages())
            {
                Toast.Show(message);
            }

            bool connected = session.Connected;
            if (connected != wasConnected)
            {
                wasConnected = connected;
                if (connected)
                {
                    ApGame.ResetSessionState();
                    needSync = true;
                    Toast.Show($"Connecté à Archipelago (slot {session.SlotName}).");
                }
                else
                {
                    Toast.Show("Déconnecté d'Archipelago.");
                    nextReconnect = Time.realtimeSinceStartup + 5f;
                }
            }

            if (Time.realtimeSinceStartup < nextTick)
            {
                return;
            }
            nextTick = Time.realtimeSinceStartup + 0.25f;

            if (!autoConnectDone && AutoConnect.Value && !string.IsNullOrWhiteSpace(SlotName.Value) && Time.realtimeSinceStartup > 5f)
            {
                autoConnectDone = true;
                Connect();
            }
            if (session.Status == ApStatus.Failed && session.Error.StartsWith("Connexion perdue") && Time.realtimeSinceStartup > nextReconnect)
            {
                nextReconnect = Time.realtimeSinceStartup + 10f;
                Connect();
            }

            Agency agency = ApGame.Human;
            window.Warning = null;
            if (agency != null && ApGame.IsBound(agency))
            {
                window.Warning = ApGame.SlotMismatch(agency);
                if (connected && window.Warning == null)
                {
                    if (needSync)
                    {
                        needSync = false;
                        ApGame.SyncAllLocations(agency);
                    }
                    ItemApplier.Apply(agency);
                }
            }
            else if (connected)
            {
                needSync = true;
            }
        }

        internal static void OnTitleScreen()
        {
            TitleReached = true;
            if (TestEnabled.Value && !Instance.testStarted)
            {
                Instance.testStarted = true;
                Instance.StartCoroutine(new SelfTest().Run());
            }
            if (!DumpEnabled.Value || Instance.dumpDone)
            {
                return;
            }
            Instance.dumpDone = true;
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
    }

    // Écran titre atteint = données et localisation chargées (Controller.Load, Controller.cs:344).
    // Cible : Astronautica.View.TitleScreen.OnVisible (decompiled/.../Astronautica/View/TitleScreen.cs:310)
    [HarmonyPatch(typeof(TitleScreen), "OnVisible")]
    internal static class TitleScreenPatch
    {
        private static void Postfix()
        {
            Plugin.OnTitleScreen();
        }
    }
}
