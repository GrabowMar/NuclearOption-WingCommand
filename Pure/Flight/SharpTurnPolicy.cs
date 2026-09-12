using System;

namespace WingCommand
{
    /// <summary>Pure deterministic flight policy for sharp fixed-wing tactical turns.</summary>
    internal static class SharpTurnPolicy
    {
        /// <summary>Minimum altitude above ground (metres) before sharp combat turns are permitted.</summary>
        public const float SafeFloorAgl = 60f;

        /// <summary>Minimum turn angle (degrees) to engage sharp turn dynamics.</summary>
        public const float SharpTurnMinAngleDeg = 30f;

        /// <summary>Check whether an aircraft is eligible for sharp fixed-wing combat turning.</summary>
        public static bool IsEligible(
            bool isPlaneAutopilot,
            bool isDisabled,
            bool hasPlayer,
            bool runwayAlign,
            bool gearDeployed,
            float radarAlt,
            float speed,
            float landingSpeed)
        {
            if (!isPlaneAutopilot) return false;
            if (isDisabled || hasPlayer || runwayAlign || gearDeployed) return false;
            if (radarAlt < SafeFloorAgl) return false;
            if (speed < landingSpeed * 1.15f) return false;
            return true;
        }

        /// <summary>Scale allowed bank angle ceiling with radar altitude, unlocking 135-140 deg at altitude.</summary>
        public static float SafeBankCeiling(float currentCeiling, float radarAlt)
        {
            if (radarAlt < SafeFloorAgl)
                return Math.Min(currentCeiling, 60f);

            // Scale from 70 deg at 60m up to 140 deg at 200m+
            float altScale = Math.Max(0f, Math.Min(1f, (radarAlt - SafeFloorAgl) / 140f));
            float allowed = 70f + altScale * 70f;
            return Math.Max(currentCeiling, allowed);
        }

        /// <summary>Speed control during sharp turns: deploy airbrake above 1.15x corner speed, full throttle otherwise.
        /// Inhibit airbrake near the ground or during high sink rates.</summary>
        public static (float throttle, float brake) ComputeSpeedControl(
            float airspeed,
            float cornerSpeed,
            float turnAngleDeg,
            bool energyFighter,
            float radarAlt = 1000f,
            float verticalSpeed = 0f)
        {
            // Ground safety: never cut power or deploy airbrake below 200m or in a descent
            if (radarAlt < 200f || verticalSpeed < -10f)
                return (1f, 0f);

            if (turnAngleDeg < 35f)
                return (1f, 0f);

            // Energy fighters preserve speed and do not deliberately dump energy below corner speed
            if (energyFighter && airspeed < cornerSpeed)
                return (1f, 0f);

            // If significantly above corner speed into a sharp turn, brake into corner speed
            if (airspeed > cornerSpeed * 1.15f)
                return (0f, 1f);

            return (1f, 0f);
        }

        /// <summary>Calculate elevator authority boost during roll-in to execute slice turns rather than stalling pitch.</summary>
        public static float ComputePitchAuthority(float rollErrorDeg, float turnAngleDeg)
        {
            if (turnAngleDeg < SharpTurnMinAngleDeg) return 1f;
            float absRoll = Math.Abs(rollErrorDeg);
            if (absRoll > 60f) return 1f;

            // In stock AutoAim, pitch was throttled down to ~10-20% during roll.
            // As bank aligns with target direction, blend pitch authority multiplier from 1.0 up to 2.2x.
            float alignFactor = Math.Max(0f, (60f - absRoll) / 60f);
            return 1f + alignFactor * 1.2f;
        }

        /// <summary>Coordinated rudder kick into the turn direction to assist roll-in and yaw rate.</summary>
        public static float ComputeRudderKick(float headingErrDeg, float rollErrorDeg, float radarAlt)
        {
            if (radarAlt < 30f) return 0f;
            if (Math.Abs(headingErrDeg) < 20f) return 0f;

            float sign = Math.Sign(headingErrDeg);
            float rollFactor = Math.Min(1f, Math.Abs(rollErrorDeg) / 45f);
            // Deflect 0.15 to 0.35 into the turn during roll entry
            return (float)(sign * (0.15 + 0.20 * rollFactor));
        }

        /// <summary>Defensive bank angle ceiling for fixed-wing missile break turns.</summary>
        public static float DefensiveBankLimit(bool terminal, bool hasBreakTurnPerk, float radarAlt)
        {
            if (radarAlt < SafeFloorAgl) return 60f;
            if (terminal && hasBreakTurnPerk && radarAlt >= 120f) return 180f;

            if (terminal)
            {
                float altScale = Math.Max(0f, Math.Min(1f, (radarAlt - SafeFloorAgl) / 100f));
                return 90f + altScale * 45f; // 90 to 135 deg
            }

            float normScale = Math.Max(0f, Math.Min(1f, (radarAlt - SafeFloorAgl) / 100f));
            return 75f + normScale * 35f; // 75 to 110 deg
        }
    }
}
