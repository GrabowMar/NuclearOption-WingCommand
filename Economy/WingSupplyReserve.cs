using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>
    /// Three concrete airframes held for the player's wing.
    ///
    /// A slot owns all facts about one airframe: its definition, whether it was already
    /// purchased, and the loadout (if any) it carried home. Keeping those facts together is
    /// important. Parallel per-type counters and loadout FIFOs can consume a held frame while
    /// discarding an owned frame's fit, which quietly changes which physical aircraft the
    /// player still owns.
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

        /// <summary>One literal reserve airframe. Purchase reservation keeps the slot present.</summary>
        internal sealed class Slot
        {
            internal readonly AircraftDefinition Definition;
            internal readonly Source Source;
            internal readonly bool HasLoadout;
            internal readonly WingLoadoutChoice Loadout;
            internal readonly object RecoveryToken;
            internal bool ReservedForPurchase;

            internal Slot(AircraftDefinition definition, Source source,
                          bool hasLoadout, WingLoadoutChoice loadout,
                          object recoveryToken = null)
            {
                Definition = definition;
                Source = source;
                HasLoadout = hasLoadout;
                Loadout = loadout;
                RecoveryToken = recoveryToken;
            }
        }

        private static readonly List<Slot> slots = new List<Slot>();
        private static readonly List<AircraftDefinition> definitions =
            new List<AircraftDefinition>(Capacity);
        private static FactionHQ hq;
        private static bool isHost;

        public static bool IsHost => isHost;
        public static bool HasFaction => hq != null;

        /// <summary>Occupied reserve capacity, including a slot reserved by a pending order.</summary>
        public static int Count => slots.Count;

        public static IReadOnlyList<AircraftDefinition> Definitions
        {
            get
            {
                definitions.Clear();
                for (int i = 0; i < slots.Count; i++)
                {
                    AircraftDefinition definition = slots[i].Definition;
                    if (definition != null && !definitions.Contains(definition))
                        definitions.Add(definition);
                }
                return definitions;
            }
        }

        /// <summary>Launchable slots of this type; pending purchase reservations are excluded.</summary>
        public static int CountOf(AircraftDefinition definition)
        {
            if (definition == null) return 0;
            int total = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Definition == definition && !slots[i].ReservedForPurchase) total++;
            return total;
        }

        public static int OwnedOf(AircraftDefinition definition)
        {
            if (definition == null) return 0;
            int total = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Definition == definition &&
                    slots[i].Source == Source.Owned && !slots[i].ReservedForPurchase)
                    total++;
            return total;
        }

        public static int TotalOwnedCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].Source == Source.Owned && !slots[i].ReservedForPurchase)
                        total++;
                return total;
            }
        }

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
            slots.Add(new Slot(definition, Source.Held, false, WingLoadoutChoice.Standard));
            Plugin.Logger.LogInfo(
                "[Reserve] held " + definition.unitName + " for the wing (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>Return one exact, unreserved airframe to ordinary faction stock.</summary>
        public static bool Release(AircraftDefinition definition, out bool wasOwned,
                                   out string reason)
        {
            wasOwned = false;
            reason = null;
            if (!CanWrite(definition, out reason)) return false;

            // Prefer an unpaid hold, but remove that concrete slot and its own loadout only.
            int index = ReserveSlotPolicy.SelectForRelease(
                slots.Count,
                i => slots[i].Definition == definition,
                i => slots[i].Source == Source.Owned,
                i => slots[i].HasLoadout,
                i => slots[i].ReservedForPurchase);
            if (index < 0)
            {
                reason = definition.unitName + " is not available in the wing reserve";
                return false;
            }

            Slot slot = slots[index];
            slots.RemoveAt(index);
            wasOwned = slot.Source == Source.Owned;
            hq.AddSupplyUnit(definition, 1);

            Plugin.Logger.LogInfo(
                "[Reserve] returned " + definition.unitName + " to faction stock (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>Store one recovered airframe with its ownership and fit in one slot.</summary>
        public static bool StoreRecovered(AircraftDefinition definition, bool owned,
                                          bool loadoutKnown, WingLoadoutChoice loadout,
                                          object recoveryToken)
        {
            if (definition == null || hq == null || !isHost) return false;
            if (recoveryToken != null)
            {
                for (int i = 0; i < slots.Count; i++)
                    if (ReferenceEquals(slots[i].RecoveryToken, recoveryToken)) return true;
            }
            // Unpaid held stock from faction obeys the holding limit; owned planes bought by the player
            // are always preserved in reserve so they do not have to be bought again.
            if (!ReserveSlotPolicy.CanStoreAirframe(owned, Count, Capacity)) return false;

            slots.Add(new Slot(definition, owned ? Source.Owned : Source.Held,
                               loadoutKnown, loadout, recoveryToken));
            Plugin.Logger.LogInfo(
                "[Reserve] recovered " + definition.unitName +
                (owned ? " (owned)" : "") + " into reserve (" + Count + " held)");
            return true;
        }

        /// <summary>Which exact slot the next requisition would consume.</summary>
        internal static bool PeekForPurchase(AircraftDefinition definition, out Slot slot)
        {
            int index = ReserveSlotPolicy.SelectForPurchase(
                slots.Count,
                i => slots[i].Definition == definition,
                i => slots[i].Source == Source.Owned,
                i => slots[i].ReservedForPurchase);
            slot = index >= 0 ? slots[index] : null;
            return slot != null;
        }

        public static Source NextSource(AircraftDefinition definition) =>
            PeekForPurchase(definition, out Slot slot) ? slot.Source : Source.None;

        public static bool PeekLoadout(AircraftDefinition definition,
                                       out WingLoadoutChoice loadout)
        {
            loadout = WingLoadoutChoice.Standard;
            if (!PeekForPurchase(definition, out Slot slot) || !slot.HasLoadout) return false;
            loadout = slot.Loadout;
            return true;
        }

        /// <summary>Reserve the concrete slot without freeing capacity for another recovery.</summary>
        internal static bool ReserveForPurchase(AircraftDefinition definition, Source expected,
                                                out Slot slot)
        {
            slot = null;
            if (!PeekForPurchase(definition, out Slot candidate)) return false;
            if (expected != Source.None && candidate.Source != expected) return false;
            candidate.ReservedForPurchase = true;
            slot = candidate;
            return true;
        }

        /// <summary>Commit a delivered purchase by consuming exactly the reserved slot.</summary>
        internal static bool CommitPurchase(Slot slot)
        {
            if (slot == null || !slot.ReservedForPurchase) return false;
            int index = slots.IndexOf(slot);
            if (index < 0) return false;
            slots.RemoveAt(index);
            return true;
        }

        /// <summary>Roll back an order while preserving the slot's identity and FIFO position.</summary>
        internal static void CancelPurchase(Slot slot)
        {
            if (slot != null && slots.Contains(slot)) slot.ReservedForPurchase = false;
        }

        public static void Reset()
        {
            ReturnAllToFaction();
            definitions.Clear();
            hq = null;
            isHost = false;
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
                for (int i = 0; i < slots.Count; i++)
                {
                    Slot slot = slots[i];
                    if (slot.Definition != null) hq.AddSupplyUnit(slot.Definition, 1);
                }
            }
            slots.Clear();
        }
    }
}
