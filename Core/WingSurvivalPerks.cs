using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WingCommand
{
    internal static class WingSurvivalPerks
    {
        private sealed class MissileRolls
        {
            public readonly Dictionary<WingPilot, Vector3> Offsets = new Dictionary<WingPilot, Vector3>();
        }
        // Weak keys let destroyed native missiles leave memory without a scene-wide scan.
        private static ConditionalWeakTable<Missile, MissileRolls> rolls = new ConditionalWeakTable<Missile, MissileRolls>();

        public static void Reset() => rolls = new ConditionalWeakTable<Missile, MissileRolls>();

        public static bool Has(WingPilot pilot, PilotPerk perk) =>
            pilot != null && !pilot.Lost && Plugin.Settings != null &&
            Plugin.Settings.PilotProgression.Value && Plugin.Settings.RankEffect.Value > 0f &&
            pilot.Perks.Contains(perk);

        public static bool Has(Aircraft aircraft, PilotPerk perk) =>
            aircraft != null && !aircraft.disabled && aircraft.IsServer && aircraft.LocalSim &&
            Has(WingPilotRoster.Of(aircraft), perk);

        internal static float FuelUse(Aircraft aircraft, float amount)
        {
            if (amount <= 0f || aircraft == null) return amount;
            bool warned = Has(aircraft, PilotPerk.CoolHead) &&
                aircraft.GetMissileWarningSystem() != null && aircraft.GetMissileWarningSystem().IsWarning();
            return amount * PilotPerks.FuelMultiplier(Has(aircraft, PilotPerk.FuelDiscipline), warned,
                Has(aircraft, PilotPerk.HighAltitudeCruise) && aircraft.radarAlt >= 3000f);
        }

        internal static void ProtectPilotDamage(Aircraft aircraft, ref float pierce, ref float blast,
                                                ref float fire, ref float impact, ref float hitPoints)
        {
            bool tough = Has(aircraft, PilotPerk.Toughness);
            pierce = PilotPerks.PilotDamage(pierce, tough);
            blast = PilotPerks.PilotDamage(blast, tough);
            fire = PilotPerks.PilotDamage(fire, tough);
            impact = PilotPerks.PilotDamage(impact, tough);
            if (pierce > 0f && Has(aircraft, PilotPerk.BallisticVest)) pierce *= 0.4f;
            if (blast > 0f && Has(aircraft, PilotPerk.BlastSurvivor)) blast *= 0.4f;
            if (fire > 0f && Has(aircraft, PilotPerk.Fireproof)) fire *= 0.25f;
            if (impact > 0f && Has(aircraft, PilotPerk.CrashTraining)) impact *= 0.4f;
            float total = pierce + blast + fire + impact;
            WingPilot pilot = WingPilotRoster.Of(aircraft);
            if (pilot == null || pilot.SecondChanceUsed || hitPoints < 0f || total <= hitPoints ||
                !Has(aircraft, PilotPerk.SecondChance)) return;
            pilot.SecondChanceUsed = true;
            hitPoints = Math.Max(1f, hitPoints);
            float scale = Math.Max(0f, hitPoints - 1f) / total;
            pierce *= scale; blast *= scale; fire *= scale; impact *= scale;
            WingCommandManager.Instance?.Toast(pilot.Callsign + " — Second Chance saved the pilot; return to base!");
        }

        internal static void BiasGuidance(Missile missile, ref GlobalPosition aimPoint)
        {
            if (missile == null || !missile.IsServer || !missile.LocalSim || missile.disabled ||
                !UnitRegistry.TryGetUnit(missile.targetID, out Unit target) || !(target is Aircraft aircraft) ||
                aircraft.disabled) return;
            WingPilot pilot = WingPilotRoster.Of(aircraft);
            if (!Has(aircraft, PilotPerk.Luck) && !Has(aircraft, PilotPerk.LowLevelEvasion) &&
                !Has(aircraft, PilotPerk.RadarGhost) && !Has(aircraft, PilotPerk.HeatGhost) &&
                !Has(aircraft, PilotPerk.NotchExpert)) return;
            MissileRolls perMissile = rolls.GetValue(missile, _ => new MissileRolls());
            if (!perMissile.Offsets.TryGetValue(pilot, out Vector3 offset))
            {
                offset = Vector3.zero;
                string seeker = missile.GetSeekerType();
                bool radar = seeker == "ARH" || seeker == "SARH";
                Vector3 direction = aircraft.GlobalPosition() - missile.GlobalPosition();
                bool beaming = aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 900f &&
                    Math.Abs(Vector3.Dot(aircraft.rb.velocity.normalized, direction.normalized)) < 0.258819f;
                float chance = PilotPerks.MissileErrorChance(Has(aircraft, PilotPerk.Luck),
                    Has(aircraft, PilotPerk.LowLevelEvasion) && aircraft.radarAlt < 300f,
                    radar && Has(aircraft, PilotPerk.RadarGhost),
                    seeker == "IR" && Has(aircraft, PilotPerk.HeatGhost),
                    radar && beaming && Has(aircraft, PilotPerk.NotchExpert));
                if (chance > 0f && Random.value < chance)
                {
                    Vector3 sideways = Vector3.Cross(direction.normalized, Vector3.up);
                    if (sideways.sqrMagnitude < 0.01f) sideways = Vector3.right;
                    offset = sideways.normalized * PilotPerks.GuidanceOffset;
                }
                perMissile.Offsets.Add(pilot, offset);
            }
            aimPoint += offset;
        }
    }

#pragma warning disable IDE0051
    [HarmonyPatch(typeof(FuelTank), nameof(FuelTank.UseFuel))]
    internal static class WingFuelDisciplinePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft ___aircraft, ref float rate) =>
            rate = WingSurvivalPerks.FuelUse(___aircraft, rate);
    }

    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeGForceDamage))]
    internal static class WingGTolerancePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, ref float sqrGForces, byte ___pilotNumber)
        {
            if (___pilotNumber == 0)
                sqrGForces = PilotPerks.GLoad(sqrGForces,
                    WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.GTolerance));
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.SetAimpoint))]
    internal static class WingLuckPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, ref GlobalPosition aimPoint) =>
            WingSurvivalPerks.BiasGuidance(__instance, ref aimPoint);
    }

    // Temporarily change instance tuning for the native synchronous call; finalizers restore it even
    // if another mod or native code throws. Native delayed ejection and ammo bookkeeping stay intact.
    [HarmonyPatch(typeof(ChaffEjector), nameof(ChaffEjector.Fire))]
    internal static class WingChaffReflexPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ChaffEjector __instance, ref float ___ejectionInterval, out float? __state)
        {
            __state = ___ejectionInterval;
            if (WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.ChaffReflex)) ___ejectionInterval *= 0.5f;
        }
        [HarmonyFinalizer]
        internal static void Finalizer(ref float ___ejectionInterval, float? __state) { if (__state.HasValue) ___ejectionInterval = __state.Value; }
    }

    [HarmonyPatch(typeof(FlareEjector), nameof(FlareEjector.Fire))]
    internal static class WingFlareReflexPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(FlareEjector __instance, ref float ___ejectionInterval, out float? __state)
        {
            __state = ___ejectionInterval;
            if (WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.FlareReflex)) ___ejectionInterval *= 0.5f;
        }
        [HarmonyFinalizer]
        internal static void Finalizer(ref float ___ejectionInterval, float? __state) { if (__state.HasValue) ___ejectionInterval = __state.Value; }
    }

    [HarmonyPatch(typeof(RadarJammer), nameof(RadarJammer.Fire))]
    internal static class WingEcmSpecialistPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(RadarJammer __instance, ref float ___jammingIntensity, out float? __state)
        {
            __state = ___jammingIntensity;
            if (WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.EcmSpecialist)) ___jammingIntensity *= 1.75f;
        }
        [HarmonyFinalizer]
        internal static void Finalizer(ref float ___jammingIntensity, float? __state) { if (__state.HasValue) ___jammingIntensity = __state.Value; }
    }

    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.TakeDamage))]
    internal static class WingSurvivalistPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(PilotDismounted __instance, ref float pierceDamage,
                                    ref float blastDamage, ref float fireDamage)
        {
            if (__instance == null || !__instance.IsServer || __instance.disabled ||
                __instance.animationState == PilotDismounted.PilotState.dead ||
                !WingSurvivalPerks.Has(WingSearchAndRescue.PilotOf(__instance), PilotPerk.Survivalist)) return;
            if (pierceDamage > 0f) pierceDamage *= 0.4f;
            if (blastDamage > 0f) blastDamage *= 0.4f;
            if (fireDamage > 0f) fireDamage *= 0.4f;
        }
    }
}
