using System;
using HarmonyLib;

// Harmony calls postfixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    [HarmonyPatch(typeof(PilotPlayerState))]
    internal static class PlayerAutopilotPatches
    {
        [HarmonyPatch("PlayerAxisControls")]
        [HarmonyPostfix]
        private static void AxisPostfix(PilotPlayerState __instance)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            try
            {
                ap.AfterAxisControls(__instance);
            }
            catch (Exception e)
            {
                ap.Fault(e);
            }
        }

        [HarmonyPatch("PlayerThrottleAxis1Controls")]
        [HarmonyPostfix]
        private static void ThrottlePostfix(PilotPlayerState __instance)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            try
            {
                ap.AfterThrottle(__instance);
            }
            catch (Exception e)
            {
                ap.Fault(e);
            }
        }
    }
}
