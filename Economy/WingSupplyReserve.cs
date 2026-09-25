using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>SUPPLY's HANGAR store (user decision 2026-09-25: HANGAR with STORE / RETURN): up to <see cref="Capacity"/>
    /// airframes taken out of the faction's stock for the wing, used first by a requisition, given back to the faction on
    /// RETURN, a faction change or a new mission. Ticked by SpawnService with the local player's faction (never switched to
    /// none mid-mission); stock always moves through ModifyUnitSupply (AddSupplyUnit can hand an airframe to a player's
    /// pending request instead).</summary>
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
            // Without a player faction (a harness, a respawn gap) the wing's own stands in; never none mid-mission.
            if (current == null) current = WingService.Instance?.Leader != null ? WingService.Instance.Leader.NetworkHQ : null;

            isHost = player != null ? player.IsServer : WingService.Instance?.Leader != null && WingService.Instance.Leader.IsServer;
            if (current == null || current == hq) return;

            ReturnAllToFaction();
            hq = current;
        }

        /// <summary>Hold one faction airframe outside AI-accessible stock.</summary>
        public static bool Hold(AircraftDefinition definition, out string reason)
        {
            reason = null;
            if (!CanWrite(definition, out reason)) return false;
            reason = ShopRules.StoreBlock(isHost, hq != null, Count, Capacity, hq.GetUnitSupply(definition));
            if (reason != null) return false;

            hq.ModifyUnitSupply(definition, -1);
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
                reason = ShopRules.ReturnBlock(isHost, hq != null, 0);
                return false;
            }

            Slot slot = slots[index];
            slots.RemoveAt(index);
            wasOwned = slot.Source == Source.Owned;
            hq.ModifyUnitSupply(definition, 1);

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

        /// <summary>A requisition's aircraft spawned from a stored airframe: its slot is used up (a refund then goes to the
        /// faction's stock, never back into the store).</summary>
        public static bool TakeForLaunch(AircraftDefinition definition)
        {
            int index = ReserveSlotPolicy.SelectForPurchase(
                slots.Count,
                i => slots[i].Definition == definition,
                i => slots[i].Source == Source.Owned,
                i => slots[i].ReservedForPurchase);
            if (index < 0) return false;
            slots.RemoveAt(index);
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
                reason = "Pick an airframe first";
                return false;
            }
            reason = hq == null || !isHost ? ShopRules.StoreBlock(isHost, hq != null, 0, Capacity, 1) : null;
            return reason == null;
        }

        private static void ReturnAllToFaction()
        {
            if (hq != null && isHost)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    Slot slot = slots[i];
                    if (slot.Definition != null) hq.ModifyUnitSupply(slot.Definition, 1);
                }
            }
            slots.Clear();
        }
    }
}
