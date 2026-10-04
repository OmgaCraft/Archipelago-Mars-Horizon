extern alias fp;
using Astronautica;
using Astronautica.View;
using fp::Messages;

namespace MarsHorizonAP.Game
{
    // Lie la partie à Archipelago à sa création : il faut donc être connecté AVANT de lancer la nouvelle partie.
    internal static class NewGameBinder
    {
        public static void Install()
        {
            Host.onBeforeHistory = (Host.OnBeforeHistory)System.Delegate.Combine(Host.onBeforeHistory, new Host.OnBeforeHistory(OnHistory));
        }

        private static void OnHistory(EventStream<NetMessages.Message> history)
        {
            history.Listen((NetMessages.AgencyCreate message) =>
            {
                if (message.isAI)
                {
                    return;
                }
                if (!ApGame.Session.Connected)
                {
                    Toast.Show("Archipelago non connecté : cette partie n'est PAS liée au multiworld.");
                    return;
                }
                if (!string.Equals(ApGame.Session.AgencyName, message.type.ToString(), System.StringComparison.OrdinalIgnoreCase))
                {
                    Toast.Show($"Ce slot utilise l'agence {ApGame.Session.AgencyName} : cette partie ({message.type}) n'est PAS liée.");
                    return;
                }
                ApGame.Bind(message.agency);
            });
        }

    }
}
