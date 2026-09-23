using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RotaryControllerTests
    {
        private const float Dt = 1f / 60f;

        private static AirframeProfile Utility() => AirframeProfile.Derive(new ProfileInputs
        {
            UnitName = "UH-90", Class = AirframeClass.Rotary, MaxSpeed = 80f, GLimit = 3f, MaxRadius = 9f,
        });

        private static AircraftState Hover(Vec3 vel = default) => new AircraftState
        {
            Pos = new Vec3(0f, 300f, 0f), Vel = vel, Fwd = Vec3.Forward, Up = Vec3.Up, Right = new Vec3(1f, 0f, 0f),
            Tas = vel.Length, RadarAlt = 300f, FbwActive = true,
        };

        private static ControlOutput Run(RotaryController c, in GuidanceCommand g, in AircraftState s, AirframeProfile p,
            float seconds)
        {
            ControlOutput o = default;
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) o = c.Step(g, s, p, Dt);
            return o;
        }

        [Fact]
        public void HoverFeedforwardHoldsCollectiveAtTrim()
        {
            AirframeProfile p = Utility();
            ControlOutput o = Run(new RotaryController(), default, Hover(), p, 2f);
            Assert.Equal(p.HoverCollective, o.Throttle, 3);
            Assert.Equal(0f, o.Pitch, 3);
            Assert.Equal(0f, o.Roll, 3);
            Assert.Equal(0.5f, o.Aux);
        }

        [Fact]
        public void ForwardAccelerationTiltsTheNoseDownAndSidewaysRollsThatWay()
        {
            AirframeProfile p = Utility();
            ControlOutput forward = new RotaryController().Step(new GuidanceCommand { Accel = new Vec3(0f, 0f, 3f) }, Hover(), p, Dt);
            Assert.True(forward.Pitch < -0.05f && Math.Abs(forward.Roll) < 1e-3f, $"pitch {forward.Pitch}, roll {forward.Roll}");
            ControlOutput right = new RotaryController().Step(new GuidanceCommand { Accel = new Vec3(3f, 0f, 0f) }, Hover(), p, Dt);
            Assert.True(right.Roll > 0.05f && Math.Abs(right.Pitch) < 1e-3f, $"pitch {right.Pitch}, roll {right.Roll}");
        }

        [Fact]
        public void TiltTargetStaysWithinTheTiltLimit()
        {
            AirframeProfile p = Utility();
            var c = new RotaryController();
            c.Step(new GuidanceCommand { Accel = new Vec3(40f, 0f, 40f) }, Hover(), p, Dt);
            float tilt = (float)Math.Sqrt(c.PitchTargetDeg * c.PitchTargetDeg + c.RollTargetDeg * c.RollTargetDeg);
            Assert.True(tilt <= p.MaxTiltDeg + 1e-3f, $"tilt target {tilt:0.0}");
        }

        [Fact]
        public void CollectiveDeliversTheCommandedVerticalAcceleration()
        {
            // Model-based: hover collective × (g + a_y)/g; with thrust-to-weight 1/hover that is exactly a_y.
            AirframeProfile p = Utility();
            ControlOutput o = Run(new RotaryController(), new GuidanceCommand { Accel = new Vec3(0f, 2f, 0f) }, Hover(), p, 2f);
            Assert.Equal(p.HoverCollective * (Scalar.G + 2f) / Scalar.G, o.Throttle, 2);
        }

        [Fact]
        public void TrimRecoversFromATooLowSeedWhileTheClimbCommandIsAtItsLimit()
        {
            // A tiltwing converting from plane mode hands over a low throttle: sinking at 10 m/s, the climb command sits
            // at its limit, and the trim must still rise (freezing it there kept the aircraft sinking: 1475 m → 112 m, T1).
            AirframeProfile p = Utility();
            var c = new RotaryController();
            c.Track(Hover(new Vec3(0f, -10f, 0f)), new ControlOutput { Throttle = 0.1f }, p);
            var recover = new GuidanceCommand { VelCmd = Vec3.Zero, Accel = new Vec3(0f, p.VerticalAccelMax, 0f) };
            ControlOutput o = Run(c, recover, Hover(new Vec3(0f, -10f, 0f)), p, 3f);
            Assert.True(o.Throttle >= p.HoverCollective, $"collective {o.Throttle:0.00} after 3 s of sinking");
        }

        [Fact]
        public void TrimStaysWithinItsRangeWhenTheAircraftDoesNotRespond()
        {
            AirframeProfile p = Utility();
            var c = new RotaryController();
            var climb = new GuidanceCommand { VelCmd = new Vec3(0f, 50f, 0f), Accel = new Vec3(0f, p.VerticalAccelMax, 0f) };
            Run(c, climb, Hover(), p, 60f);
            ControlOutput back = Run(c, default, Hover(), p, 2f);
            Assert.True(back.Throttle <= (1f + RotaryController.TrimRange) * p.HoverCollective + 1e-3f,
                $"collective {back.Throttle:0.00} once the command ended");
        }

        [Fact]
        public void AVerticalCollisionBiasMovesTheCollective()
        {
            // Two helicopters stacked vertically: the bias is almost purely vertical and must act.
            AirframeProfile p = Utility();
            var pipeline = new RotaryPipeline();
            var ctx = new LimitContext { FloorY = float.NaN, Clearance = 60f, CollisionBias = new Vec3(0f, 5f, 0f) };
            ControlOutput o = default;
            for (int i = 0; i < 60; i++) o = pipeline.Step(default, Hover(), ctx, p, Dt);
            Assert.True(o.Throttle > p.HoverCollective + 0.1f, $"collective {o.Throttle:0.00}");
        }

        [Fact]
        public void YawTurnsTowardTheHeadingTarget()
        {
            ControlOutput o = new RotaryController().Step(new GuidanceCommand { HasHeading = true, HeadingDeg = 90f }, Hover(), Utility(), Dt);
            Assert.True(o.Yaw > 0.05f, $"yaw {o.Yaw}");
        }

        [Fact]
        public void HoldsHeadingWhenNoTarget()
        {
            ControlOutput o = new RotaryController().Step(default, Hover(), Utility(), Dt);
            Assert.Equal(0f, o.Yaw);
        }

        [Fact]
        public void ZeroDtIsNeutral()
        {
            ControlOutput o = new RotaryController().Step(new GuidanceCommand { Accel = new Vec3(5f, 0f, 5f) }, Hover(), Utility(), 0f);
            Assert.Equal(0f, o.Pitch);
            Assert.Equal(0f, o.Roll);
            Assert.Equal(0f, o.Yaw);
            Assert.False(float.IsNaN(o.Throttle));
        }

        [Fact]
        public void FloorRaisesTheClimbCommand()
        {
            AirframeProfile p = Utility();
            var pipeline = new RotaryPipeline();
            var ctx = new LimitContext { FloorY = 280f, Clearance = 60f };   // 40 m under the floor + clearance
            ControlOutput o = default;
            for (int i = 0; i < 60; i++) o = pipeline.Step(default, Hover(), ctx, p, Dt);
            Assert.True(o.Throttle > p.HoverCollective + 0.02f, $"collective {o.Throttle:0.00}");
            Assert.Equal(ConstraintId.Terrain, pipeline.Report.VerticalBy);
        }

        [Fact]
        public void FlightStackGivesRotaryAircraftTheRotaryPipeline()
        {
            Assert.IsType<RotaryPipeline>(FlightStack.NewPipeline(AirframeClass.Rotary));
            Assert.IsType<TiltwingPipeline>(FlightStack.NewPipeline(AirframeClass.Tiltwing));
        }
    }
}
