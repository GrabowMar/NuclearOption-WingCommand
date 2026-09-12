using System;
using System.Numerics;

namespace WingCommand
{
    /// <summary>Horizontal formation command before terrain and collision safety overrides.</summary>
    internal static class FormationGuidance
    {
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

        // Permit faster bank-authority rise during rapid leader roll; native demand and terrain,
        // airspeed, and pitch-down limits still govern.
        public static float BankRiseRate(float leaderBankRate) =>
            WingTuning.FormationBankRiseRate + 60f * Smooth01(
                ((float)Math.Abs(leaderBankRate * 180d / Math.PI) - 15f) / 75f);

        private static float Smooth01(float value)
        { value = Clamp(value, 0f, 1f); return value * value * (3f - 2f * value); }
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
