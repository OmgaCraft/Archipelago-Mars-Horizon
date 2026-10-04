using System;
using System.Collections.Generic;
using System.Linq;
using MarsHorizonAP.Ap;
using Newtonsoft.Json.Linq;

namespace MarsHorizonAP.Ui
{
    // Ce qui manque pour chaque mission, calculé avec les exigences envoyées par l'APWorld (slot data « missions ») :
    // item de mission, payload, véhicule (booster + étage + pas de tir). Sert au panneau « Missions » et aux indices.
    internal sealed class MissionRow
    {
        public string Id;
        public string Name;
        public int Order;
        public bool Done;
        public List<string> Missing = new List<string>();
        public List<string> BlockedBy = new List<string>();
        public bool Ready => !Done && Missing.Count == 0 && BlockedBy.Count == 0;
    }

    internal static class MissionPlanner
    {
        public static List<MissionRow> Compute(ApSession session)
        {
            var rows = new List<MissionRow>();
            JObject data = session.SlotData["missions"] as JObject;
            if (data == null || !session.Connected)
            {
                return rows;
            }
            var list = (JObject)data["list"];
            var vehicles = (JObject)data["vehicles"];
            foreach (var pair in list)
            {
                JObject m = (JObject)pair.Value;
                string location = (string)m["location"];
                var row = new MissionRow
                {
                    Id = pair.Key,
                    Name = location.StartsWith("Milestone: ") ? location.Substring(11) : location,
                    Order = (int)m["order"],
                    Done = session.CheckedLocations.Contains((long)m["location_id"]),
                };
                if (!row.Done)
                {
                    foreach (string item in m["items"].Select(t => (string)t))
                    {
                        if (!session.HasReceived(item)) { row.Missing.Add(item); }
                    }
                    row.Missing.AddRange(BestAlternative(session, (JArray)m["payloads"], vehicles));
                    foreach (string pre in m["prerequisites"].Select(t => (string)t))
                    {
                        JToken other = list[pre];
                        if (other != null && !session.CheckedLocations.Contains((long)other["location_id"]))
                        {
                            row.BlockedBy.Add(((string)other["location"]).Replace("Milestone: ", ""));
                        }
                    }
                    row.Missing = row.Missing.Distinct().ToList();
                }
                rows.Add(row);
            }
            return rows.OrderBy(r => r.Done).OrderBy(r => r.Done ? 0 : r.Missing.Count + r.BlockedBy.Count).ThenBy(r => r.Order).ToList();
        }

        // Charge utile + véhicule qui demandent le moins d'items manquants.
        private static List<string> BestAlternative(ApSession session, JArray payloads, JObject vehicles)
        {
            List<string> best = null;
            foreach (JToken alt in payloads)
            {
                string payload = alt[0].Type == JTokenType.Null ? null : (string)alt[0];
                var missing = new List<string>();
                if (payload != null && !session.HasReceived(payload)) { missing.Add(payload); }
                List<string> vehicle = null;
                JArray options = vehicles[(string)alt[1]] as JArray;
                if (options != null)
                {
                    foreach (JArray option in options)
                    {
                        List<string> lacking = option.Select(t => (string)t).Where(n => !session.HasReceived(n)).ToList();
                        if (vehicle == null || lacking.Count < vehicle.Count) { vehicle = lacking; }
                    }
                }
                missing.AddRange(vehicle ?? new List<string>());
                if (best == null || missing.Count < best.Count) { best = missing; }
            }
            return best ?? new List<string>();
        }
    }
}
