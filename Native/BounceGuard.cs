using HarmonyLib;

// Harmony calls the prefix by name.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>In game a recovering FS-20 touched down at 62 m/s, bounced past 1 m radar altitude, and the game's landing
    /// aborted (touched-down mode: above 1 m → abort, full throttle, then combat) — every landing, on every retry. A
    /// recovering member whose wheels have touched and that bounces no higher than <see cref="BounceHeight"/> is held on
    /// the runway: the game's touched-down step is skipped for that tick (its last inputs hold: throttle idle, brakes)
    /// until the wheels are back down, and its own rollout then goes on to the runway exit and taxi (which our ground
    /// pilot takes over). (A first version also required less than the takeoff speed: the FS-20 bounced faster and was
    /// not held.)</summary>
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
            if (!WingService.Instance.Landing(a)) return true;
            if (Held++ % 50 == 0)
                Plugin.Logger.LogInfo($"[Recovery] bounce held on the runway: radar alt {a.radarAlt:0.0}, speed {a.speed:0}, " +
                                      $"takeoff speed {a.GetAircraftParameters().takeoffSpeed:0}");
            return false;
        }
    }
}
