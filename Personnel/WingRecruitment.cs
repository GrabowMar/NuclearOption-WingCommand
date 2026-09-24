using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>Takes command of a faction aircraft already flying (spec M3 §5): the wing adopts it, and the player pays
    /// Squadron/RecruitmentCostRate of its value from the allocation once per aircraft (<see cref="CallCost.Recruit"/>;
    /// free with Squadron/SandboxFreeCalls). Nothing is refunded when it goes home: the command was bought, not the
    /// airframe.</summary>
    internal static class WingRecruitment
    {
        private static readonly HashSet<PersistentID> paidAircraft = new HashSet<PersistentID>();

        public static CallQuote Quote(Aircraft aircraft)
        {
            GameManager.GetLocalPlayer(out Player player);
            return CallCost.Recruit(aircraft != null && aircraft.definition != null ? aircraft.definition.value : 0f,
                Plugin.Settings.RecruitmentCostRate.Value, player != null ? player.Allocation : 0f,
                Plugin.Settings.SandboxFreeCalls.Value, aircraft != null && paidAircraft.Contains(aircraft.persistentID));
        }

        public static bool TryRecruit(WingService wing, Aircraft aircraft, out WingMember member, out string reason)
        {
            member = null;
            if (wing == null)
            {
                reason = "Wing Command is not ready";
                return false;
            }
            if (!wing.CanRecruit(aircraft, out reason)) return false;
            if (!GameManager.GetLocalPlayer(out Player player) || player == null || !player.IsServer)
            {
                reason = "Host or single-player only";
                return false;
            }
            CallQuote quote = Quote(aircraft);
            if (!quote.Allowed)
            {
                reason = quote.Reason;
                return false;
            }
            member = wing.Recruit(aircraft);
            if (member == null)
            {
                reason = "the wing could not take it over";
                return false;
            }
            if (quote.Charge > 0f) player.AddAllocation(-quote.Charge);
            paidAircraft.Add(aircraft.persistentID);
            Plugin.Logger.LogInfo($"[Recruit] {aircraft.unitName} joined the wing for {CallCost.Money(quote.Charge)}");
            return true;
        }

        public static void Reset() => paidAircraft.Clear();
    }
}
