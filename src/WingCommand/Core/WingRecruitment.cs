using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Economy-backed assignment of already active faction aircraft.</summary>
    internal static class WingRecruitment
    {
        private static readonly HashSet<PersistentID> paidAircraft = new HashSet<PersistentID>();

        /// <summary>
        /// A flat fraction of the airframe's list value, once per aircraft.
        ///
        /// It used to compound with wing size on top of that fraction, so the fee for the
        /// same aircraft depended on how many wingmen you happened to have at the time and
        /// no displayed number could be trusted twice.
        /// </summary>
        public static float PriceOf(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.definition == null) return 0f;
            if (paidAircraft.Contains(aircraft.persistentID)) return 0f;

            return aircraft.definition.value * Plugin.Settings.RecruitmentCostRate.Value;
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

            Plugin.Logger.LogInfo(
                $"[Recruit] assigned {aircraft.unitName} for {price:F0} allocation");
            return true;
        }

        public static void Reset() => paidAircraft.Clear();
    }
}
