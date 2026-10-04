using System.Linq;
using Astronautica;
using MarsHorizonAP.Ap;
using MarsHorizonAP.Game;
using UnityEngine;

namespace MarsHorizonAP.Ui
{
    // Fenêtre IMGUI (touche F8) : connexion au serveur, état, mini-tracker. Les toasts (items reçus) sont toujours
    // affichés en haut à droite. Les champs sont aussi dans BepInEx/config/archipelago.marshorizon.cfg.
    internal sealed class ApWindow
    {
        private const int WindowId = 7658100;
        private Rect rect = new Rect(30, 30, 380, 260);
        private bool open;
        private string server;
        private string slot;
        private string password;

        public string Warning;

        public ApWindow()
        {
            server = Plugin.Server.Value;
            slot = Plugin.SlotName.Value;
            password = Plugin.Password.Value;
        }

        public void Toggle() => open = !open;

        public void OnGUI()
        {
            GUI.depth = -1000;
            DrawToasts();
            DrawTracker();
            if (open)
            {
                rect = GUI.Window(WindowId, rect, Draw, "Archipelago — Mars Horizon");
            }
        }

        private void Draw(int id)
        {
            ApSession s = ApGame.Session;
            GUILayout.BeginVertical();
            GUILayout.Label($"Statut : {StatusText(s)}");
            if (!string.IsNullOrEmpty(s.Error) && s.Status == ApStatus.Failed)
            {
                GUILayout.Label("<color=#ff7070>" + s.Error + "</color>", Rich());
            }
            if (!string.IsNullOrEmpty(Warning))
            {
                GUILayout.Label("<color=#ffc060>" + Warning + "</color>", Rich());
            }

            GUI.enabled = s.Status != ApStatus.Connected && s.Status != ApStatus.Connecting;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Serveur", GUILayout.Width(80));
            server = GUILayout.TextField(server);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot", GUILayout.Width(80));
            slot = GUILayout.TextField(slot);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mot de passe", GUILayout.Width(80));
            password = GUILayout.PasswordField(password, '*');
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (s.Status == ApStatus.Connected || s.Status == ApStatus.Connecting)
            {
                if (GUILayout.Button("Déconnecter"))
                {
                    Plugin.Instance.Disconnect();
                }
            }
            else if (GUILayout.Button("Connecter"))
            {
                Plugin.Server.Value = server;
                Plugin.SlotName.Value = slot;
                Plugin.Password.Value = password;
                Plugin.Instance.Connect();
            }
            if (GUILayout.Button("Fermer"))
            {
                open = false;
            }
            GUILayout.EndHorizontal();

            if (s.Connected)
            {
                GUILayout.Label($"Slot {s.SlotName} · agence {s.AgencyName} · seed {Short(s.SeedName)}");
            }
            GUILayout.Label("Lance la nouvelle partie APRÈS la connexion, avec la même agence que ton slot.");
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

        private static string Short(string seed) => seed != null && seed.Length > 10 ? seed.Substring(0, 10) + "…" : seed;

        private static string StatusText(ApSession s)
        {
            switch (s.Status)
            {
                case ApStatus.Connected: return "<b>connecté</b>".Replace("<b>", "").Replace("</b>", "");
                case ApStatus.Connecting: return "connexion…";
                case ApStatus.Failed: return "échec";
                default: return "déconnecté";
            }
        }

        private void DrawToasts()
        {
            float y = 10;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fontSize = 14 };
            foreach (string text in Toast.Visible())
            {
                var content = new GUIContent(text);
                float width = Mathf.Min(520, Screen.width - 40);
                float height = style.CalcHeight(content, width);
                GUI.Box(new Rect(Screen.width - width - 15, y, width, height + 4), content, style);
                y += height + 8;
            }
        }

        private void DrawTracker()
        {
            ApSession s = ApGame.Session;
            if (!open && !(s.Connected || ApGame.IsBound(ApGame.Human)))
            {
                return;
            }
            string line = s.Connected ? $"AP · {s.SlotName}" : "AP · hors ligne";
            if (s.Connected)
            {
                line += $" · Recherches {Done(ApIds.ResearchLocation)} · Bâtiments {Done(ApIds.BuildingLocation)} · Jalons {Done(ApIds.MilestoneLocation)} · Items {ItemApplier.AppliedCount}";
            }
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUI.Label(new Rect(8, Screen.height - 24, Screen.width - 16, 22), line, style);
        }

        private static string Done(System.Collections.Generic.Dictionary<string, long> table)
        {
            ApSession s = ApGame.Session;
            int total = table.Values.Count(id => s.AllLocations.Contains(id));
            int done = table.Values.Count(id => s.AllLocations.Contains(id) && s.CheckedLocations.Contains(id));
            return $"{done}/{total}";
        }
    }
}
