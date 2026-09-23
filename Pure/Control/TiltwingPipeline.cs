using System;

namespace WingCommand
{
    internal enum TiltwingMode : byte { Plane, Rotary }

    /// <summary>The tiltwing flight pipeline (spec M2 §4.4): the fixed-wing pipeline in plane mode, the rotary one in
    /// rotary mode, each with its own guidance law. The game's auto-tilt moves the nacelles with speed; this only
    /// chooses which stack flies.
    /// <list type="bullet">
    /// <item>Plane above ConversionHigh, rotary below ConversionLow (1.4 / 1.1 × the plane-mode stall), and at most one
    /// conversion per <see cref="MinDwell"/>. The first mode is a guess from the first speed seen, so it may convert
    /// at once.</item>
    /// <item>On a conversion the incoming pipeline tracks the aircraft and the output just produced (bumpless); the
    /// outgoing one idles.</item>
    /// <item>In rotary mode the speed is capped at max(<see cref="RotaryCapFactor"/> × ConversionHigh, the reference's
    /// speed + <see cref="RotaryCatchUp"/>): chasing a slow reference never runs it into plane mode, while a fast
    /// reference still carries it through the conversion.</item>
    /// </list></summary>
    internal sealed class TiltwingPipeline : IFlightPipeline
    {
        public static float MinDwell = 3f, RotaryCapFactor = 0.9f, RotaryCatchUp = 10f;

        public readonly FixedWingPipeline Plane = new FixedWingPipeline();
        public readonly RotaryPipeline Rotary = new RotaryPipeline();
        private bool primed;
        private float dwell;

        public TiltwingMode Mode { get; private set; }
        public int Conversions { get; private set; }

        private IFlightPipeline Active => Mode == TiltwingMode.Plane ? Plane : (IFlightPipeline)Rotary;

        public BindingReport Report => Active.Report;
        public AttitudeCommand LastAttitude => Active.LastAttitude;
        public bool GcasActive => Active.GcasActive;

        public GuidanceCommand Guide(in FlightIntent intent, in AircraftState s, AirframeProfile p)
        {
            Prime(s, p);
            if (Mode == TiltwingMode.Plane) return Plane.Guide(intent, s, p);
            float cap = Math.Max(RotaryCapFactor * p.ConversionHigh, intent.Ref.Vel.Horizontal.Length + RotaryCatchUp);
            FlightIntent rotary = intent;
            rotary.Limits = new SpeedLimits(intent.Limits.Min, intent.Limits.Max > 0f ? Math.Min(intent.Limits.Max, cap) : cap,
                intent.Limits.AfterburnerAllowed, intent.Limits.AirbrakeAllowed);
            return Rotary.Guide(rotary, s, p);
        }

        public ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx, AirframeProfile p, float dt)
        {
            Prime(s, p);
            ControlOutput o = Active.Step(guidance, s, ctx, p, dt);
            dwell += dt;
            TiltwingMode wanted = Mode == TiltwingMode.Plane
                ? (s.Tas < p.ConversionLow ? TiltwingMode.Rotary : TiltwingMode.Plane)
                : (s.Tas > p.ConversionHigh ? TiltwingMode.Plane : TiltwingMode.Rotary);
            if (wanted != Mode && dwell >= MinDwell - 1e-6f)
            {
                Mode = wanted;
                dwell = 0f;
                Conversions++;
                Active.Track(s, o, p);
            }
            return o;
        }

        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p)
        {
            Prime(s, p);
            Active.Track(s, applied, p);
        }

        public void NoteAppliedRoll(float appliedRoll) => Active.NoteAppliedRoll(appliedRoll);

        private void Prime(in AircraftState s, AirframeProfile p)
        {
            if (primed) return;
            primed = true;
            Mode = s.Tas >= 0.5f * (p.ConversionLow + p.ConversionHigh) ? TiltwingMode.Plane : TiltwingMode.Rotary;
            dwell = MinDwell;
        }
    }
}
