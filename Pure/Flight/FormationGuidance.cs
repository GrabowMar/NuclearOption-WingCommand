using System;
using System.Numerics;

namespace WingCommand
{
    /// <summary>Horizontal formation command before terrain and collision safety overrides.</summary>
    internal static class FormationGuidance
    {
        // AutoAim adds a high-speed zoom climb for destinations within 2 km at >60 degrees
        // to velocity. A formation aim is a direction, not a nearby point to circle around.
        // With followTerrain=false, extending this ray preserves normal steering and terrain checks.
        internal static Vector3 SteeringAim(Vector3 offset)
        {
            float distance = offset.Length();
            return distance > 0.001f && distance < 2100f ? offset * (2100f / distance) : offset;
        }

        internal readonly struct HorizontalCommand
        {
            public readonly Vector2 Aim, Correction;
            public readonly float MaxCorrection;
            public HorizontalCommand(Vector2 aim, Vector2 correction, float maxCorrection)
            { Aim = aim; Correction = correction; MaxCorrection = maxCorrection; }
        }

        public static HorizontalCommand Horizontal(Vector2 toSlot, Vector2 ownVelocity,
            Vector2 slotVelocity, Vector2 forward, Vector2 rendezvous, Vector2 arrivalVelocity,
            float distance, float lookAhead, float speed,
            float acquisition, float aggression, float damping)
        {
            float limit = lookAhead * (float)Math.Tan(WingTuning.CommandAngle * Math.PI / 180d);
            Vector2 cross = toSlot - forward * Vector2.Dot(toSlot, forward);
            Vector2 drift = ownVelocity - slotVelocity;
            drift -= forward * Vector2.Dot(drift, forward);
            // Filtered slot motion handles noise; proportional correction responds to small moves
            // without a dead zone.
            Vector2 correction = cross * (1.35f * aggression) - drift * (5f * damping);
            if (correction.LengthSquared() > limit * limit)
                correction = Vector2.Normalize(correction) * limit;
            float travelTime = Math.Max(0.1f, distance / Math.Max(speed, 50f));
            var capture = FormationTracking.Capture(rendezvous.X, rendezvous.Y,
                ownVelocity.X, ownVelocity.Y, arrivalVelocity.X, arrivalVelocity.Y,
                travelTime, lookAhead / Math.Max(ownVelocity.Length(), 50f));
            Vector2 pursuit = new Vector2(capture.x, capture.z);
            // Far from formation, fly directly toward the future meeting point. A Hermite
            // departure tangent favours our existing heading and delays crossing intercepts.
            // Fade back to the curved, velocity-matched arrival before entering formation.
            if (rendezvous.LengthSquared() > 1f)
                pursuit = Vector2.Lerp(pursuit, Vector2.Normalize(rendezvous) * lookAhead,
                    FormationIntercept.LongRangeBlend(distance));
            return new HorizontalCommand(Vector2.Lerp(forward * lookAhead + correction,
                pursuit, acquisition), correction, limit);
        }

        // Spend bank authority on large course reversals, then relax it as the intercept
        // lines up so the aircraft can recover speed. Caller retains terrain/energy limits.
        public static float InterceptBank(float commandAngle) =>
            Clamp(Math.Abs(commandAngle) * 2f, 8f, WingTuning.PursuitBank);

        // Convert published stall speed from km/h. AircraftParameters.landingSpeed is an approach
        // target, not stall speed; the VT-7's values differ substantially.
        public static float StallAirspeed(float publishedStallKmh, float nominalLandingSpeed) =>
            publishedStallKmh > 0f && !float.IsInfinity(publishedStallKmh)
                ? publishedStallKmh / 3.6f : Math.Max(1f, nominalLandingSpeed);

        public static float MinimumAirspeed(float publishedStallKmh, float nominalLandingSpeed) =>
            StallAirspeed(publishedStallKmh, nominalLandingSpeed) * 1.2f;

        public static float AirborneBankLimit(float altitude, float airspeed, float stallAirspeed)
        {
            float clearance = Clamp((altitude - WingTuning.FixedWingAirborneAlt) / WingTuning.RejoinBankHeightSpan, 0f, 1f);
            float terrain = WingTuning.DepartureTurnBank + clearance *
                (WingTuning.RejoinMaximumBank - WingTuning.DepartureTurnBank);
            float ratio = Math.Max(1f, stallAirspeed) * 1.1f / Math.Max(1f, airspeed);
            float energy = (float)(Math.Acos(Math.Min(1f, ratio * ratio)) * 180d / Math.PI);
            return Math.Max(WingTuning.RejoinMinimumBank, Math.Min(terrain, energy));
        }

        /// <summary>Allowable pitch-up fades toward a quarter of the request as airspeed approaches
        /// the minimum safe speed, then restores with the energy margin. Prevents a low-energy climb
        /// demand from mushing the aircraft when a large altitude correction is pending, while
        /// keeping a residual climb so the controller can still ease over rising terrain until the
        /// native warning or the terrain-abort reflex takes over.</summary>
        public static float ClimbAuthority(float airspeed, float minimumAirspeed, float allowedPitchUp)
        {
            float margin = Math.Max(1f, WingTuning.ClimbEnergyMargin);
            float scale = Math.Max(0f, Math.Min(1f, (airspeed - minimumAirspeed) / margin));
            return allowedPitchUp * (0.25f + 0.75f * scale);
        }

        // Permit faster bank-authority rise during rapid leader roll; native demand and terrain,
        // airspeed, and pitch-down limits still govern.
        public static float BankRiseRate(float leaderBankRate) =>
            WingTuning.FormationBankRiseRate + 60f * Smooth01(
                ((float)Math.Abs(leaderBankRate * 180d / Math.PI) - 15f) / 75f);

        /// <summary>Near station, request the lateral acceleration needed to follow the measured
        /// course rate. Native AutoAim's upward bank bias needs a finite heading lead even at zero
        /// slot error. This inverts its level-flight geometry; vertical guidance and native bank,
        /// terrain, and airspeed limits still govern climbs and descents.</summary>
        public static float TurnLeadDegrees(float turnRate, float horizontalSpeed,
            float bankLimit, float stationBlend)
        {
            float preview = Clamp(turnRate * 0.15f * 180f / (float)Math.PI, -45f, 45f);
            float blend = Clamp(stationBlend, 0f, 1f);
            if (blend <= 0f) return preview;

            double lateralG = Math.Min(Math.Max(0f, horizontalSpeed) * Math.Abs(turnRate) / 9.81d,
                Math.Tan(Clamp(bankLimit, 0f, 88f) * Math.PI / 180d));
            double low = 0d, high = 15d;
            if (lateralG > 0d)
            {
                // For a level waypoint, native desired tan(bank) is
                // sin(headingLead) * max(headingLeadDegrees, 5). Keep the lead at or below 15
                // degrees, before its additional large-angle upward bias begins.
                for (int i = 0; i < 12; i++)
                {
                    double middle = (low + high) * 0.5d;
                    double nativeLateralG = Math.Sin(middle * Math.PI / 180d) * Math.Max(middle, 5d);
                    if (nativeLateralG < lateralG) low = middle;
                    else high = middle;
                }
            }
            else high = 0d;

            float lead = (float)((low + high) * 0.5d) * Math.Sign(turnRate);
            return preview + (lead - preview) * blend;
        }

        private static float Smooth01(float value)
        { value = Clamp(value, 0f, 1f); return value * value * (3f - 2f * value); }
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
