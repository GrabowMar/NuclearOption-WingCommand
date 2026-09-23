using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class TrackingGuidanceTests
    {
        private static readonly AirframeProfile Fighter = new AirframeProfile();

        private static FlightIntent Intent(Vec3 refPos, Vec3 refVel, bool afterburner = false, float spacing = 0f) =>
            new FlightIntent
            {
                Ref = new RefState(refPos, refVel, Vec3.Zero),
                Limits = new SpeedLimits(Fighter.MinimumSpeed(1f), Fighter.MaxSpeed, afterburner, true),
                Precision = 1f,
                Spacing = spacing,
            };

        private static AircraftState At(Vec3 pos, Vec3 vel) =>
            new AircraftState
            {
                Pos = pos, Vel = vel, Tas = vel.Length, Qbar = Isa.DynamicPressure(pos.Y, vel.Length), Fwd = vel.Normalized,
            };

        [Fact]
        public void FarBehindSaturatesAtCatchUpSpeedWithoutAfterburner()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, 10000f), new Vec3(0f, 0f, 200f)),
                At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f)), Fighter);
            Assert.Equal(250f, cmd.VelCmd.Z, 1);   // MilSpeed 255 − 200 − 5
        }

        [Fact]
        public void AfterburnerRaisesCatchUpToTheCap()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, 10000f), new Vec3(0f, 0f, 200f), afterburner: true),
                At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f)), Fighter);
            Assert.Equal(280f, cmd.VelCmd.Z, 1);   // cap 80 over reference
        }

        [Fact]
        public void NearTheReferenceOvertakeIsCapped()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, 200f), new Vec3(0f, 0f, 200f), true, spacing: 80f),
                At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f)), Fighter);
            Assert.True(cmd.VelCmd.Z - 200f <= TrackingGuidance.NearOvertakeCap + 1e-3f);
        }

        [Fact]
        public void OnTheReferenceWithMatchedVelocityCommandsItsAcceleration()
        {
            var intent = Intent(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f));
            intent.Ref = new RefState(intent.Ref.Pos, intent.Ref.Vel, new Vec3(3f, 0f, 0f));
            var cmd = TrackingGuidance.Evaluate(intent, At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f)), Fighter);
            Assert.Equal(3f, cmd.Accel.X, 4);
            Assert.Equal(0f, cmd.Accel.Z, 4);
        }

        [Fact]
        public void CrossTrackCorrectionNeverExceedsFortyFiveDegrees()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(50000f, 2000f, 0f), new Vec3(0f, 0f, 200f)),
                At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f)), Fighter);
            float angle = (float)(Math.Atan2(cmd.VelCmd.X, cmd.VelCmd.Z) * 180.0 / Math.PI);
            Assert.InRange(angle, 0f, 45.01f);
        }

        [Fact]
        public void AheadOfTheReferenceSlowsButNeverBelowTheMinimum()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, -3000f), new Vec3(0f, 0f, 120f)),
                At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 120f)), Fighter);
            Assert.True(cmd.VelCmd.Z < 120f);
            Assert.True(cmd.VelCmd.Length >= Fighter.MinimumSpeed(1f) - 1e-3f);
        }

        [Fact]
        public void StoppingLawArrivesWithoutOvershoot()
        {
            // 1D: relative position closed by an aircraft whose speed follows the command with τ_v and
            // whose acceleration is bounded by the braking/thrust capability.
            float gap = 3000f, relVel = 0f, minGap = gap, dt = 1f / 60f;
            for (int i = 0; i < 60 * 180; i++)
            {
                var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, gap), new Vec3(0f, 0f, 200f), true),
                    At(new Vec3(0f, 2000f, 0f), new Vec3(0f, 0f, 200f + relVel)), Fighter);
                float accel = Scalar.Clamp(cmd.Accel.Z, -Fighter.AirbrakeDecel, Fighter.ThrustAccelMax);
                relVel += accel * dt;
                gap -= relVel * dt;
                minGap = Math.Min(minGap, gap);
            }
            Assert.True(minGap > -5f, $"overshot by {-minGap:0.0} m");
            Assert.InRange(gap, -2f, 2f);
        }

        [Fact]
        public void ZeroReferenceVelocityGivesFiniteCommand()
        {
            var cmd = TrackingGuidance.Evaluate(Intent(new Vec3(0f, 2000f, 0f), Vec3.Zero),
                At(new Vec3(0f, 2000f, 0f), Vec3.Zero), Fighter);
            Assert.True(Scalar.IsFinite(cmd.Accel.X) && Scalar.IsFinite(cmd.Accel.Y) && Scalar.IsFinite(cmd.Accel.Z));
        }
    }
}
