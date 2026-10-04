using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace MarsHorizonAP.Ap
{
    // Table d'IDs de l'APWorld (apworld/mars_horizon/data/ids.json, embarquée dans le plugin).
    // Les clés sont les ids internes du jeu : ids de recherche, de bâtiment, de jalon.
    internal static class ApIds
    {
        public const string GameName = "Mars Horizon";

        public static readonly Dictionary<string, long> ResearchLocation = new Dictionary<string, long>();
        public static readonly Dictionary<string, long> BuildingLocation = new Dictionary<string, long>();
        public static readonly Dictionary<string, long> MilestoneLocation = new Dictionary<string, long>();
        public static readonly Dictionary<long, string> ResearchItem = new Dictionary<long, string>();
        public static readonly Dictionary<long, string> FillerItem = new Dictionary<long, string>();

        static ApIds()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ids.json"))
            using (var reader = new StreamReader(stream))
            {
                JObject table = JObject.Parse(reader.ReadToEnd());
                Fill(ResearchLocation, (JObject)table["locations"]["research"]);
                Fill(BuildingLocation, (JObject)table["locations"]["building"]);
                Fill(MilestoneLocation, (JObject)table["locations"]["milestone"]);
                Fill(ResearchItem, (JObject)table["items"]["research"]);
                Fill(FillerItem, (JObject)table["items"]["filler"]);
            }
        }

        private static void Fill(Dictionary<string, long> into, JObject section)
        {
            foreach (var pair in section)
            {
                into[pair.Key.ToLowerInvariant()] = (long)pair.Value;
            }
        }

        private static void Fill(Dictionary<long, string> into, JObject section)
        {
            foreach (var pair in section)
            {
                into[(long)pair.Value] = pair.Key;
            }
        }
    }
}
