using HarmonyLib;

// Harmony calls the prefix by name.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>In game a recovering FS-20 touched down at 62 m/s, bounced past 1 m radar altitude, and the game's landing
    /// aborted (touched-down mode: above 1 m → abort, full throttle, then combat) — every landing, on every retry. A
    /// recovering member that bounces low and below its takeoff speed cannot fly out of it: the game's touched-down
    /// step is skipped for that tick (its last inputs hold: throttle idle, brakes) until the wheels are back down, and
    /// its own rollout then goes on to the runway exit and taxi (which our ground pilot takes over).</summary>
    [HarmonyPatch(typeof(AIPilotLandingState), "TouchedDown")]
    internal static class BounceGuard
    {
        public static float BounceHeight = 6f;

        /// <summary>Touched-down steps held this session (automation reads it).</summary>
        public static int Held { get; private set; }

        private static readonly AccessTools.FieldRef<PilotBaseState, Aircraft> AircraftOf =
            AccessTools.FieldRefAccess<PilotBaseState, Aircraft>("aircraft");

        private static bool Prefix(AIPilotLandingState __instance, bool checkMode)
        {
            if (checkMode || WingService.Instance == null) return true;
            Aircraft a = AircraftOf(__instance);
            if (a == null || a.radarAlt <= 1f || a.radarAlt > BounceHeight) return true;
            if (a.speed >= a.GetAircraftParameters().takeoffSpeed || !WingService.Instance.Landing(a)) return true;
            Held++;
            return false;
        }
    }
}
