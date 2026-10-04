using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;

namespace MarsHorizonAP.Ap
{
    internal enum ApStatus { Disconnected, Connecting, Connected, Failed }

    internal sealed class ReceivedItem
    {
        public int Index;
        public long ItemId;
        public string Name;
        public string From;
    }

    // Enveloppe de MultiClient.Net. Les rappels réseau arrivent sur un autre thread : tout est mis en file
    // d'attente et consommé sur le thread principal par Plugin.Update.
    internal sealed class ApSession
    {
        private static readonly Version ProtocolVersion = new Version(0, 6, 0);

        private ArchipelagoSession session;
        private readonly ConcurrentQueue<ReceivedItem> items = new ConcurrentQueue<ReceivedItem>();
        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private int generation;
        private int enqueuedUpTo;

        public volatile ApStatus Status = ApStatus.Disconnected;
        public volatile string Error = "";
        public string SeedName = "";
        public string SlotName = "";
        public int Slot;
        public JObject SlotData = new JObject();
        public HashSet<long> AllLocations = new HashSet<long>();
        public HashSet<long> CheckedLocations = new HashSet<long>();
        public bool DeathLink;

        public string AgencyName => (string)SlotData["agency_name"] ?? "";
        public bool Connected => Status == ApStatus.Connected;

        public void Connect(string server, string slot, string password)
        {
            Disconnect();
            int mine = Interlocked.Increment(ref generation);
            enqueuedUpTo = 0;
            Status = ApStatus.Connecting;
            Error = "";
            ThreadPool.QueueUserWorkItem(_ => DoConnect(mine, server, slot, password));
        }

        public void Disconnect()
        {
            Interlocked.Increment(ref generation);
            try
            {
                session?.Socket.Disconnect();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"Disconnect: {e.Message}");
            }
            session = null;
            if (Status != ApStatus.Failed)
            {
                Status = ApStatus.Disconnected;
            }
        }

        private void DoConnect(int mine, string server, string slot, string password)
        {
            try
            {
                string host = server.Trim();
                int port = 38281;
                int colon = host.LastIndexOf(':');
                if (colon > 0 && int.TryParse(host.Substring(colon + 1), out int parsed))
                {
                    port = parsed;
                    host = host.Substring(0, colon);
                }
                host = host.Replace("ws://", "").Replace("wss://", "");

                var created = ArchipelagoSessionFactory.CreateSession(host, port);
                created.MessageLog.OnMessageReceived += m =>
                {
                    if (m is Archipelago.MultiClient.Net.MessageLog.Messages.ItemSendLogMessage ||
                        m is Archipelago.MultiClient.Net.MessageLog.Messages.ItemCheatLogMessage ||
                        m is Archipelago.MultiClient.Net.MessageLog.Messages.HintItemSendLogMessage)
                    {
                        messages.Enqueue(m.ToString());
                    }
                };
                created.Items.ItemReceived += helper => OnItemReceived(mine, helper);
                created.Socket.SocketClosed += reason =>
                {
                    if (mine == generation)
                    {
                        Error = "Connexion perdue : " + reason;
                        Status = ApStatus.Failed;
                    }
                };

                var tags = new List<string> { "AP" };
                LoginResult result = created.TryConnectAndLogin(ApIds.GameName, slot, ItemsHandlingFlags.AllItems,
                    ProtocolVersion, tags.ToArray(), null, string.IsNullOrEmpty(password) ? null : password, true);
                if (mine != generation)
                {
                    created.Socket.Disconnect();
                    return;
                }
                if (!result.Successful)
                {
                    var failure = (LoginFailure)result;
                    Error = string.Join("; ", failure.Errors);
                    Status = ApStatus.Failed;
                    return;
                }

                var ok = (LoginSuccessful)result;
                session = created;
                Slot = ok.Slot;
                SlotName = slot;
                SeedName = created.RoomState.Seed;
                SlotData = JObject.FromObject(ok.SlotData ?? new Dictionary<string, object>());
                AllLocations = new HashSet<long>(created.Locations.AllLocations);
                CheckedLocations = new HashSet<long>(created.Locations.AllLocationsChecked);
                Status = ApStatus.Connected;
                Plugin.Log.LogInfo($"Connecté : slot {slot}, seed {SeedName}, agence {AgencyName}");
            }
            catch (Exception e)
            {
                if (mine == generation)
                {
                    Error = e.Message;
                    Status = ApStatus.Failed;
                }
                Plugin.Log.LogWarning($"Connexion échouée : {e}");
            }
        }

        private void OnItemReceived(int mine, Archipelago.MultiClient.Net.Helpers.ReceivedItemsHelper helper)
        {
            if (mine != generation)
            {
                return;
            }
            // L'index d'un item est sa position dans la liste complète envoyée par le serveur : stable entre sessions.
            var all = helper.AllItemsReceived;
            for (int i = enqueuedUpTo; i < all.Count; i++)
            {
                ItemInfo info = all[i];
                items.Enqueue(new ReceivedItem
                {
                    Index = i,
                    ItemId = info.ItemId,
                    Name = info.ItemDisplayName,
                    From = info.Player?.Name,
                });
            }
            enqueuedUpTo = all.Count;
        }

        // Tous les items reçus depuis l'index donné (les doublons d'une même rafale sont écartés).
        public List<ReceivedItem> DrainItems(int fromIndex)
        {
            var list = new List<ReceivedItem>();
            var seen = new HashSet<int>();
            while (items.TryDequeue(out ReceivedItem item))
            {
                if (item.Index >= fromIndex && seen.Add(item.Index))
                {
                    list.Add(item);
                }
            }
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
            return list;
        }

        public bool HasPendingItems => !items.IsEmpty;

        public IEnumerable<string> DrainMessages()
        {
            while (messages.TryDequeue(out string message))
            {
                yield return message;
            }
        }

        public void SendLocations(IEnumerable<long> ids)
        {
            long[] valid = ids.Where(id => AllLocations.Contains(id) && !CheckedLocations.Contains(id)).Distinct().ToArray();
            if (valid.Length == 0 || session == null || !Connected)
            {
                return;
            }
            foreach (long id in valid)
            {
                CheckedLocations.Add(id);
            }
            session.Locations.CompleteLocationChecksAsync(ok =>
            {
                if (!ok)
                {
                    foreach (long id in valid)
                    {
                        CheckedLocations.Remove(id);
                    }
                }
            }, valid);
        }

        public void SendGoal()
        {
            if (session != null && Connected)
            {
                session.SetGoalAchieved();
            }
        }

        public void Say(string text)
        {
            if (session != null && Connected && !string.IsNullOrWhiteSpace(text))
            {
                session.Say(text);
            }
        }
    }
}
