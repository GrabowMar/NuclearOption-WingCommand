namespace WingCommand
{
    /// <summary>One aircraft's flight stack below the behaviours: its guidance law and its control chain. The seam
    /// between airframe classes (spec M2 §2.1): behaviours, formation and the wing frame are shared, and only the
    /// pipeline differs. One instance per aircraft for its lifetime; <see cref="Track"/> makes a handover bumpless.</summary>
    internal interface IFlightPipeline
    {
        /// <summary>The class's guidance law: the acceleration and velocity that track the intent's reference.</summary>
        GuidanceCommand Guide(in FlightIntent intent, in AircraftState s, AirframeProfile p);

        /// <summary>Constraints and inner loops: guidance in, stick out (Pure signs).</summary>
        ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx, AirframeProfile p, float dt);

        /// <summary>Seed every loop from the aircraft and the output actually applied.</summary>
        void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p);

        /// <summary>The roll stick really applied after this tick's Step, when the caller overrode the output.</summary>
        void NoteAppliedRoll(float appliedRoll);

        /// <summary>Which constraint bound the last command, for the HUD, overlay and telemetry.</summary>
        BindingReport Report { get; }

        /// <summary>The attitude-level demand of the last tick (bank, load factor, energy rate) for telemetry.</summary>
        AttitudeCommand LastAttitude { get; }

        bool GcasActive { get; }
    }

    /// <summary>The only switch on <see cref="AirframeClass"/>: which pipeline an aircraft flies with.</summary>
    internal static class FlightStack
    {
        public static IFlightPipeline NewPipeline(AirframeClass cls)
        {
            switch (cls)
            {
                case AirframeClass.Rotary: return new RotaryPipeline();
                case AirframeClass.Tiltwing: return new TiltwingPipeline();
                default: return new FixedWingPipeline();
            }
        }
    }
}
