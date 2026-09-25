using System;

namespace WingCommand
{
    /// <summary>The helicopter flight pipeline: rotary guidance → acceleration-stage constraints (the wing's collision
    /// bias, the terrain floor) → <see cref="RotaryController"/>. No stall, loaded-minimum or bank-ceiling logic: the
    /// tilt limit is the controller's.</summary>
    internal sealed class RotaryPipeline : IFlightPipeline
    {
        /// <summary>Seconds to close the height margin under the floor + clearance; above it, the sink allowed.</summary>
        public static float FloorTau = 3f;

        public readonly RotaryController Controller = new RotaryController();
        private BindingReport report;

        public BindingReport Report => report;
        public AttitudeCommand LastAttitude { get; private set; }
        public bool GcasActive => false;   // ponytail: rotary GCAS arrives with the terrain-hugging roles (M4)

        public GuidanceCommand Guide(in FlightIntent intent, in AircraftState s, AirframeProfile p) =>
            RotaryGuidance.Evaluate(intent, s, p);

        public ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx, AirframeProfile p, float dt)
        {
            report = default;
            GuidanceCommand g = guidance;
            if (ctx.CollisionBias.SqrLength > 1e-4f)
            {
                g.Accel += ctx.CollisionBias;
                report.CollisionActive = true;
            }
            if (!float.IsNaN(ctx.FloorY))
            {
                float margin = s.Pos.Y - ctx.FloorY - ctx.Clearance;
                float minVy = margin < 0f ? Math.Min(p.ClimbRateMax, -margin / FloorTau) : -margin / FloorTau;
                if (g.VelCmd.Y < minVy)
                {
                    float dv = minVy - g.VelCmd.Y;
                    g.VelCmd = new Vec3(g.VelCmd.X, minVy, g.VelCmd.Z);
                    g.Accel += Vec3.Up * (dv / Math.Max(0.1f, p.TauVel));
                    report.VerticalBy = ConstraintId.Terrain;
                }
            }
            ControlOutput o = Controller.Step(g, s, p, dt);
            float tilt = (float)Math.Sqrt(Controller.PitchTargetDeg * Controller.PitchTargetDeg +
                                          Controller.RollTargetDeg * Controller.RollTargetDeg);
            LastAttitude = new AttitudeCommand
            {
                BankDeg = Controller.RollTargetDeg,
                Nz = 1f / (float)Math.Cos(tilt * Scalar.Deg2Rad),
                EnergyRate = g.VelCmd.Y,
            };
            return o;
        }

        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p) => Controller.Track(s, applied, p);

        public void NoteAppliedRoll(float appliedRoll) => Controller.NoteAppliedRoll(appliedRoll);
    }
}
