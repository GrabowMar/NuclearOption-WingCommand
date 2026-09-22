using HarmonyLib;

namespace WingCommand
{
    /// <summary>Grant mod-flown wingmen more pitch authority than the native fly-by-wire allows. The
    /// limiter caps commanded pull at the airframe's nominal G limit and 25 degrees of angle of
    /// attack, so a follower cannot physically match a hard player break or recover a blown slot at
    /// full rate. Raise both limits for the duration of the filter call while a Wing Command pilot
    /// state flies the aircraft, then restore the prefab values so a captured or player-flown
    /// airframe keeps stock handling.</summary>
    [HarmonyPatch(typeof(ControlsFilter.FlyByWire), nameof(ControlsFilter.FlyByWire.Filter))]
    internal static class WingmanOverdrivePatch
    {
        /// <summary>Limiter values captured before overdrive; Applied false leaves the call
        /// untouched.</summary>
        private readonly struct Overdrive
        {
            public readonly bool Applied;
            public readonly float GLimit;
            public readonly float AlphaLimiter;

            public Overdrive(bool applied, float gLimit, float alphaLimiter)
            {
                Applied = applied;
                GLimit = gLimit;
                AlphaLimiter = alphaLimiter;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(ControlsFilter.FlyByWire __instance, Aircraft aircraft,
            out Overdrive __state)
        {
            __state = default;
            if (aircraft == null || aircraft.Player != null) return;
            if (Plugin.Settings == null || !Plugin.Settings.WingmanOverdrive.Value) return;

            Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
            if (pilot == null || !(pilot.currentState is WingPilotState)) return;

            if (!GameAccess.TryReadFlyByWireLimits(__instance, out float gLimit, out float alphaLimiter))
                return;

            if (GameAccess.SetFlyByWireLimits(__instance, WingTuning.WingmanGLimit,
                    WingTuning.WingmanAlphaLimiter))
                __state = new Overdrive(true, gLimit, alphaLimiter);
        }

        [HarmonyPostfix]
        private static void Postfix(ControlsFilter.FlyByWire __instance, Overdrive __state)
        {
            if (__state.Applied)
                GameAccess.SetFlyByWireLimits(__instance, __state.GLimit, __state.AlphaLimiter);
        }
    }
}
