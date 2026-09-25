using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class TiltwingPipelineTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>A VT-7-like tiltwing: 45 m/s plane-mode stall, so conversion between 49.5 and 63 m/s.</summary>
        private static AirframeProfile Tiltwing() => AirframeProfile.Derive(new ProfileInputs
        {
            UnitName = "tiltwing", Class = AirframeClass.Tiltwing, PublishedStallKmh = 45f * 3.6f, MaxSpeed = 150f, GLimit = 4f,
        });

        private static AircraftState At(float speed) =>
            TestStates.Flying(new Vec3(0f, 500f, 0f), new Vec3(0f, 0f, Math.Max(speed, 0.001f)));

        private static void Run(TiltwingPipeline t, float speed, float seconds, AirframeProfile p, float throttle = 0.5f)
        {
            var ctx = new LimitContext { FloorY = float.NaN, Clearance = 60f };
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++)
                t.Step(new GuidanceCommand { VelCmd = new Vec3(0f, 0f, speed) }, At(speed), ctx, p, Dt);
        }

        [Fact]
        public void ConversionSpeedsComeFromThePlaneModeStall()
        {
            AirframeProfile p = Tiltwing();
            Assert.Equal(49.5f, p.ConversionLow, 2);
            Assert.Equal(63f, p.ConversionHigh, 2);
            Assert.Equal(0f, p.MinimumSpeed(1f));   // it can hover: no loaded minimum for roles
        }

        [Fact]
        public void ModeFollowsTheAirspeedWithHysteresis()
        {
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 80f, 1f, p);
            Assert.Equal(TiltwingMode.Plane, t.Mode);
            Run(t, 55f, 10f, p);                        // inside the band: stays
            Assert.Equal(TiltwingMode.Plane, t.Mode);
            Run(t, 45f, 4f, p);
            Assert.Equal(TiltwingMode.Rotary, t.Mode);
            Run(t, 55f, 10f, p);
            Assert.Equal(TiltwingMode.Rotary, t.Mode);
            Run(t, 70f, 4f, p);
            Assert.Equal(TiltwingMode.Plane, t.Mode);
        }

        [Fact]
        public void AModeIsHeldForTheMinimumDwell()
        {
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 80f, 1f, p);
            Run(t, 30f, 1f, p);                         // the initial mode is a guess: no dwell before the first switch
            Assert.Equal(TiltwingMode.Rotary, t.Mode);
            Run(t, 80f, TiltwingPipeline.MinDwell - 1.5f, p);   // 1 s at 30 m/s + 1.5 s: still inside the dwell
            Assert.Equal(TiltwingMode.Rotary, t.Mode);
            Run(t, 80f, 1f, p);
            Assert.Equal(TiltwingMode.Plane, t.Mode);
            Assert.Equal(2, t.Conversions);   // the initial mode is not a conversion
        }

        [Fact]
        public void GuidanceIsTheActiveModesLaw()
        {
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            var intent = new FlightIntent
            {
                Ref = new RefState(new Vec3(0f, 500f, 30f), Vec3.Zero, Vec3.Zero), Limits = new SpeedLimits(0f, 150f, false, true),
                Precision = 1f,
            };
            Run(t, 20f, 1f, p);
            GuidanceCommand rotary = t.Guide(intent, At(20f), p);
            GuidanceCommand expected = RotaryGuidance.Evaluate(intent, At(20f), p);
            Assert.Equal(expected.VelCmd, rotary.VelCmd);
        }

        private static FlightIntent Toward(Vec3 refPos, Vec3 refVel) => new FlightIntent
        {
            Ref = new RefState(refPos, refVel, Vec3.Zero), Limits = new SpeedLimits(0f, 150f, false, true), Precision = 1f,
        };

        [Fact]
        public void RotaryModeDoesNotOutrunTheConversionToChaseASlowReference()
        {
            // Overshot 3 km past a slow leader: flying back at plane cruise would convert it to plane mode and back (T1).
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 20f, 1f, p);
            GuidanceCommand g = t.Guide(Toward(new Vec3(0f, 500f, -3000f), new Vec3(0f, 0f, 20f)), At(20f), p);
            Assert.True(g.VelCmd.Horizontal.Length < p.ConversionHigh, $"{g.VelCmd.Horizontal.Length:0} m/s");
        }

        [Fact]
        public void RotaryModeStillFollowsAFastReferenceIntoPlaneMode()
        {
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 20f, 1f, p);
            GuidanceCommand g = t.Guide(Toward(new Vec3(0f, 500f, 100f), new Vec3(0f, 0f, 120f)), At(20f), p);
            Assert.True(g.VelCmd.Horizontal.Length >= 120f, $"{g.VelCmd.Horizontal.Length:0} m/s");
        }

        [Fact]
        public void RotaryModeStaysBelowTheConversionForAReferenceInsideTheBand()
        {
            // Review M2b I4: with the reference at 58 m/s (inside 49.5-63) the rotary cap was 68 m/s, so the tiltwing
            // ran into plane mode and out again on every S-turn.
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 30f, 1f, p);
            GuidanceCommand g = t.Guide(Toward(new Vec3(0f, 500f, 500f), new Vec3(0f, 0f, 58f)), At(30f), p);
            Assert.True(g.VelCmd.Horizontal.Length < p.ConversionHigh, $"{g.VelCmd.Horizontal.Length:0} m/s");
        }

        [Fact]
        public void PlaneModeStaysAboveTheConversionForAReferenceInsideTheBand()
        {
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            Run(t, 80f, 1f, p);
            // Ahead of a reference at 52 m/s: the plane-mode speed loop would slow it below 49.5 m/s and convert it.
            GuidanceCommand g = t.Guide(Toward(new Vec3(0f, 500f, -300f), new Vec3(0f, 0f, 52f)), At(60f), p);
            Assert.True(g.VelCmd.Length >= TiltwingPipeline.PlaneFloorFactor * p.ConversionLow - 0.01f, $"{g.VelCmd.Length:0} m/s");
        }

        [Fact]
        public void TheIncomingPipelineTakesOverFromTheAppliedOutput()
        {
            // Plane mode slows through the band: the rotary pipeline's first collective continues from the throttle the
            // plane pipeline last produced (slewed), not from its hover trim.
            AirframeProfile p = Tiltwing();
            var t = new TiltwingPipeline();
            var ctx = new LimitContext { FloorY = float.NaN, Clearance = 60f };
            Run(t, 80f, 1f, p);
            ControlOutput last = default;
            for (int i = 0; i < 600 && t.Mode == TiltwingMode.Plane; i++)
                last = t.Step(new GuidanceCommand { VelCmd = new Vec3(0f, 0f, 60f) }, At(40f), ctx, p, Dt);
            Assert.Equal(TiltwingMode.Rotary, t.Mode);
            ControlOutput first = t.Step(new GuidanceCommand { VelCmd = new Vec3(0f, 0f, 40f) }, At(40f), ctx, p, Dt);
            Assert.True(Math.Abs(first.Throttle - last.Throttle) <= RotaryController.CollectiveSlew * Dt + 1e-4f,
                $"collective {first.Throttle:0.000} after the switch, {last.Throttle:0.000} before");
        }
    }
}
