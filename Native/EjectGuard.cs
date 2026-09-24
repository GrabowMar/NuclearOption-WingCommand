using System.Collections.Generic;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>The native taxi, takeoff and landing states eject pilots of aircraft they think are stuck or tilted
    /// (native §B). A wing aircraft under ground supervision, or landing for us in the game's landing state, is never
    /// ejected that way: the ejection is skipped while it is intact (logged once per aircraft).</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
    internal static class EjectGuard
    {
        private static readonly HashSet<Aircraft> logged = new HashSet<Aircraft>();

        /// <summary>Native ejections skipped this session (automation reads it).</summary>
        public static int Blocked { get; private set; }

        private static bool Prefix(Aircraft __instance)
        {
            if (WingService.Instance == null || !WingService.Instance.ProtectsFromEjection(__instance)) return true;
            Blocked++;
            if (logged.Add(__instance))
                Plugin.Logger.LogWarning($"[Ground] blocked a native ejection of {__instance.definition.unitName} under ground supervision");
            return false;
        }
    }
}
