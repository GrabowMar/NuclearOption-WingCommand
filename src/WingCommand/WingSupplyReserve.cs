using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Adds a small host-controlled delta to Nuclear Option's own per-type AI reserve.
    /// The supply dictionary remains authoritative and no deployment method is patched.
    /// </summary>
    internal static class WingSupplyReserve
    {
        private static FactionHQ hq;
        private static int baseline;
        private static int applied;
        private static bool warnedExternalChange;

        public static int MissionReserve => baseline;
        public static int Additional => applied;

        public static int NativeProtectedPerType
        {
            get
            {
                if (hq == null) return 0;
                return baseline + hq.GetPlayers(sortByScore: false).Count * hq.extraReservesPerPlayer;
            }
        }

        public static int EffectiveProtectedPerType => NativeProtectedPerType + applied;

        public static void Tick(Aircraft leader)
        {
            FactionHQ current = leader != null && leader.IsServer ? leader.NetworkHQ : null;
            if (current != hq)
            {
                Restore();
                hq = current;
                if (hq == null) return;
                baseline = hq.reserveAirframes;
                applied = 0;
                warnedExternalChange = false;
            }

            if (hq == null) return;

            // Respect another mod or mission system that changes the live baseline.
            int expected = baseline + applied;
            if (hq.reserveAirframes != expected)
            {
                baseline = Mathf.Max(0, hq.reserveAirframes - applied);
                if (!warnedExternalChange)
                {
                    warnedExternalChange = true;
                    Plugin.Logger.LogWarning(
                        "[Reserve] another system changed reserveAirframes; adopted the new baseline");
                }
            }

            int desired = Plugin.Config2.AdditionalWingReserve.Value;
            if (desired == applied) return;
            applied = desired;
            hq.reserveAirframes = baseline + applied;
            Plugin.Logger.LogInfo(
                $"[Reserve] AI holdback per type: mission {baseline} + wing {applied}");
        }

        public static int ProtectedFromAi(AircraftDefinition definition)
        {
            if (hq == null || definition == null) return 0;
            return Mathf.Min(hq.GetUnitSupply(definition), EffectiveProtectedPerType);
        }

        public static void Reset()
        {
            Restore();
            hq = null;
            baseline = 0;
            applied = 0;
            warnedExternalChange = false;
        }

        private static void Restore()
        {
            if (hq != null && hq.reserveAirframes == baseline + applied)
                hq.reserveAirframes = baseline;
        }
    }
}
