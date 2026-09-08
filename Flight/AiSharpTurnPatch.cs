using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Allow powered, airborne AI to commit to large heading changes.</summary>
    [HarmonyPatch(typeof(AutopilotPlane), nameof(AutopilotPlane.AutoAim))]
    internal static class AiSharpTurnPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft ___aircraft, GlobalPosition destination,
                                   bool runwayAlign, ref float effort)
        {
            if (!Plugin.Settings.AiSharpTurns.Value) return;
            Aircraft aircraft = ___aircraft;
            if (aircraft == null || aircraft.disabled || aircraft.Player != null ||
                aircraft.rb == null || runwayAlign || effort > 1f)
                return;

            // Leave low flight and slow approaches under the stock energy protection.
            if (aircraft.radarAlt < Mathf.Max(150f, aircraft.maxRadius) ||
                aircraft.rb.velocity.magnitude < aircraft.GetAircraftParameters().landingSpeed * 1.3f)
                return;

            Vector3 toDestination = destination - aircraft.GlobalPosition();
            if (toDestination.sqrMagnitude < 1f ||
                Vector3.Angle(aircraft.rb.velocity, toDestination) < 60f)
                return;

            // Native effort > 1 removes the corner-speed steering attenuation. AutoAim still
            // applies terrain avoidance, bank constraints and the aircraft's input filters.
            effort = 2f;
        }
    }
}
