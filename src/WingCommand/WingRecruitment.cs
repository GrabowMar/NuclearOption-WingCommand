using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Economy-backed assignment of already active faction aircraft.</summary>
    internal static class WingRecruitment
    {
        private static readonly HashSet<PersistentID> paidAircraft = new HashSet<PersistentID>();

        public static float PriceOf(WingRegistry wing, Aircraft aircraft, int additionalMembers = 0)
        {
            if (aircraft == null || aircraft.definition == null) return 0f;
            if (paidAircraft.Contains(aircraft.persistentID)) return 0f;

            int size = (wing?.Count ?? 0) + Mathf.Max(0, additionalMembers);
            return aircraft.definition.value * Plugin.Config2.RecruitmentCostRate.Value *
                   Mathf.Pow(Plugin.Config2.WingPriceGrowth.Value, size);
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

            float price = PriceOf(wing, aircraft);
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

        public static bool TryRecruitNearest(WingRegistry wing,
                                             out WingMember member, out string reason)
        {
            member = null;
            Aircraft candidate = wing?.FindNearestRecruitCandidate();
            if (candidate == null)
            {
                reason = wing != null && wing.Count >= Plugin.Config2.MaxWingSize.Value
                    ? "Wing is full"
                    : "No eligible friendly AI aircraft in range";
                return false;
            }
            return TryRecruit(wing, candidate, out member, out reason);
        }

        public static void Reset() => paidAircraft.Clear();
    }
}
