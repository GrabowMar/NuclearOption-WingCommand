namespace WingCommand
{
    /// <summary>Who flies the member under test this tick: its own brain, the test's open-loop stick, or the
    /// brain with the test's throttle.</summary>
    internal enum StepPhase : byte { Brain, Open, Throttle }

    /// <summary>One tick of the step test (Pure signs). <see cref="Throttle"/> applies in the Throttle phase;
    /// an Open pulse keeps the throttle the brain last applied.</summary>
    internal struct StepCommand
    {
        public bool Done;
        public StepPhase Phase;
        public float Pitch, Roll, Throttle;
    }

    /// <summary>The calibration step test. The brain flies between short open-loop pulses and recovers from each
    /// one; an open-loop test left the aircraft banked with the stick centred and it dived into the ground in game.
    /// <list type="table">
    /// <item><term>4.0–4.5 s / 9.0–9.5 s</term><description>roll ±0.5, pitch stick centred</description></item>
    /// <item><term>14–15 s / 19–20 s</term><description>pitch ±0.3, roll stick centred</description></item>
    /// <item><term>24–30 s</term><description>military power (0.89), the brain flies attitude</description></item>
    /// <item><term>30–36 s</term><description>idle with the airbrake, the brain flies attitude</description></item>
    /// </list>
    /// The brain flies everything else, up to <see cref="Duration"/>. <see cref="Unsafe"/> ends the test early.</summary>
    internal static class StepSequence
    {
        public static float RollRightAt = 4f, RollLeftAt = 9f, RollPulse = 0.5f, RollStick = 0.5f;
        public static float PitchUpAt = 14f, PitchDownAt = 19f, PitchPulse = 1f, PitchStick = 0.3f;
        public static float ThrustFrom = 24f, ThrustTo = 30f, BrakeFrom = 30f, BrakeTo = 36f, SpoolSeconds = 2f;
        public static float MilitaryThrottle = 0.89f, Duration = 38f;
        public static float AbortAglFraction = 0.5f, AbortSink = 40f;

        public static StepCommand At(float t)
        {
            var c = new StepCommand();
            if (t >= Duration)
            {
                c.Done = true;
                return c;
            }
            if (In(t, RollRightAt, RollPulse)) Open(ref c, 0f, RollStick);
            else if (In(t, RollLeftAt, RollPulse)) Open(ref c, 0f, -RollStick);
            else if (In(t, PitchUpAt, PitchPulse)) Open(ref c, PitchStick, 0f);
            else if (In(t, PitchDownAt, PitchPulse)) Open(ref c, -PitchStick, 0f);
            else if (t >= ThrustFrom && t < ThrustTo)
            {
                c.Phase = StepPhase.Throttle;
                c.Throttle = MilitaryThrottle;
            }
            else if (t >= BrakeFrom && t < BrakeTo) c.Phase = StepPhase.Throttle;
            return c;
        }

        /// <summary>Too low (under <see cref="AbortAglFraction"/> of the test's minimum height) or sinking faster
        /// than <see cref="AbortSink"/>: the brain takes the member back at once.</summary>
        public static bool Unsafe(in AircraftState s, float minAgl) =>
            s.RadarAlt < AbortAglFraction * minAgl || s.Vel.Y < -AbortSink;

        /// <summary>True when <paramref name="t"/> is inside an open-loop roll pulse (the roll fit's rows).</summary>
        public static bool InRollPulse(float t) => In(t, RollRightAt, RollPulse) || In(t, RollLeftAt, RollPulse);

        private static bool In(float t, float from, float length) => t >= from && t < from + length;

        private static void Open(ref StepCommand c, float pitch, float roll)
        {
            c.Phase = StepPhase.Open;
            c.Pitch = pitch;
            c.Roll = roll;
        }
    }
}
