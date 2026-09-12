using UnityEngine;

namespace WingCommand
{
    /// <summary>Shared AutoAim clamps: fixed-wing altitude from maxRadius to 8 km, rotary altitude from
    /// minimumRadarAlt, and pursuit bank below inversion.</summary>
    internal static class AutopilotMath
    {
        /// <summary>Recompute safe forward controls during terrain recovery and missile-warning gaps.
        /// Native AutoAim levels the wings before applying its pitch demand and retains terrain checks.</summary>
        public static void RecoverFlight(Aircraft aircraft, ControlInputs inputs)
        {
            if (aircraft == null || aircraft.autopilot == null || inputs == null) return;
            Vector3 forward = aircraft.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            float vertSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            float urgency = aircraft.autopilot.GetTerrainWarningSystem()?.urgency ?? 0f;
            bool immediateDanger = TerrainAbortPolicy.ImmediateDanger(aircraft.radarAlt, vertSpeed, urgency);
            bool sinking = vertSpeed < -5f;
            bool lowTerrain = aircraft.radarAlt < TerrainAbortPolicy.AbortAlt;

            bool rotary = WingRegistry.IsRotary(aircraft);

            // A gentle forward recovery restores energy; use the existing climb demand near terrain.
            float climb = (immediateDanger || sinking || lowTerrain || rotary) ? 250f : 50f;
            GlobalPosition destination = aircraft.GlobalPosition() + forward * 800f + Vector3.up * climb;
            if (rotary)
            {
                aircraft.autopilot.AutoAim(destination, RotaryAgl(aircraft, aircraft.radarAlt + climb),
                    Vector3.zero, Vector3.zero, true);
                return;
            }
            inputs.throttle = 1f;
            inputs.brake = 0f;
            aircraft.autopilot.AutoAim(destination, true, false, false, 2f,
                FormationControlRules.BankInput(12f, aircraft.radarAlt), false,
                CruiseHold(aircraft, aircraft.radarAlt + climb), Vector3.zero);

            // Keep native actuator signs and filtering. Positive raw pitch is nose-down;
            // forcing a positive "pull" here reverses the native terrain-escape command.
        }

        /// <summary>Clamp fixed-wing held altitude to the airframe turn-radius floor and 8 km
        /// ceiling.</summary>
        public static float CruiseHold(Aircraft aircraft, float desired) =>
            Mathf.Clamp(desired, aircraft.maxRadius, 8000f);

        /// <summary>Clamp rotary AGL to the airframe minimumRadarAlt and task-specific limits.</summary>
        public static float RotaryAgl(Aircraft aircraft, float desired,
                                      float min = 25f, float max = 3000f) =>
            Mathf.Clamp(Mathf.Max(aircraft.GetAircraftParameters().minimumRadarAlt, desired),
                        min, max);

        /// <summary>Pursuit bank limit below inversion.</summary>
        public static float PursuitBank() =>
            Mathf.Min(WingTuning.PursuitBank, FixedWingFormation.MaxSafeBank);
    }
}
