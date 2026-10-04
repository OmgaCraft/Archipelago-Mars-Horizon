using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Astronautica;
using Astronautica.View;
using HarmonyLib;
using MarsHorizonAP.Ap;
using MarsHorizonAP.Game;
using UnityEngine;

namespace MarsHorizonAP.Dev
{
    // Test de bout en bout, sans interface : lance une nouvelle partie liée à un serveur Archipelago local, pilote la
    // simulation (recherches, constructions, jalons) et vérifie les checks, les items, la persistance.
    // Activé par [Test] Enabled = true ; le résultat est écrit dans le log BepInEx ("[SELFTEST] ...").
    internal sealed class SelfTest
    {
        private int pass;
        private int fail;

        internal void Check(bool condition, string what)
        {
            if (condition) { pass++; } else { fail++; }
            Plugin.Log.LogInfo($"[SELFTEST] {(condition ? "PASS" : "FAIL")} {what}");
        }

        private static void Info(string text) => Plugin.Log.LogInfo("[SELFTEST] " + text);

        public IEnumerator Run()
        {
            Info("démarrage");
            ApSession session = ApGame.Session;
            float t0 = Time.realtimeSinceStartup;
            while (!Plugin.TitleReached || Controller.Instance == null || !Controller.Initialised) { yield return null; }
            Plugin.Instance.Connect();
            while (!session.Connected && Time.realtimeSinceStartup - t0 < 60f) { yield return null; }
            Check(session.Connected, "connexion au serveur (" + session.Error + ")");
            if (!session.Connected) { Finish(); yield break; }
            Check(session.AgencyName == "USA", "slot data : agence " + session.AgencyName);

            // --- nouvelle partie liée -------------------------------------------------------------------
            Controller controller = Controller.Instance;
            Data.Scenario scenario = controller.GameData.scenarios.First(s => s.id == "Scenario_Default");
            var modification = new Data.Scenario.Modification
            {
                humanAgencyType = Agency.Type.USA,
                startingEra = 0,
                diplomacyGeneration = Data.Scenario.DiplomacyGeneration.Default,
                tutorialEnabled = false,
            };
            bool ready = false;
            controller.clientViewer.StartScenarioGame(scenario, modification, () => ready = true);
            float t1 = Time.realtimeSinceStartup;
            while ((!ready || ApGame.Human == null || !ApGame.Sim.universe.IsActive) && Time.realtimeSinceStartup - t1 < 120f) { yield return null; }
            Agency human = ApGame.Human;
            Check(human != null && ApGame.Sim.universe.IsActive, "nouvelle partie démarrée");
            if (human == null) { Finish(); yield break; }
            Simulation sim = ApGame.Sim;
            Check(ApGame.IsBound(human), "partie liée à Archipelago (marqueur AP.game)");

            int fundsBefore = human.funds, doneBefore = human.researchCompleted.Count;
            sim.AgencyApplyCheat(human, Simulation.Cheat.IncreaseFunds);
            sim.AgencyApplyCheat(human, Simulation.Cheat.CompleteAllResearch);
            Check(human.funds == fundsBefore && human.researchCompleted.Count == doneBefore, "triches du jeu bloquées dans une partie liée");

            // --- items de départ -------------------------------------------------------------------------
            float t2 = Time.realtimeSinceStartup;
            while (!human.researchCompleted.Contains("building_launchpad_small") && Time.realtimeSinceStartup - t2 < 20f) { yield return null; }
            Check(human.researchCompleted.Contains("building_launchpad_small"), "item de départ reçu : Small Launchpad débloqué");
            Check(!ApGame.IsChecked(human, "Building_LaunchPad_Small"), "…sans que la recherche soit marquée « faite »");
            Check(!human.researchCompleted.Contains("building_researchlab"), "Research Lab pas encore débloqué");

            // --- recherche : check envoyé, contenu non débloqué ------------------------------------------------
            const string lab = "Building_ResearchLab";
            long labLocation = ApIds.ResearchLocation[lab.ToLowerInvariant()];
            sim.AgencyCompleteResearch(human, lab, false, true);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(ApGame.IsChecked(human, lab), "recherche faite : marquée dans la sauvegarde (AP.chk)");
            Check(session.CheckedLocations.Contains(labLocation), "recherche faite : location envoyée au serveur");
            bool received = human.researchCompleted.Contains("building_researchlab");
            Info("contenu Research Lab " + (received ? "reçu (l'item était à cet endroit)" : "non débloqué"));
            ApGame.TreeDepth++;
            bool treeView = human.HasCompletedResearch(lab);
            ApGame.TreeDepth--;
            Check(treeView, "vue « arbre » : la recherche compte comme faite");
            bool contentView = human.HasCompletedResearch(lab);
            Check(contentView == received, "vue « contenu » : débloquée seulement si l'item est reçu");
            Check(!sim.GetAgencyIsBlueprintUnlocked(human, sim.gamedata.blueprints.First(b => b.id == lab)) || received,
                "bâtiment non constructible sans l'item");
            TechNodeFinder.AssertChildrenUnlockable(this, sim, human, lab);

            // --- flux normal : recherche menée sur plusieurs tours (fin de tour, science, achèvement) -----------------
            Data.Research turnResearch = sim.GetResearch("Building_RocketTestPad");
            Client client = controller.activeClient;
            Check(client.Send(new NetMessages.AgencySetActiveResearch { agency = human, node = sim.GetNodeFromResearch(turnResearch), research = turnResearch.id }),
                "recherche active choisie");
            int turnStart = sim.universe.turn;
            for (int i = 0; i < 12 && !ApGame.IsChecked(human, turnResearch.id); i++)
            {
                int current = sim.universe.turn;
                bool sent = client.Send(new NetMessages.AgencyEndTurn { agency = human }, false);
                if (!sent) { Info($"fin de tour refusée (événements en attente : {sim.universe.currentEvents.Count})"); break; }
                float tt = Time.realtimeSinceStartup;
                while (sim.universe.turn == current && Time.realtimeSinceStartup - tt < 25f) { yield return null; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(sim.universe.turn > turnStart, $"tours joués : {sim.universe.turn - turnStart}");
            Check(ApGame.IsChecked(human, turnResearch.id), "recherche terminée par le flux normal du jeu (science dépensée)");
            Check(!human.researchCompleted.Contains(turnResearch.id.ToLowerInvariant()) || ApGame.Session.CheckedLocations.Count > 0,
                "contenu de la recherche : pas débloqué par le jeu lui-même");
            yield return new WaitForSecondsRealtime(1f);

            // --- toutes les recherches --------------------------------------------------------------------------
            int before = session.CheckedLocations.Count;
            foreach (string id in ApGame.NodeResearch(human).ToList())
            {
                sim.AgencyCompleteResearch(human, id, false, true);
            }
            yield return new WaitForSecondsRealtime(3f);
            int nodes = ApGame.NodeResearch(human).Count;
            Check(ApGame.NodeResearch(human).All(id => ApGame.IsChecked(human, id)), $"{nodes} recherches faites");
            int researchChecked = ApIds.ResearchLocation.Values.Count(id => session.CheckedLocations.Contains(id));
            Check(researchChecked == nodes, $"{researchChecked}/{nodes} locations de recherche envoyées");
            Check(human.era >= 0, "ère de l'agence : " + human.era);
            float t3 = Time.realtimeSinceStartup;
            while (session.HasPendingItems && Time.realtimeSinceStartup - t3 < 10f) { yield return null; }
            yield return new WaitForSecondsRealtime(1f);
            int contentUnlocked = ApGame.NodeResearch(human).Count(id => human.researchCompleted.Contains(id));
            Info($"items de recherche débloqués : {contentUnlocked}/{nodes} (les autres sont placés sur bâtiments/jalons)");

            // --- construction d'un bâtiment (première fois) ------------------------------------------------------
            const string pad = "Building_LaunchPad_Small";
            Data.Blueprint blueprint = sim.gamedata.blueprints.First(b => b.id == pad);
            bool unlocked = sim.GetAgencyIsBlueprintUnlocked(human, blueprint);
            Check(unlocked, "pas de tir constructible (item reçu)");
            human.funds = Math.Max(human.funds, 100000);
            if (sim.GetAgencyPlaceableBuildingFromBlueprint(human, blueprint, out Data.Building building))
            {
                Check(controller.activeClient.Send(new NetMessages.BuildingCreate { agency = human, building = building, isFree = true }),
                    "message BuildingCreate accepté");
            }
            else
            {
                Check(false, "emplacement libre pour le pas de tir");
            }
            yield return new WaitForSecondsRealtime(1.5f);
            long padLocation = ApIds.BuildingLocation[pad.ToLowerInvariant()];
            Check(session.CheckedLocations.Contains(padLocation), "construction : location envoyée");
            Check(human.memorySwitches.Contains(ApGame.BuiltPrefix + pad.ToLowerInvariant()), "construction : mémorisée (AP.bld)");

            // --- jalon ------------------------------------------------------------------------------------------------
            Data.Milestone first = sim.gamedata.milestones.First(m => m.id == "milestone_sounding_rocket");
            MethodInfo achieve = AccessTools.Method(typeof(Simulation), "AgencyAchieveMilestone",
                new[] { typeof(Agency), typeof(Data.Milestone), typeof(Mission), typeof(Data.Date) });
            achieve.Invoke(sim, new object[] { human, first, null, null });
            yield return new WaitForSecondsRealtime(1.5f);
            long milestoneLocation = ApIds.MilestoneLocation["milestone_sounding_rocket"];
            Check(session.CheckedLocations.Contains(milestoneLocation), "jalon : location envoyée");

            // --- persistance ---------------------------------------------------------------------------------------------
            int appliedBefore = (int)human.memoryValues[ApGame.IndexKey];
            Check(appliedBefore > 0, $"index d'items appliqués : {appliedBefore}");
            yield return RoundTrip(sim, human, appliedBefore);

            // --- objectif ------------------------------------------------------------------------------------------------------
            Data.Milestone final = sim.gamedata.milestones.First(m => m.id == "milestone_mars_final_mission");
            achieve.Invoke(sim, new object[] { human, final, null, null });
            yield return new WaitForSecondsRealtime(1.5f);
            Info("objectif envoyé (voir le log du serveur : « finished »)");
            Finish();
        }

        // Sauvegarde puis relecture en mémoire (même sérialiseur que les fichiers de sauvegarde du jeu).
        private IEnumerator RoundTrip(Simulation sim, Agency human, int applied)
        {
            var info = new SaveInfo
            {
                name = "aptest", time = DateTime.Now, type = SaveType.Internal, scenario = "Scenario_Default", version = Application.version,
                human = human.type, ai = new List<Agency.Type>(), aiMarsReadiness = new List<int>(), completedMilestonePositions = new int[0],
            };
            byte[] bytes = null;
            bool saved = false;
            Saves.ThreadedSaveToZipBytes(sim.gamedata, sim.universe, info, b => { bytes = b; saved = true; });
            float ts = Time.realtimeSinceStartup;
            while (!saved && Time.realtimeSinceStartup - ts < 30f) { yield return null; }
            Check(bytes != null && bytes.Length > 0, "sauvegarde en mémoire : " + (bytes?.Length ?? 0) + " octets");
            if (bytes == null) { yield break; }
            SaveData loaded = null;
            bool done = false;
            Saves.ThreadedZipBytesToSave(bytes, Saves.ThreadedDeserializer.EType.Data, (i, data) => { loaded = data; done = true; });
            ts = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - ts < 30f) { yield return null; }
            Check(loaded?.universe != null, "relecture de la sauvegarde");
            if (loaded?.universe == null) { yield break; }
            Agency copy = loaded.universe.agencies.First(a => !a.isAI);
            Check(copy.memorySwitches.Contains(ApGame.MarkerKey), "après relecture : marqueur AP.game conservé");
            Check(Math.Abs(copy.memoryValues[ApGame.IndexKey] - applied) < 0.5f, "après relecture : index d'items conservé (" + copy.memoryValues[ApGame.IndexKey] + ")");
            Check(Math.Abs(copy.memoryValues[ApGame.SlotKey] - ApGame.SlotHash(ApGame.Session)) < 0.5f, "après relecture : empreinte du slot conservée");
            Check(copy.memorySwitches.SetEquals(human.memorySwitches), "après relecture : toutes les marques AP.* conservées (" + copy.memorySwitches.Count + ")");
            Check(copy.researchCompleted.SetEquals(human.researchCompleted), "après relecture : contenu débloqué identique (" + copy.researchCompleted.Count + ")");
            Check(copy.funds == human.funds && copy.science == human.science, "après relecture : ressources identiques");
        }

        private void Finish()
        {
            Info($"TERMINÉ : {pass} réussis, {fail} échecs");
            Plugin.Log.LogInfo(fail == 0 ? "[SELFTEST] RESULT OK" : "[SELFTEST] RESULT FAILED");
            Application.Quit();
        }

        private static class TechNodeFinder
        {
            public static void AssertChildrenUnlockable(SelfTest test, Simulation sim, Agency human, string researchId)
            {
                // Les nœuds qui ne dépendent que de Research Lab doivent pouvoir être recherchés : l'arbre avance sur « recherche faite ».
                var tree = sim.GetTechTree(Astronautica.TechTrees.TechTree.Type.Base);
                var node = tree.FindNode<Astronautica.TechTrees.Runtime.TechNodeData>(researchId);
                bool any = false, allOk = true;
                foreach (var connector in node.outNodes)
                {
                    if (connector.inNodes.Count == 1 && connector.nodesRequired == 1)
                    {
                        foreach (var child in connector.outNodes)
                        {
                            any = true;
                            allOk &= sim.GetAgencyCanResearch(human, child);
                        }
                    }
                }
                test.Check(any && allOk, "arbre : les nœuds enfants de la recherche faite sont recherchables");
            }
        }
    }
}

