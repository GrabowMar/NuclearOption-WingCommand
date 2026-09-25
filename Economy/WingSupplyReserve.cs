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

        /// <summary>The stored airframes, oldest first (one entry each).</summary>
        private static readonly List<AircraftDefinition> slots = new List<AircraftDefinition>(Capacity);
        private static FactionHQ hq;
        private static bool isHost;

        public static bool IsHost => isHost;
        public static bool HasFaction => hq != null;
        public static int Count => slots.Count;

        /// <summary>Every stored airframe, oldest first (a type stored twice appears twice).</summary>
        public static IReadOnlyList<AircraftDefinition> Stored => slots;

        public static int CountOf(AircraftDefinition definition)
        {
            if (definition == null) return 0;
            int total = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == definition) total++;
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

        /// <summary>STORE: one of the faction's airframes goes into the HANGAR, out of its AI's reach.</summary>
        public static bool Hold(AircraftDefinition definition, out string reason)
        {
            if (!CanWrite(definition, out reason)) return false;
            reason = ShopRules.StoreBlock(isHost, hq != null, Count, Capacity, hq.GetUnitSupply(definition));
            if (reason != null) return false;

            hq.ModifyUnitSupply(definition, -1);
            slots.Add(definition);
            Plugin.LogVerbose("[Hangar] stored " + definition.unitName + " (" + Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>RETURN: the oldest stored airframe of the type goes back to the faction's stock.</summary>
        public static bool Release(AircraftDefinition definition, out string reason)
        {
            if (!CanWrite(definition, out reason)) return false;
            int index = slots.IndexOf(definition);
            if (index < 0)
            {
                reason = ShopRules.ReturnBlock(isHost, hq != null, 0);
                return false;
            }
            slots.RemoveAt(index);
            hq.ModifyUnitSupply(definition, 1);
            Plugin.LogVerbose("[Hangar] returned " + definition.unitName + " to the faction (" + Count + "/" + Capacity + ")");
            return true;
        }

        /// <summary>A requisition's aircraft spawned from a stored airframe: its slot is used up (a refund then goes to the
        /// faction's stock, never back into the store).</summary>
        public static bool TakeForLaunch(AircraftDefinition definition)
        {
            int index = slots.IndexOf(definition);
            if (index < 0) return false;
            slots.RemoveAt(index);
            return true;
        }

        public static void Reset()
        {
            ReturnAllToFaction();
            hq = null;
            isHost = false;
        }

        private static bool CanWrite(AircraftDefinition definition, out string reason)
        {
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
                foreach (AircraftDefinition d in slots)
                    if (d != null) hq.ModifyUnitSupply(d, 1);
            slots.Clear();
        }
    }
}
