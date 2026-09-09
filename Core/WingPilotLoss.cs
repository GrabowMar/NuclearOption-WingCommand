using System.Reflection;
using HarmonyLib;

namespace WingCommand
{
    // Harmony invokes these hooks through reflection.
#pragma warning disable IDE0051
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.ApplyDamage))]
    internal static class WingPilotFatalDamagePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, ref float pierceDamage, ref float blastDamage,
                                   ref float fireDamage, ref float impactDamage, ref float ___hitPoints,
                                   byte ___pilotNumber)
        {
            if (___pilotNumber == 0 && !__instance.dead && !__instance.ejected)
            {
                WingSurvivalPerks.ProtectPilotDamage(__instance.aircraft, ref pierceDamage,
                    ref blastDamage, ref fireDamage, ref impactDamage, ref ___hitPoints);
            }
            if (___pilotNumber != 0 || __instance.dead || __instance.ejected ||
                ___hitPoints - (pierceDamage + blastDamage + fireDamage + impactDamage) >= 0f) return;
            WingPilot pilot = WingPilotRoster.Of(__instance.aircraft);
            if (pilot == null) return;
            // Capture before native death handling can retire the player's taken-over pilot.
            pilot.LossCause = ((pierceDamage > 0f ? "Projectile + " : "") +
                               (blastDamage > 0f ? "Explosion + " : "") +
                               (fireDamage > 0f ? "Fire + " : "") +
                               (impactDamage > 0f ? "Impact / collision + " : "")).TrimEnd(' ', '+');
        }
    }

    [HarmonyPatch]
    internal static class WingPilotKillerPatch
    {
        // Patch the received RPC body as well as host execution, including late kill messages.
        private static MethodBase TargetMethod() => AccessTools.FirstMethod(typeof(MessageManager),
            method => method.Name.StartsWith("UserCode_RpcKillMessage_"));

        [HarmonyPrefix]
        private static void Prefix(PersistentID killerID, PersistentID killedID) =>
            WingPilotRoster.RecordKiller(killedID, killerID);
    }
}
