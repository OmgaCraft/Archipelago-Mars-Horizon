using System.Collections.Generic;
using UnityEngine;

namespace MarsHorizonAP.Game
{
    // Notifications affichées dans un coin de l'écran (items reçus, envoyés, événements).
    internal static class Toast
    {
        private sealed class Entry
        {
            public string Text;
            public float Until;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private const float Duration = 8f;
        private const int MaxVisible = 6;

        public static void Show(string text)
        {
            Plugin.Log.LogInfo("[toast] " + text);
            Entries.Add(new Entry { Text = text, Until = Time.realtimeSinceStartup + Duration });
            if (Entries.Count > 30)
            {
                Entries.RemoveAt(0);
            }
        }

        public static IEnumerable<string> Visible()
        {
            float now = Time.realtimeSinceStartup;
            Entries.RemoveAll(e => e.Until < now);
            int skip = Entries.Count - MaxVisible;
            for (int i = skip > 0 ? skip : 0; i < Entries.Count; i++)
            {
                yield return Entries[i].Text;
            }
        }
    }
}
