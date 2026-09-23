using System;

namespace WingCommand
{
    /// <summary>One ordered chain applied to every command, recording what bound it.
    /// <para>Acceleration stage: terrain floor (collision bias joins in M1b).</para>
    /// <para>Attitude stage: envelope (bank ceiling, lift- and structure-limited load factor, loaded
    /// minimum speed), then ground-collision avoidance, then authority slews that keep commands continuous.</para>
    /// Holds per-aircraft state (GCAS latch, last command), so there is one instance per aircraft.</summary>
    internal sealed class ConstraintChain
    {
        public const float GcasTrigger = 1.5f, GcasRelease = 3f;
        public const float BankFloorDeg = 60f, BankRangeDeg = 25f;
        public const float NzSlew = 3f;

        private bool gcas;
        private float lastBank, lastNz;
        private bool primed;

        public bool GcasActive => gcas;

        public void ApplyAccel(ref GuidanceCommand c, in AircraftState s, in LimitContext ctx, AirframeProfile p,
            ref BindingReport r)
        {
            if (float.IsNaN(ctx.FloorY)) return;
            // Keep the commanded vertical speed above what the height margin allows: climb back when
            // below the floor + clearance, otherwise descend no faster than a half-g pull can arrest.
            float margin = s.Pos.Y - ctx.FloorY - ctx.Clearance;
            float pullAccel = Math.Max(1f, 0.5f * (p.GLimit - 1f) * Scalar.G);
            float minVy = margin < 0f
                ? Math.Min(p.ClimbRateMax, -margin / 3f)
                : -(float)Math.Sqrt(2f * pullAccel * margin);
            if (c.VelCmd.Y >= minVy) return;
            float dv = minVy - c.VelCmd.Y;
            c.VelCmd = new Vec3(c.VelCmd.X, minVy, c.VelCmd.Z);
            c.Accel += Vec3.Up * (dv / Math.Max(0.1f, p.TauVel));
            r.VerticalBy = ConstraintId.Terrain;
        }

        public void ApplyAttitude(ref AttitudeCommand a, in AircraftState s, in LimitContext ctx, AirframeProfile p,
            float dt, ref BindingReport r)
        {
            // Envelope: bank ceiling from aggression, lowered toward 30° near the floor.
            float ceiling = BankFloorDeg + BankRangeDeg * Scalar.Clamp01(ctx.Aggression);
            if (!float.IsNaN(ctx.FloorY))
            {
                float height = s.Pos.Y - ctx.FloorY;
                ceiling = Math.Min(ceiling, Scalar.Lerp(30f, ceiling,
                    Scalar.SmoothStep(0.5f * ctx.Clearance, 2f * ctx.Clearance, height)));
            }
            if (Math.Abs(a.BankDeg) > ceiling)
            {
                // Keep the vertical lift and give up turn rate: holding the load factor at a shallower bank
                // would turn the surplus into a climb.
                float requested = a.BankDeg;
                float vertical = Math.Max(0f, a.Nz * (float)Math.Cos(requested * Scalar.Deg2Rad));
                a.BankDeg = Math.Sign(a.BankDeg) * ceiling;
                a.Nz = vertical / (float)Math.Cos(ceiling * Scalar.Deg2Rad);
                r.BindBank(ConstraintId.Envelope, requested, a.BankDeg);
            }

            // Envelope: load factor within structure and available lift; protect the loaded minimum speed.
            // Both scale with equivalent airspeed, so the limits hold at altitude.
            float nzMax = Math.Min(p.GLimit, p.LiftLimitedG(s.Eas));
            if (a.Nz > nzMax)
            {
                r.BindNz(ConstraintId.Envelope, a.Nz, nzMax);
                a.Nz = nzMax;
            }
            if (s.Eas < p.MinimumSpeed(Math.Max(1f, a.Nz)))
            {
                a.EnergyRate = Math.Max(a.EnergyRate, 10f);
                r.SpeedBy = ConstraintId.Envelope;
                float ratio = s.Eas / (1.2f * Math.Max(1f, p.StallSpeed));
                float allowed = Math.Max(1f, ratio * ratio);
                if (a.Nz > allowed)
                {
                    r.BindNz(ConstraintId.Envelope, a.Nz, allowed);
                    a.Nz = allowed;
                }
            }

            // Ground-collision avoidance: roll level, pull at available g, full dry power.
            UpdateGcas(s, ctx, p);
            if (gcas)
            {
                r.BindBank(ConstraintId.Gcas, a.BankDeg, 0f);
                r.BindNz(ConstraintId.Gcas, a.Nz, nzMax);
                a.BankDeg = 0f;
                a.Nz = nzMax;
                a.EnergyRate = Math.Max(a.EnergyRate, 50f);
                a.AirbrakeAllowed = false;
                a.Gcas = true;
                r.GcasActive = true;
            }

            // Authority: a command cannot move faster than the aircraft can follow.
            if (primed && dt > 0f)
            {
                float bank = SlewBank(lastBank, a.BankDeg, p.RollRateMaxDps * dt);
                if (Math.Abs(Scalar.Wrap180(bank - a.BankDeg)) > 1e-4f) r.BindBank(ConstraintId.Authority, a.BankDeg, bank);
                a.BankDeg = bank;
                a.Nz = Slew.Step(lastNz, a.Nz, NzSlew, dt);
            }
            lastBank = a.BankDeg;
            lastNz = a.Nz;
            primed = true;
        }

        /// <summary>Seed the authority stage from the aircraft's actual attitude (handover, spawn).</summary>
        public void Track(in AircraftState s)
        {
            lastBank = s.BankDeg;
            lastNz = s.Nz;
            primed = true;
        }

        /// <summary>Move a bank angle toward a target by at most maxStep degrees, the short way round.</summary>
        public static float SlewBank(float current, float target, float maxStep)
        {
            float step = Scalar.Clamp(Scalar.Wrap180(target - current), -maxStep, maxStep);
            return Scalar.Wrap180(current + step);
        }

        private void UpdateGcas(in AircraftState s, in LimitContext ctx, AirframeProfile p)
        {
            if (float.IsNaN(ctx.FloorY))
            {
                gcas = false;
                return;
            }
            float height = s.Pos.Y - ctx.FloorY;
            float sink = Math.Max(0f, -s.Vel.Y);
            float pullAccel = Math.Max(1f, (Math.Min(p.GLimit, p.LiftLimitedG(s.Eas)) - 1f) * Scalar.G);
            float rollTime = Math.Abs(s.BankDeg) / Math.Max(1f, p.RollRateMaxDps);
            float loss = sink * rollTime + sink * sink / (2f * pullAccel);
            float remaining = sink > 0.5f ? (height - loss - 0.5f * ctx.Clearance) / sink : float.PositiveInfinity;
            if (!gcas && remaining < GcasTrigger) gcas = true;
            else if (gcas && (remaining > GcasRelease || s.Vel.Y > 5f)) gcas = false;
        }
    }
}
