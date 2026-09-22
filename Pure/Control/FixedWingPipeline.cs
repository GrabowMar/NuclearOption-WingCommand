namespace WingCommand
{
    /// <summary>The per-aircraft flight pipeline: accel-stage constraints → lift-vector mapping →
    /// attitude-stage constraints → inner loops. Guidance is computed by the caller (tracking or hold).
    /// The engine and the FlightSim run this same class. One instance per aircraft for its lifetime.</summary>
    internal sealed class FixedWingPipeline
    {
        public readonly FixedWingController Controller = new FixedWingController();
        public readonly ConstraintChain Constraints = new ConstraintChain();
        public BindingReport Report;
        public AttitudeCommand LastAttitude;
        private float lastBankCmd;

        public ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx,
            AirframeProfile p, float dt)
        {
            Report = default;
            GuidanceCommand g = guidance;
            Constraints.ApplyAccel(ref g, s, ctx, p, ref Report);
            AttitudeCommand a = AccelMapping.Map(g, s.Vel, lastBankCmd);
            Constraints.ApplyAttitude(ref a, s, ctx, p, dt, ref Report);
            lastBankCmd = a.BankDeg;
            LastAttitude = a;
            return Controller.Step(a, s, p, dt);
        }

        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p)
        {
            Controller.Track(s, applied, p);
            Constraints.Track(s);
            lastBankCmd = s.BankDeg;
        }
    }
}
