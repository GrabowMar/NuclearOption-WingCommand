using HarmonyLib;

namespace WingCommand
{
    /// <summary>While a Wing Command departure holds a runway, the game's takeoff check says "not available" to every
    /// other aircraft (the user's "reserve the runway exclusively"; native landings are never denied, since a native
    /// lander without a runway ejects its pilot in mid-air).</summary>
    [HarmonyPatch(typeof(Airbase.Runway), nameof(Airbase.Runway.IsAvailableForTakeoff))]
    internal static class RunwayLockPatch
    {
        private static void Postfix(Airbase.Runway __instance, Aircraft querier, ref bool __result)
        {
            if (__result && FieldRegistry.LockedAgainst(__instance, querier)) __result = false;
        }
    }
}
