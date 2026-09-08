using System;

namespace WingCommand
{
    /// <summary>Read-only flight telemetry for influences without live aircraft or order mutation
    /// access.</summary>
    public readonly struct WingFlightSituation
    {
        public readonly WingSituation Situation;
        public readonly float SlotError;
        public readonly float CaptureDistance;
        public readonly float RelativeClosingSpeed;
        public readonly float LeaderSpeed;
        public readonly float PilotSkill;
        /// <summary>Safe formation airspeed in m/s, or zero when unavailable.</summary>
        public readonly float MinimumAirspeed;

        public WingFlightSituation(in WingSituation situation, float slotError, float captureDistance,
            float relativeClosingSpeed, float leaderSpeed, float pilotSkill)
        {
            Situation = situation;
            SlotError = Math.Max(0f, slotError);
            CaptureDistance = Math.Max(1f, captureDistance);
            RelativeClosingSpeed = relativeClosingSpeed;
            LeaderSpeed = Math.Max(0f, leaderSpeed);
            PilotSkill = WingFlightProfile.Clamp(pilotSkill, 0f, 1f);
            MinimumAirspeed = 0f;
        }

        private WingFlightSituation(in WingFlightSituation basis, float minimumAirspeed)
        {
            this = basis;
            MinimumAirspeed = float.IsNaN(minimumAirspeed) || float.IsInfinity(minimumAirspeed)
                ? 0f : Math.Max(0f, minimumAirspeed);
        }

        public WingFlightSituation WithMinimumAirspeed(float minimumAirspeed) =>
            new WingFlightSituation(in this, minimumAirspeed);
    }

    /// <summary>Weighted flight adjustment without task or control ownership; combine all eligible
    /// contributions each tick.</summary>
    public interface IWingInfluence
    {
        string Id { get; }
        bool RequiresSmartMode { get; }
        WingFlightContribution Evaluate(in WingFlightSituation situation);
    }

    public readonly struct WingFlightContribution
    {
        public readonly float Weight;
        public readonly float CaptureGain;
        public readonly float SpacingScale;
        public readonly float DampingScale;
        public readonly float BankScale;

        public WingFlightContribution(float weight, float captureGain = 1f, float spacingScale = 1f,
            float dampingScale = 1f, float bankScale = 1f)
        {
            Weight = weight;
            CaptureGain = captureGain;
            SpacingScale = spacingScale;
            DampingScale = dampingScale;
            BankScale = bankScale;
        }

        internal bool IsFinite => Finite(Weight) && Finite(CaptureGain) && Finite(SpacingScale) &&
                                  Finite(DampingScale) && Finite(BankScale);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Bounded combined profile applied once by flight, subject to existing safety
    /// limits.</summary>
    public readonly struct WingFlightProfile
    {
        public readonly float CaptureGain;
        public readonly float SpacingScale;
        public readonly float DampingScale;
        public readonly float BankScale;
        public static WingFlightProfile Neutral => new WingFlightProfile(1f, 1f, 1f, 1f);

        internal WingFlightProfile(float capture, float spacing, float damping, float bank)
        {
            CaptureGain = Clamp(capture, 0.65f, 1.35f);
            SpacingScale = Clamp(spacing, 0.85f, 1.6f);
            DampingScale = Clamp(damping, 1f, 1.5f);
            BankScale = Clamp(bank, 0.65f, 1f);
        }

        internal static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
        internal static float CombineSpacing(float current, float next) =>
            Math.Max(Clamp(current, 0.85f, 1.6f), Clamp(next, 0.85f, 1.6f));

        internal static float LimitBank(float requested, float levelBank, float scale) =>
            Math.Min(requested, levelBank + (requested - levelBank) * Clamp(scale, 0.65f, 1f));
        internal static float Smooth(float value)
        {
            float x = Clamp(value, 0f, 1f);
            return x * x * (3f - 2f * x);
        }
    }
}
