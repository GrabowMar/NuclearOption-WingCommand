using System;

namespace WingCommand
{
    /// <summary>Helicopter inner loops (spec M2 §4.3), 60 Hz, Pure signs.
    /// <list type="bullet">
    /// <item>Tilt: the horizontal acceleration in the heading frame gives the disc attitude (pitch = −atan(a_fwd/g),
    /// roll = atan(a_right/g)) plus a slow trim on the acceleration error (drag, hover attitude), within MaxTilt.</item>
    /// <item>Attitude → rate → stick: a P loop to a rate command; the stick is that rate over the authority learned
    /// in flight per axis (<see cref="RateAuthority"/>), since the helo FBW is itself a rate loop.</item>
    /// <item>Yaw: heading error → yaw rate → pedal; no heading target holds the heading (zero rate).</item>
    /// <item>Collective: the guidance's vertical acceleration through the hover model, trim × (g + a_y)/(g·cos tilt),
    /// so the collision bias, the floor and the reference's own acceleration all act. The trim (seeded from
    /// HoverCollective, bounded to ±50% of it) integrates the vertical-speed error, except into a saturated collective;
    /// it keeps integrating while the climb command sits at its limit, since an aircraft that does not respond to it
    /// has a wrong trim (a tiltwing converting from plane mode hands over a low throttle). Slewed, clamped 0..1.</item>
    /// </list></summary>
    internal sealed class RotaryController
    {
        public static float AttitudeGain = 2.5f, RateMaxDps = 60f, YawGain = 1.5f, YawRateMaxDps = 30f;
        public static float TrimTau = 3f, CollectiveKi = 0.05f, CollectiveSlew = 1f, TrimRange = 0.5f;
        public static float AuxNeutral = 0.5f;

        public readonly RateAuthority Pitch = new RateAuthority(), Roll = new RateAuthority(), Yaw = new RateAuthority();
        private float trimF, trimR, integrator = float.NaN, collective = float.NaN;
        private float lastPitch, lastRoll, lastYaw;
        private AirframeProfile seededFrom;

        public float PitchTargetDeg { get; private set; }
        public float RollTargetDeg { get; private set; }

        public ControlOutput Step(in GuidanceCommand g, in AircraftState s, AirframeProfile p, float dt)
        {
            Seed(p);
            if (dt <= 0f) return new ControlOutput { Throttle = collective, Aux = AuxNeutral };
            Pitch.Update(lastPitch, s.Q, dt);
            Roll.Update(lastRoll, s.P, dt);
            Yaw.Update(lastYaw, s.R, dt);

            Vec3 fwd = s.Fwd.Horizontal.SqrLength > 1e-4f ? s.Fwd.Horizontal.Normalized : Vec3.Forward;
            Vec3 right = Vec3.Cross(Vec3.Up, fwd);
            float aF = Vec3.Dot(g.Accel, fwd), aR = Vec3.Dot(g.Accel, right);
            float half = 0.5f * p.MaxTiltDeg;
            float k = dt / TrimTau * Scalar.Rad2Deg / Scalar.G;
            trimF = Scalar.Clamp(trimF + (aF - Vec3.Dot(s.Acc, fwd)) * k, -half, half);
            trimR = Scalar.Clamp(trimR + (aR - Vec3.Dot(s.Acc, right)) * k, -half, half);
            float pitch = -((float)Math.Atan(aF / Scalar.G) * Scalar.Rad2Deg + trimF);
            float roll = (float)Math.Atan(aR / Scalar.G) * Scalar.Rad2Deg + trimR;
            float tilt = (float)Math.Sqrt(pitch * pitch + roll * roll);
            if (tilt > p.MaxTiltDeg)
            {
                pitch *= p.MaxTiltDeg / tilt;
                roll *= p.MaxTiltDeg / tilt;
            }
            PitchTargetDeg = pitch;
            RollTargetDeg = roll;

            float q = Scalar.Clamp(AttitudeGain * (pitch - s.PitchDeg), -RateMaxDps, RateMaxDps);
            float r = Scalar.Clamp(AttitudeGain * (roll - s.BankDeg), -RateMaxDps, RateMaxDps);
            float yawRate = 0f;
            if (g.HasHeading)
                yawRate = Scalar.Clamp(YawGain * Scalar.Wrap180(g.HeadingDeg - Vec3.HeadingDeg(fwd)), -YawRateMaxDps, YawRateMaxDps);

            float error = g.VelCmd.Y - s.Vel.Y;
            float cos = Scalar.Clamp(s.Up.Y, 0.7f, 1f);
            float raw = integrator * (Scalar.G + g.Accel.Y) / (Scalar.G * cos);
            if (!(raw >= 1f && error > 0f) && !(raw <= 0f && error < 0f))
                integrator = Scalar.Clamp(integrator + CollectiveKi * error * dt,
                    (1f - TrimRange) * p.HoverCollective, (1f + TrimRange) * p.HoverCollective);
            collective = Scalar.Clamp01(collective + Scalar.Clamp(raw - collective, -CollectiveSlew * dt, CollectiveSlew * dt));

            var o = new ControlOutput
            {
                Pitch = Scalar.Clamp(q / Pitch.RateDps, -1f, 1f),
                Roll = Scalar.Clamp(r / Roll.RateDps, -1f, 1f),
                Yaw = Scalar.Clamp(yawRate / Yaw.RateDps, -1f, 1f),
                Throttle = collective,
                Aux = AuxNeutral,
            };
            lastPitch = o.Pitch;
            lastRoll = o.Roll;
            lastYaw = o.Yaw;
            return o;
        }

        /// <summary>Bumpless takeover: the collective loop continues from the applied collective, the trims restart.</summary>
        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p)
        {
            Seed(p);
            collective = Scalar.Clamp01(applied.Throttle);
            integrator = Scalar.Clamp(collective * Scalar.Clamp(s.Up.Y, 0.7f, 1f),
                (1f - TrimRange) * p.HoverCollective, (1f + TrimRange) * p.HoverCollective);
            trimF = trimR = 0f;
            lastPitch = applied.Pitch;
            lastRoll = applied.Roll;
            lastYaw = applied.Yaw;
        }

        public void NoteAppliedRoll(float appliedRoll) => lastRoll = appliedRoll;

        /// <summary>A new profile (another airframe) re-seeds the learned authorities; the first use seeds the hover trim.</summary>
        private void Seed(AirframeProfile p)
        {
            if (!ReferenceEquals(p, seededFrom))
            {
                seededFrom = p;
                Pitch.Reset(p.PitchRateMaxDps);
                Roll.Reset(p.RollRateMaxDps);
                Yaw.Reset(p.YawRateMaxDps);
            }
            if (float.IsNaN(integrator)) integrator = p.HoverCollective;
            if (float.IsNaN(collective)) collective = p.HoverCollective;
        }
    }
}
