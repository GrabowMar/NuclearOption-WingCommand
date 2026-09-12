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

        internal static void ProtectPilotDamage(Aircraft aircraft, ref float pierce, ref float blast,
                                                ref float fire, ref float impact, ref float hitPoints)
        {
            bool tough = Has(aircraft, PilotPerk.Toughness);
            pierce = PilotPerks.PilotDamage(pierce, tough);
            blast = PilotPerks.PilotDamage(blast, tough);
            fire = PilotPerks.PilotDamage(fire, tough);
            impact = PilotPerks.PilotDamage(impact, tough);
        }

        internal static void BiasGuidance(Missile missile, ref GlobalPosition aimPoint)
        {
            if (missile == null || !missile.IsServer || !missile.LocalSim || missile.disabled ||
                !UnitRegistry.TryGetUnit(missile.targetID, out Unit target) || !(target is Aircraft aircraft) ||
                aircraft.disabled) return;
            WingPilot pilot = WingPilotRoster.Of(aircraft);
            if (!Has(aircraft, PilotPerk.Ghost) && !Has(aircraft, PilotPerk.NotchExpert)) return;
            MissileRolls perMissile = rolls.GetValue(missile, _ => new MissileRolls());
            if (!perMissile.Offsets.TryGetValue(pilot, out Vector3 offset))
            {
                offset = Vector3.zero;
                string seeker = missile.GetSeekerType();
                bool radar = seeker == "ARH" || seeker == "SARH";
                Vector3 direction = aircraft.GlobalPosition() - missile.GlobalPosition();
                bool beaming = aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 900f &&
                    Math.Abs(Vector3.Dot(aircraft.rb.velocity.normalized, direction.normalized)) < 0.258819f;
                float chance = PilotPerks.MissileErrorChance(Has(aircraft, PilotPerk.Ghost),
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
    [HarmonyPatch(typeof(Missile), nameof(Missile.SetAimpoint))]
    internal static class WingLuckPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, ref GlobalPosition aimPoint) =>
            WingSurvivalPerks.BiasGuidance(__instance, ref aimPoint);
    }

    // Temporarily change instance tuning for the native synchronous call; finalizers restore it even
    // if another mod or native code throws. Native delayed ejection and ammo bookkeeping stay intact.
    [HarmonyPatch(typeof(FlareEjector), nameof(FlareEjector.Fire))]
    internal static class WingFlareReflexPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(FlareEjector __instance, ref float ___ejectionInterval, out float? __state)
        {
            __state = ___ejectionInterval;
            if (WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.Countermeasures)) ___ejectionInterval *= 0.5f;
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
            if (WingSurvivalPerks.Has(__instance.aircraft, PilotPerk.Countermeasures)) ___jammingIntensity *= 2.0f;
        }
        [HarmonyFinalizer]
        internal static void Finalizer(ref float ___jammingIntensity, float? __state) { if (__state.HasValue) ___jammingIntensity = __state.Value; }
    }
}
