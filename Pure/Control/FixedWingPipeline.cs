namespace WingCommand
{
    /// <summary>The per-aircraft flight pipeline: accel-stage constraints → lift-vector mapping →
    /// attitude-stage constraints → inner loops. Guidance is computed by the caller (tracking or hold).
    /// The engine and the FlightSim run this same class. One instance per aircraft for its lifetime.
    /// <para>It keeps its own copy of the airframe profile whose roll rate is the authority learned in flight
    /// (<see cref="RollAuthority"/>), so the controller, the authority slew and GCAS all use what the aircraft
    /// actually does.</para></summary>
    internal sealed class FixedWingPipeline
    {
        public readonly FixedWingController Controller = new FixedWingController();
        public readonly ConstraintChain Constraints = new ConstraintChain();
        public readonly RollAuthority Roll = new RollAuthority();
        public BindingReport Report;
        public AttitudeCommand LastAttitude;
        private float lastBankCmd, lastRollStick;
        private AirframeProfile source, effective;

        public ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx,
            AirframeProfile p, float dt)
        {
            AirframeProfile e = Effective(p);
            Roll.Update(lastRollStick, s.P, dt);
            e.RollRateMaxDps = Roll.RateDps;

            Report = default;
            GuidanceCommand g = guidance;
            Constraints.ApplyAccel(ref g, s, ctx, e, ref Report);
            AttitudeCommand a = AccelMapping.Map(g, s.Vel, lastBankCmd);
            Constraints.ApplyAttitude(ref a, s, ctx, e, dt, ref Report);
            lastBankCmd = a.BankDeg;
            LastAttitude = a;
            ControlOutput o = Controller.Step(a, s, e, dt);
            lastRollStick = o.Roll;
            return o;
        }

        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p)
        {
            AirframeProfile e = Effective(p);
            e.RollRateMaxDps = Roll.RateDps;
            Controller.Track(s, applied, e);
            Constraints.Track(s);
            lastBankCmd = s.BankDeg;
            lastRollStick = applied.Roll;
        }

        /// <summary>The pipeline's copy of <paramref name="p"/>; a new profile (another airframe, a calibration
        /// reload) re-seeds the learned roll authority from it.</summary>
        private AirframeProfile Effective(AirframeProfile p)
        {
            if (ReferenceEquals(p, source)) return effective;
            source = p;
            effective = p.Clone();
            Roll.Reset(p.RollRateMaxDps);
            return effective;
        }
    }
}
