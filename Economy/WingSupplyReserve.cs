using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>Concrete reserve airframes with definition, ownership, and fit stored together, preventing
    /// per-type counters from mismatching owned equipment.</summary>
    internal static class WingSupplyReserve
    {
        public const int Capacity = 3;

        internal enum Source
        {
            None,
            Held,
            Owned,
        }

        /// <summary>One reserve airframe; purchase reservation keeps its slot occupied.</summary>
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

        /// <summary>Occupied slots, including pending purchase reservations.</summary>
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

        /// <summary>Unreserved launchable slots of this definition.</summary>
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

        /// <summary>Hold one faction airframe outside AI-accessible stock.</summary>
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
            Plugin.LogVerbose(
                "[Reserve] held " + definition.unitName + " for the wing (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>Return an exact unreserved airframe to faction stock.</summary>
        public static bool Release(AircraftDefinition definition, out bool wasOwned,
                                   out string reason)
        {
            wasOwned = false;
            reason = null;
            if (!CanWrite(definition, out reason)) return false;

            // Prefer unpaid stock, releasing only the selected slot and its associated fit.
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

            Plugin.LogVerbose(
                "[Reserve] returned " + definition.unitName + " to faction stock (" +
                Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>Preserve a recovered airframe's definition, ownership, and fit together.</summary>
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
            // Apply the hold cap to unpaid stock; retain owned returns even above capacity.
            if (!ReserveSlotPolicy.CanStoreAirframe(owned, Count, Capacity)) return false;

            slots.Add(new Slot(definition, owned ? Source.Owned : Source.Held,
                               loadoutKnown, loadout, recoveryToken));
            Plugin.LogVerbose(
                "[Reserve] recovered " + definition.unitName +
                (owned ? " (owned)" : "") + " into reserve (" + Count + " held)");
            return true;
        }

        /// <summary>Inspect the concrete slot the next purchase would consume.</summary>
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

        /// <summary>Reserve a slot without freeing its capacity for other returns.</summary>
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

        /// <summary>Consume the exact reserved slot on confirmed delivery.</summary>
        internal static bool CommitPurchase(Slot slot)
        {
            if (slot == null || !slot.ReservedForPurchase) return false;
            int index = slots.IndexOf(slot);
            if (index < 0) return false;
            slots.RemoveAt(index);
            return true;
        }

        /// <summary>Cancel reservation without changing slot identity or FIFO order.</summary>
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
