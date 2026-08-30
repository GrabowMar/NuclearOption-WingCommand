using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>
    /// Three concrete airframes held for the player's wing.
    ///
    /// The old system raised <c>FactionHQ.reserveAirframes</c>, a per-type floor used by the
    /// game's AI deployment. A value of two therefore protected two of every stocked type
    /// and the UI needed a mission + player + mod formula to explain the result. That was
    /// neither a three-aircraft reserve nor an intuitive inventory.
    ///
    /// This reserve is literal. Holding an airframe removes one unit from faction supply so
    /// native AI cannot spend it; releasing or ending the mission puts it back. Recovered
    /// wing aircraft enter the same three slots. An airframe previously requisitioned at
    /// full price is marked owned and may be launched again without buying it twice.
    /// </summary>
    internal static class WingSupplyReserve
    {
        public const int Capacity = 3;

        internal enum Source
        {
            None,
            Held,
            Owned,
        }

        private sealed class Entry
        {
            public int Held;
            public int Owned;
            public int Total => Held + Owned;
        }

        private static readonly Dictionary<AircraftDefinition, Entry> entries =
            new Dictionary<AircraftDefinition, Entry>();

        private static FactionHQ hq;
        private static bool isHost;

        public static bool IsHost => isHost;
        public static bool HasFaction => hq != null;

        public static int Count
        {
            get
            {
                int total = 0;
                foreach (Entry entry in entries.Values) total += entry.Total;
                return total;
            }
        }

        public static IEnumerable<AircraftDefinition> Definitions => entries.Keys;

        public static int CountOf(AircraftDefinition definition) =>
            definition != null && entries.TryGetValue(definition, out Entry entry)
                ? entry.Total
                : 0;

        public static int OwnedOf(AircraftDefinition definition) =>
            definition != null && entries.TryGetValue(definition, out Entry entry)
                ? entry.Owned
                : 0;

        public static int FactionStockOf(AircraftDefinition definition) =>
            hq != null && definition != null ? hq.GetUnitSupply(definition) : 0;

        public static void Tick()
        {
            FactionHQ current = GameManager.GetLocalPlayer(out Player player) && player != null
                ? player.HQ
                : null;

            isHost = player != null && player.IsServer;
            if (current == hq) return;

            ReturnAllToFaction();
            hq = current;
        }

        /// <summary>Move one selected faction airframe out of AI-accessible stock.</summary>
        public static bool Hold(AircraftDefinition definition, out string reason)
        {
            reason = null;
            if (!CanWrite(definition, out reason)) return false;
            if (Count >= Capacity)
            {
                reason = "Wing reserve is full (" + Count + " / " + Capacity + ")";
                return false;
            }
            if (hq.GetUnitSupply(definition) <= 0)
            {
                reason = definition.unitName + ": no faction stock left to hold";
                return false;
            }

            hq.AddSupplyUnit(definition, -1);
            GetOrCreate(definition).Held++;
            Plugin.Logger.LogInfo(
                "[Reserve] held " + definition.unitName + " for the wing (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>Return one selected reserve airframe to ordinary faction stock.</summary>
        public static bool Release(AircraftDefinition definition, out bool wasOwned,
                                   out string reason)
        {
            wasOwned = false;
            reason = null;
            if (!CanWrite(definition, out reason)) return false;
            if (!entries.TryGetValue(definition, out Entry entry) || entry.Total <= 0)
            {
                reason = definition.unitName + " is not in the wing reserve";
                return false;
            }

            // Give back an unpaid hold before an owned airframe. This makes room without
            // accidentally surrendering something the player already bought.
            if (entry.Held > 0) entry.Held--;
            else
            {
                entry.Owned--;
                wasOwned = true;
            }

            if (entry.Total == 0) entries.Remove(definition);
            hq.AddSupplyUnit(definition, 1);
            Plugin.Logger.LogInfo(
                "[Reserve] returned " + definition.unitName + " to faction stock (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>
        /// Put a safely recovered wingman into the same reserve, preserving whether its full
        /// requisition price was already paid. Returns false when the three slots are full.
        /// </summary>
        public static bool StoreRecovered(AircraftDefinition definition, bool owned)
        {
            if (definition == null || hq == null || !isHost || Count >= Capacity) return false;

            Entry entry = GetOrCreate(definition);
            if (owned) entry.Owned++;
            else entry.Held++;

            Plugin.Logger.LogInfo(
                "[Reserve] recovered " + definition.unitName +
                (owned ? " (owned)" : "") + " into slot " + Count + "/" + Capacity);
            return true;
        }

        /// <summary>Which reserve bucket the next requisition should consume.</summary>
        public static Source NextSource(AircraftDefinition definition)
        {
            if (definition == null || !entries.TryGetValue(definition, out Entry entry))
                return Source.None;

            // Use an already-paid airframe before charging for a merely held one.
            if (entry.Owned > 0) return Source.Owned;
            return entry.Held > 0 ? Source.Held : Source.None;
        }

        /// <summary>Commit a successful requisition against one reserved airframe.</summary>
        public static bool Consume(AircraftDefinition definition, Source source)
        {
            if (source == Source.None || definition == null ||
                !entries.TryGetValue(definition, out Entry entry)) return false;

            if (source == Source.Owned)
            {
                if (entry.Owned <= 0) return false;
                entry.Owned--;
            }
            else
            {
                if (entry.Held <= 0) return false;
                entry.Held--;
            }

            if (entry.Total == 0) entries.Remove(definition);
            return true;
        }

        public static void Reset()
        {
            ReturnAllToFaction();
            hq = null;
            isHost = false;
        }

        private static Entry GetOrCreate(AircraftDefinition definition)
        {
            if (entries.TryGetValue(definition, out Entry entry)) return entry;
            entry = new Entry();
            entries.Add(definition, entry);
            return entry;
        }

        private static bool CanWrite(AircraftDefinition definition, out string reason)
        {
            reason = null;
            if (definition == null)
            {
                reason = "Select an airframe first";
                return false;
            }
            if (hq == null)
            {
                reason = "No faction";
                return false;
            }
            if (!isHost)
            {
                reason = "Wing reserve is managed by the host";
                return false;
            }
            return true;
        }

        private static void ReturnAllToFaction()
        {
            if (hq != null && isHost)
            {
                foreach (KeyValuePair<AircraftDefinition, Entry> pair in entries)
                {
                    if (pair.Key != null && pair.Value.Total > 0)
                        hq.AddSupplyUnit(pair.Key, pair.Value.Total);
                }
            }
            entries.Clear();
        }
    }
}
