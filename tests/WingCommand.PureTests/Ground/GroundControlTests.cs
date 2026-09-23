using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class GroundControlTests
    {
        private const float Dt = 1f / 60f;

        private static AircraftState OnGround(Vec3 pos, Vec3 fwd, float speed) => new AircraftState
        {
            Pos = pos, Vel = fwd * speed, Fwd = fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, fwd), Tas = speed, RadarAlt = 0f,
        };

        private static AirframeProfile Jet() => AirframeProfile.Derive(new ProfileInputs
        {
            Class = AirframeClass.FixedWing, PublishedStallKmh = 270f, SteerLockDeg = 45f, WheelbaseM = 6.6f, SpanM = 11f,
        });

        [Fact]
        public void GroundGeometryHasDefaultsAndTakesTheAirframesOwn()
        {
            AirframeProfile generic = AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 270f });
            Assert.Equal(45f, generic.SteerLockDeg);
            Assert.Equal(6f, generic.WheelbaseM);
            Assert.Equal(12f, generic.SpanM);
            Assert.Equal(1.2f * generic.StallSpeed, generic.TakeoffSpeed, 3);
            AirframeProfile read = AirframeProfile.Derive(new ProfileInputs
            {
                PublishedStallKmh = 270f, SteerLockDeg = -45f, SteerRateDps = 45f, WheelbaseM = 1.2f, SpanM = 10f, TakeoffSpeed = 70f,
            });
            Assert.Equal(-45f, read.SteerLockDeg);
            Assert.Equal(45f, read.SteerRateDps);
            Assert.Equal(AirframeProfile.WheelbaseMin, read.WheelbaseM);
            Assert.Equal(10f, read.SpanM);
            Assert.Equal(70f, read.TakeoffSpeed);
        }

        [Fact]
        public void AStraightPathGivesNoYawAndThrottleFromRest()
        {
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, 200f) };
            int progress = 0;
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, OnGround(Vec3.Zero, Vec3.Forward, 0f), 200f);
            Assert.Equal(0f, c.Curvature, 4);
            Assert.Equal(GroundGuidance.TaxiSpeed, c.Speed, 3);
            ControlOutput o = new GroundController().Step(c, OnGround(Vec3.Zero, Vec3.Forward, 0f), Jet(), Dt);
            Assert.Equal(0f, o.Yaw, 4);
            Assert.True(o.Throttle > 0.1f && o.Brake == 0f, $"throttle {o.Throttle}, brake {o.Brake}");
        }

        [Fact]
        public void ARightCornerCommandsRightYawAndSlowsBeforeIt()
        {
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, 60f), new Vec3(60f, 0f, 60f) };
            int progress = 0;
            // Braking from 12 m/s to the ~4 m/s corner speed at 1.5 m/s² takes ~42 m: 25 m out it must already slow.
            GroundCommand far = GroundGuidance.Pursue(path, ref progress, OnGround(new Vec3(0f, 0f, 35f), Vec3.Forward, 12f), 500f);
            Assert.True(far.Speed < GroundGuidance.TaxiSpeed - 1f, $"speed {far.Speed:0.0} 25 m before a right angle");
            GroundCommand near = GroundGuidance.Pursue(path, ref progress, OnGround(new Vec3(0f, 0f, 55f), Vec3.Forward, 4f), 500f);
            Assert.True(near.Curvature > 0f, $"curvature {near.Curvature}");
            ControlOutput o = new GroundController().Step(near, OnGround(new Vec3(0f, 0f, 55f), Vec3.Forward, 4f), Jet(), Dt);
            Assert.True(o.Yaw > 0.05f, $"yaw {o.Yaw}");
        }

        [Fact]
        public void ANegativeSteeringLockReversesTheYaw()
        {
            var c = new GroundCommand { Curvature = 0.05f, Speed = 5f };
            AirframeProfile reversed = AirframeProfile.Derive(new ProfileInputs { PublishedStallKmh = 270f, SteerLockDeg = -45f });
            ControlOutput o = new GroundController().Step(c, OnGround(Vec3.Zero, Vec3.Forward, 5f), reversed, Dt);
            Assert.True(o.Yaw < 0f, $"yaw {o.Yaw}");
        }

        [Fact]
        public void APathThatStartsBehindTheNoseTurnsAroundSlowly()
        {
            // A reroute that doubles back: pure pursuit alone sees no lateral offset and would drive straight on.
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, -200f) };
            int progress = 0;
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, OnGround(Vec3.Zero, Vec3.Forward, 8f), 500f);
            Assert.True(Math.Abs(c.Curvature) >= 1f / GroundGuidance.MinTurnRadius - 1e-4f, $"curvature {c.Curvature}");
            Assert.True(c.Speed <= (float)Math.Sqrt(GroundGuidance.TurnAccel * GroundGuidance.MinTurnRadius) + 1e-3f, $"speed {c.Speed}");
        }

        [Fact]
        public void TheProgressAlongThePathNeverGoesBack()
        {
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, 50f), new Vec3(0f, 0f, 100f) };
            int progress = 0;
            GroundGuidance.Pursue(path, ref progress, OnGround(new Vec3(0f, 0f, 70f), Vec3.Forward, 5f), 500f);
            Assert.Equal(1, progress);
            GroundGuidance.Pursue(path, ref progress, OnGround(new Vec3(0f, 0f, 20f), Vec3.Forward, 5f), 500f);
            Assert.Equal(1, progress);
        }

        [Fact]
        public void AStopPointAheadBrakesToAStop()
        {
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, 200f) };
            int progress = 0;
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, OnGround(Vec3.Zero, Vec3.Forward, 5f), 0.5f);
            Assert.True(c.Stop);
            ControlOutput o = new GroundController().Step(c, OnGround(Vec3.Zero, Vec3.Forward, 5f), Jet(), Dt);
            Assert.Equal(1f, o.Brake);
            Assert.Equal(0f, o.Throttle);
            GroundCommand slowing = GroundGuidance.Pursue(path, ref progress, OnGround(Vec3.Zero, Vec3.Forward, 10f), 8f);
            Assert.True(slowing.Speed <= (float)Math.Sqrt(2f * GroundGuidance.BrakeDecel * 8f) + 1e-3f);
        }

        [Fact]
        public void OverspeedBrakesWithTheThrottleClosed()
        {
            ControlOutput o = new GroundController().Step(new GroundCommand { Speed = 5f }, OnGround(Vec3.Zero, Vec3.Forward, 10f), Jet(), Dt);
            Assert.True(o.Brake > 0f && o.Throttle == 0f, $"brake {o.Brake}, throttle {o.Throttle}");
        }

        [Fact]
        public void TheEngineWriterPassesTheBrake() =>
            Assert.Equal(0.7f, EngineSticks.FromPure(new ControlOutput { Brake = 0.7f, Throttle = 0.2f }).Brake);
    }
}
