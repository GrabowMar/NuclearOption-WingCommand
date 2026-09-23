using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class RotaryPlantTests
    {
        private const float Dt = 1f / 60f;

        private static RotaryPlant Hovering() =>
            new RotaryPlant(RotaryParams.Utility, new Vec3(0f, 300f, 0f), Vec3.Zero, 0f);

        [Fact]
        public void HalfCollectiveHoversInPlace()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 600; i++) h.Step(new ControlOutput { Throttle = 0.5f }, Dt);
            Assert.True(h.Velocity.Length < 0.1f, $"drift {h.Velocity.Length:0.00} m/s");
            Assert.True(Math.Abs(h.Position.Y - 300f) < 0.5f, $"height {h.Position.Y:0.0}");
        }

        [Fact]
        public void TwentyDegreesNoseDownReachesAboutSeventyMetresPerSecond()
        {
            RotaryPlant h = Hovering();
            h.SetAttitude(-20f, 0f);
            float level = 0.5f / (float)Math.Cos(20f * Math.PI / 180.0);
            for (int i = 0; i < 90 * 60; i++) h.Step(new ControlOutput { Throttle = level }, Dt);
            float forward = h.Velocity.Z;
            Assert.InRange(forward, 60f, 80f);
        }

        [Fact]
        public void RollRateFollowsTheStickThroughTheLag()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 60; i++) h.Step(new ControlOutput { Roll = 0.25f, Throttle = 0.5f }, Dt);
            Assert.Equal(0.25f * 2f * 180f / (float)Math.PI, h.Read(Dt).P, 0);
            Assert.True(h.Right.Y < 0f && h.Up.X > 0f, "rolled right: right wing down, disc tilted right");
        }

        [Fact]
        public void NoseDownPitchTiltsTheDiscForward()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 30; i++) h.Step(new ControlOutput { Pitch = -0.3f, Throttle = 0.5f }, Dt);
            Assert.True(h.PitchDeg < 0f && h.Up.Z > 0f, $"pitch {h.PitchDeg:0.0}, up {h.Up}");
        }
    }
}
