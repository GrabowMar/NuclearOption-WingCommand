using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RotaryGuidanceTests
    {
        private static AirframeProfile Utility() => AirframeProfile.Derive(new ProfileInputs
        {
            UnitName = "UH-90", Class = AirframeClass.Rotary, MaxSpeed = 80f, GLimit = 3f, MaxRadius = 9f,
        });

        private static AircraftState Helo(Vec3 pos, Vec3 vel) => new AircraftState
        {
            Pos = pos, Vel = vel, Fwd = Vec3.Forward, Up = Vec3.Up, Right = new Vec3(1f, 0f, 0f), Tas = vel.Length,
        };

        private static FlightIntent At(Vec3 pos, Vec3 vel) => new FlightIntent
        {
            Ref = new RefState(pos, vel, Vec3.Zero), Limits = new SpeedLimits(0f, 80f, false, true), Precision = 1f,
        };

        private static float TiltAccel(AirframeProfile p) => Scalar.G * (float)Math.Tan(p.MaxTiltDeg * Scalar.Deg2Rad);

        [Fact]
        public void RotaryDeriveSetsRotaryDefaults()
        {
            AirframeProfile p = Utility();
            Assert.Equal(64f, p.CruiseSpeed, 3);
            Assert.Equal(30f, p.MaxTiltDeg);
            Assert.Equal(0.5f, p.HoverCollective);
            Assert.True(p.ClimbRateMax <= 10f, $"climb rate {p.ClimbRateMax}");
            Assert.False(p.HasAfterburner);
        }

        [Fact]
        public void RotaryDeriveSeedsTheRateAuthoritiesAndHoverTrimFromTheGame()
        {
            AirframeProfile p = AirframeProfile.Derive(new ProfileInputs
            {
                UnitName = "helo", Class = AirframeClass.Rotary, MaxSpeed = 80f,
                HeloPitchRate = 0.8f, HeloYawRate = 1.5f, HeloRollRate = 1.8f, HeloGLimit = 2.5f, HoverCollective = 0.42f,
            });
            Assert.Equal(0.8f * Scalar.Rad2Deg, p.PitchRateMaxDps, 2);
            Assert.Equal(1.5f * Scalar.Rad2Deg, p.YawRateMaxDps, 2);
            Assert.Equal(1.8f * Scalar.Rad2Deg, p.RollRateMaxDps, 2);
            Assert.Equal(2.5f, p.GLimit, 3);
            Assert.Equal(0.42f, p.HoverCollective, 3);
        }

        [Fact]
        public void NoMinimumSpeedForAReferenceAtRest()
        {
            var at = new Vec3(0f, 300f, 0f);
            GuidanceCommand g = RotaryGuidance.Evaluate(At(at, Vec3.Zero), Helo(at, Vec3.Zero), Utility());
            Assert.True(g.VelCmd.Length < 1e-3f, $"velocity command {g.VelCmd.Length}");
            Assert.True(g.Accel.Length < 1e-3f, $"acceleration {g.Accel.Length}");
        }

        [Fact]
        public void StopsAtAHoveringReferenceWithoutOvershootSpeed()
        {
            // 20 m short of a hovering reference at 30 m/s: the stopping law allows √(2·0.6·g·tan(MaxTilt)·20)
            // (≈ 11.7 m/s at 30°), so the helo brakes at the tilt limit.
            AirframeProfile p = Utility();
            GuidanceCommand g = RotaryGuidance.Evaluate(At(new Vec3(0f, 300f, 20f), Vec3.Zero),
                Helo(new Vec3(0f, 300f, 0f), new Vec3(0f, 0f, 30f)), p);
            float allowed = (float)Math.Sqrt(2f * RotaryGuidance.BrakeFraction * TiltAccel(p) * 20f);
            Assert.True(g.VelCmd.Z <= allowed + 0.01f && g.VelCmd.Z > 0f, $"closing command {g.VelCmd.Z} (allowed {allowed:0.0})");
            Assert.Equal(-TiltAccel(p), g.Accel.Z, 2);
        }

        [Fact]
        public void HorizontalAccelerationIsTiltLimited()
        {
            AirframeProfile p = Utility();
            GuidanceCommand g = RotaryGuidance.Evaluate(At(new Vec3(3000f, 300f, 0f), Vec3.Zero),
                Helo(new Vec3(0f, 300f, 0f), Vec3.Zero), p);
            float horizontal = new Vec3(g.Accel.X, 0f, g.Accel.Z).Length;
            Assert.True(horizontal <= TiltAccel(p) + 1e-3f, $"horizontal acceleration {horizontal}");
            Assert.True(g.VelCmd.Length <= p.CruiseSpeed + 1e-3f, $"velocity command {g.VelCmd.Length}");
        }

        [Fact]
        public void ReferenceBehindFliesBackwardsNotAround()
        {
            GuidanceCommand g = RotaryGuidance.Evaluate(At(new Vec3(0f, 300f, -50f), Vec3.Zero),
                Helo(new Vec3(0f, 300f, 0f), Vec3.Zero), Utility());
            Assert.True(g.VelCmd.Z < 0f && Math.Abs(g.VelCmd.X) < 1e-3f, $"velocity command {g.VelCmd}");
            Assert.False(g.HasHeading);
        }

        [Fact]
        public void HeadingFollowsTheReferenceWhenSlowAndTheCommandWhenFast()
        {
            var at = new Vec3(0f, 300f, 0f);
            GuidanceCommand slow = RotaryGuidance.Evaluate(At(at, new Vec3(5f, 0f, 0f)), Helo(at, new Vec3(5f, 0f, 0f)), Utility());
            Assert.True(slow.HasHeading);
            Assert.Equal(90f, slow.HeadingDeg, 1);
            GuidanceCommand fast = RotaryGuidance.Evaluate(At(new Vec3(0f, 300f, 500f), new Vec3(0f, 0f, 40f)),
                Helo(at, new Vec3(0f, 0f, 40f)), Utility());
            Assert.True(fast.HasHeading);
            Assert.Equal(0f, fast.HeadingDeg, 1);
        }

        [Fact]
        public void SlowHelicopterFacesTheIntentsHeading()
        {
            // A hovering element faces its leader's way (spec M2 §4.2), even after sidestepping into its slot.
            var at = new Vec3(0f, 300f, 0f);
            FlightIntent intent = At(at, new Vec3(3f, 0f, 0f));
            intent.HasHeading = true;
            intent.HeadingDeg = 0f;
            GuidanceCommand g = RotaryGuidance.Evaluate(intent, Helo(at, new Vec3(3f, 0f, 0f)), Utility());
            Assert.True(g.HasHeading);
            Assert.Equal(0f, g.HeadingDeg, 1);
        }

        [Fact]
        public void ClimbIsCappedByTheClimbRateAndTheVerticalAcceleration()
        {
            AirframeProfile p = Utility();
            GuidanceCommand g = RotaryGuidance.Evaluate(At(new Vec3(0f, 1300f, 0f), Vec3.Zero),
                Helo(new Vec3(0f, 300f, 0f), Vec3.Zero), p);
            Assert.Equal(p.ClimbRateMax, g.VelCmd.Y, 3);
            Assert.Equal(p.VerticalAccelMax, g.Accel.Y, 3);
        }
    }
}
