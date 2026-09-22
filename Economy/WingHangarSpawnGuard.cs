using HarmonyLib;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>Keep native AI spawns off a hangar whose pad or roll-out is physically blocked, so
    /// Airbase.TrySpawnAircraft moves on to its next hangar instead of stacking the new aircraft
    /// into a parked one. Player spawns are never touched, and a clear pad runs the original call
    /// unchanged. Host-side only; the game's own server check still throws on clients.</summary>
    [HarmonyPatch(typeof(Hangar), nameof(Hangar.TrySpawnAircraft))]
    internal static class WingHangarSpawnGuard
    {
#pragma warning disable IDE0051
        // A bool prefix skips the native spawn when the pad is blocked; the airbase loop treats the
        // default result as a refusal and tries the next hangar.
        [HarmonyPrefix]
        private static bool Prefix(Hangar __instance, Player player, AircraftDefinition definition,
                                   ref Airbase.TrySpawnResult __result)
        {
            if (player != null || __instance == null || definition == null) return true;
            if (!__instance.IsServer) return true;
            if (Plugin.Settings == null || !Plugin.Settings.ProtectHangarSpawns.Value) return true;
            if (!__instance.CanSpawnAircraft(definition)) return true;
            if (!WingAirfield.IsHangarPathBlocked(__instance, out string blocker)) return true;

            Plugin.LogVerbose("[Airfield] held " + __instance.name + " spawn: " + blocker);
            __result = default(Airbase.TrySpawnResult);
            return false;
        }
#pragma warning restore IDE0051
    }
}
