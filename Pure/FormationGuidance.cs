using System;
using System.Numerics;

namespace WingCommand
{
    /// <summary>The production horizontal command, before terrain and collision overrides.</summary>
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
            // Filtered slot motion already removes twitch noise. Keep position
            // correction proportional so small movements do not wait for a dead zone.
            Vector2 correction = cross * (1.35f * aggression) - drift * (5f * damping);
            if (correction.LengthSquared() > limit * limit)
                correction = Vector2.Normalize(correction) * limit;
            float travelTime = Math.Max(0.1f, distance / Math.Max(speed, 50f));
            var capture = FormationTracking.Capture(rendezvous.X, rendezvous.Y,
                ownVelocity.X, ownVelocity.Y, arrivalVelocity.X, arrivalVelocity.Y,
                travelTime, lookAhead / Math.Max(ownVelocity.Length(), 50f));
            return new HorizontalCommand(Vector2.Lerp(forward * lookAhead + correction,
                new Vector2(capture.x, capture.z), acquisition), correction, limit);
        }

        // AircraftInfo uses km/h (EncyclopediaBrowser divides stallSpeed by 3.6).
        // AircraftParameters.landingSpeed is an AI approach target, not a stall
        // limit: the VT-7 declares 100m/s there but actually stalls at 50m/s.
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

        // A rapid leader roll may need turn authority sooner than the calm-flight
        // ramp permits. This only raises permission: AutoAim still chooses the
        // demanded bank, and terrain, airspeed and pitch-down limits remain final.
        public static float BankRiseRate(float leaderBankRate) =>
            WingTuning.FormationBankRiseRate + 60f * Smooth01(
                ((float)Math.Abs(leaderBankRate * 180d / Math.PI) - 15f) / 75f);

        private static float Smooth01(float value)
        { value = Clamp(value, 0f, 1f); return value * value * (3f - 2f * value); }
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
