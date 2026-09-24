using HarmonyLib;

namespace WingCommand
{
    /// <summary>A recovering member in the game's landing state never leaves it for a native state (spec M3 §4, native
    /// §B9): the switch to taxi (a plane down on the runway) or to parked on the ground (a helicopter after touchdown)
    /// becomes our ground pilot; a switch to combat, to parked in the air, or to nothing (an abort, no runway, a
    /// blocked ejection) becomes our flight again, re-approaching. An engaged member's combat state leaving for landing
    /// or transport becomes our flight too, rejoining (spec M5 §2.2). A dead pilot is never redirected.</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.SwitchState))]
    internal static class SwitchStateGuard
    {
        /// <summary>Native switches redirected this session (automation reads it).</summary>
        public static int Redirected { get; private set; }

        private static void Prefix(Pilot __instance, ref PilotBaseState state)
        {
            WingService wing = WingService.Instance;
            if (wing == null || __instance == null || __instance.dead) return;
            PilotBaseState ours = wing.LeaveNativeLanding(__instance, state) ?? wing.LeaveNativeCombat(__instance, state);
            if (ours == null || ReferenceEquals(ours, state)) return;
            state = ours;
            Redirected++;
        }
    }
}
