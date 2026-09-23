using System;

namespace WingCommand
{
    /// <summary>One ordered chain applied to every command, recording what bound it.
    /// <para>Acceleration stage: collision bias, then speed priority (no climbing away a low speed), then terrain floor.</para>
    /// <para>Attitude stage: envelope (bank ceiling, lift- and structure-limited load factor, loaded
    /// minimum speed), then ground-collision avoidance, then authority slews that keep commands continuous.</para>
    /// Holds per-aircraft state (GCAS latch, last command), so there is one instance per aircraft.</summary>
    internal sealed class ConstraintChain
    {
        public static float GcasTrigger = 1.5f, GcasRelease = 3f;
        public static float BankFloorDeg = 60f, BankRangeDeg = 25f;
        public static float NzSlew = 3f;
        public static float SpeedProtectFactor = 1.5f, RecoverSinkGain = 1f, RecoverSinkMax = 10f;

        private bool gcas;
        private float lastBank, lastNz;
        private bool primed;

        public bool GcasActive => gcas;

        public void ApplyAccel(ref GuidanceCommand c, in AircraftState s, in LimitContext ctx, AirframeProfile p,
            ref BindingReport r)
        {
            if (ctx.CollisionBias.SqrLength > 1e-4f)
            {
                c.Accel += ctx.CollisionBias;
                r.CollisionActive = true;
            }

            // Speed priority: a slow aircraft may not climb away its speed (at full throttle the climb would bleed
            // it toward the stall). The terrain floor below still wins.
            float maxVy = SpeedLimitedClimb(s, p);
            if (c.VelCmd.Y > maxVy)
            {
                float cut = maxVy - c.VelCmd.Y;
                c.VelCmd = new Vec3(c.VelCmd.X, maxVy, c.VelCmd.Z);
                c.Accel += Vec3.Up * (cut / Math.Max(0.1f, p.TauVel));
                r.SpeedBy = ConstraintId.Envelope;
            }

            if (float.IsNaN(ctx.FloorY)) return;
            // Keep the commanded vertical speed above what the height margin allows: climb back when
            // below the floor + clearance, otherwise sink no faster than GCAS's own recovery model accepts
            // with its release time to spare, so riding the floor never trips GCAS.
            float margin = s.Pos.Y - ctx.FloorY - ctx.Clearance;
            float minVy = margin < 0f
                ? Math.Min(p.ClimbRateMax, -margin / 3f)
                : -AllowedSink(margin, s, p);
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
            float nzCap = nzMax;
            if (s.Eas < p.MinimumSpeed(Math.Max(1f, a.Nz)))
            {
                a.EnergyRate = Math.Max(a.EnergyRate, 10f);
                r.SpeedBy = ConstraintId.Envelope;
                float ratio = s.Eas / (1.2f * Math.Max(1f, p.StallSpeed));
                nzCap = Math.Min(nzCap, Math.Max(1f, ratio * ratio));
            }
            if (a.Nz > nzCap)
            {
                // Vertical priority: keep the vertical lift the command asked for and give up bank (turn rate);
                // holding the bank at the capped load factor would lose height (a descending spiral).
                float requested = a.BankDeg;
                float vertical = Math.Max(0f, a.Nz * (float)Math.Cos(requested * Scalar.Deg2Rad));
                float bank = vertical >= nzCap ? 0f : (float)Math.Acos(vertical / nzCap) * Scalar.Rad2Deg;
                if (bank < Math.Abs(requested))
                {
                    a.BankDeg = Math.Sign(requested) * bank;
                    r.BindBank(ConstraintId.Envelope, requested, a.BankDeg);
                }
                r.BindNz(ConstraintId.Envelope, a.Nz, nzCap);
                a.Nz = nzCap;
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

        /// <summary>Largest vertical speed a slow aircraft may command: unlimited from
        /// <see cref="SpeedProtectFactor"/>·Vmin(1) up, growing as the square of the margin between Vmin(1) and
        /// there, and below Vmin(1) a descent of <see cref="RecoverSinkGain"/> m/s per m/s short (at most
        /// <see cref="RecoverSinkMax"/>) to win the speed back.</summary>
        public static float SpeedLimitedClimb(in AircraftState s, AirframeProfile p)
        {
            float vmin = p.MinimumSpeed(1f), high = SpeedProtectFactor * vmin;
            if (s.Eas >= high) return float.PositiveInfinity;
            if (s.Eas >= vmin)
            {
                float f = (s.Eas - vmin) / Math.Max(1f, high - vmin);
                return p.ClimbRateMax * f * f;
            }
            return -Math.Min(RecoverSinkMax, RecoverSinkGain * (vmin - s.Eas));
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

        /// <summary>Recovery model shared by the floor law and GCAS: roll wings level at the roll-rate limit,
        /// ramp the load factor at <see cref="NzSlew"/> (half the ramp counts as dead time), then pull at the
        /// available g. Returns the dead time; <paramref name="pull"/> is the vertical deceleration.</summary>
        private static float RecoveryDelay(in AircraftState s, AirframeProfile p, out float pull)
        {
            float available = Math.Min(p.GLimit, p.LiftLimitedG(s.Eas));
            pull = Math.Max(1f, (available - 1f) * Scalar.G);
            return Math.Abs(s.BankDeg) / Math.Max(1f, p.RollRateMaxDps) + 0.5f * Math.Max(0f, available - 1f) / NzSlew;
        }

        /// <summary>Largest sink whose recovery loss plus <see cref="GcasRelease"/> seconds of sinking fits in
        /// the margin: s²/(2·pull) + s·(delay + release) = margin.</summary>
        private static float AllowedSink(float margin, in AircraftState s, AirframeProfile p)
        {
            float b = RecoveryDelay(s, p, out float pull) + GcasRelease;
            return pull * ((float)Math.Sqrt(b * b + 2f * margin / pull) - b);
        }

        private void UpdateGcas(in AircraftState s, in LimitContext ctx, AirframeProfile p)
        {
            float floor = ctx.HasNearFloor ? ctx.NearFloorY : ctx.FloorY;
            if (float.IsNaN(floor))
            {
                gcas = false;
                return;
            }
            float height = s.Pos.Y - floor;
            float sink = Math.Max(0f, -s.Vel.Y);
            float delay = RecoveryDelay(s, p, out float pull);
            float loss = sink * delay + sink * sink / (2f * pull);
            float remaining = sink > 0.5f ? (height - loss - 0.5f * ctx.Clearance) / sink : float.PositiveInfinity;
            if (!gcas && remaining < GcasTrigger) gcas = true;
            else if (gcas && (remaining > GcasRelease || s.Vel.Y > 5f)) gcas = false;
        }
    }
}
