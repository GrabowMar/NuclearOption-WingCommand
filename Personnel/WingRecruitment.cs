using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Purchases command rights for active faction aircraft.</summary>
    internal static class WingRecruitment
    {
        private static readonly HashSet<PersistentID> paidAircraft = new HashSet<PersistentID>();

        /// <summary>One-time assignment price as a fixed fraction of airframe list value, independent of
        /// wing size.</summary>
        public static float PriceOf(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.definition == null) return 0f;
            if (paidAircraft.Contains(aircraft.persistentID)) return 0f;

            float rate = Plugin.Settings != null ? Plugin.Settings.RecruitmentCostRate : WingTuning.RecruitmentCostRate;
            return aircraft.definition.value * Mathf.Clamp(rate, 0f, 1f);
        }

        public static bool TryRecruit(WingRegistry wing, Aircraft aircraft,
                                      out WingMember member, out string reason)
        {
            member = null;
            reason = null;

            if (wing == null || !wing.CanRecruit(aircraft, out reason)) return false;
            if (wing.Leader == null || !wing.Leader.IsServer)
            {
                reason = "Host or single-player only";
                return false;
            }

            if (!GameManager.GetLocalPlayer(out Player player) || player == null)
            {
                reason = "No player allocation available";
                return false;
            }

            float price = PriceOf(aircraft);
            if (player.Allocation < price)
            {
                reason = "Assignment costs " + Mathf.RoundToInt(price) + ", have " +
                         Mathf.RoundToInt(player.Allocation);
                return false;
            }

            member = wing.Add(aircraft);
            if (member == null)
            {
                reason = "Aircraft could not be assigned";
                return false;
            }

            if (price > 0f) player.AddAllocation(-price);
            paidAircraft.Add(aircraft.persistentID);

            Plugin.LogVerbose(
                $"[Recruit] assigned {aircraft.unitName} for {price:F0} allocation");
            return true;
        }

        public static void Reset() => paidAircraft.Clear();
    }
}
