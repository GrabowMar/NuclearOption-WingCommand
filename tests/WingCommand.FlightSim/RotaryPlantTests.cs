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
        public void HoverCollectiveHoversInPlace()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 600; i++) h.Step(new ControlOutput { Throttle = h.HoverCollective }, Dt);
            Assert.True(h.Velocity.Length < 0.1f, $"drift {h.Velocity.Length:0.00} m/s");
            Assert.True(Math.Abs(h.Position.Y - 300f) < 0.5f, $"height {h.Position.Y:0.0}");
            Assert.Equal(1f, h.Rpm, 3);
        }

        [Fact]
        public void FullCollectiveDroopsTheRotorWhichRecoversAtHoverCollective()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 5 * 60; i++) h.Step(new ControlOutput { Throttle = 1f }, Dt);
            Assert.True(h.Rpm < 0.95f, $"rpm {h.Rpm:0.000} after 5 s at full collective");
            for (int i = 0; i < 10 * 60; i++) h.Step(new ControlOutput { Throttle = h.HoverCollective }, Dt);
            Assert.True(h.Rpm > 0.99f, $"rpm {h.Rpm:0.000} after 10 s at hover collective");
        }

        [Fact]
        public void AtOneHundredSevenMetresPerSecondTheSustainableCollectiveCannotHoldTheHeight()
        {
            // The in-game UH-90 sank at 14 m/s at 107 m/s with the collective pinned.
            var h = new RotaryPlant(RotaryParams.Utility, new Vec3(0f, 1500f, 0f), new Vec3(0f, 0f, 107f), 0f);
            h.SetAttitude(-15f, 0f);
            for (int i = 0; i < 5 * 60; i++) h.Step(new ControlOutput { Throttle = RotaryParams.Utility.SustainCollective }, Dt);
            Assert.True(h.Velocity.Y < -2f, $"vertical speed {h.Velocity.Y:0.0} m/s");
        }

        [Fact]
        public void RollRateFollowsTheStickThroughTheLag()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 60; i++) h.Step(new ControlOutput { Roll = 0.25f, Throttle = h.HoverCollective }, Dt);
            Assert.Equal(0.25f * 2f * 180f / (float)Math.PI, h.Read(Dt).P, 0);
            Assert.True(h.Right.Y < 0f && h.Up.X > 0f, "rolled right: right wing down, disc tilted right");
        }

        [Fact]
        public void NoseDownPitchTiltsTheDiscForward()
        {
            RotaryPlant h = Hovering();
            for (int i = 0; i < 30; i++) h.Step(new ControlOutput { Pitch = -0.3f, Throttle = h.HoverCollective }, Dt);
            Assert.True(h.PitchDeg < 0f && h.Up.Z > 0f, $"pitch {h.PitchDeg:0.0}, up {h.Up}");
        }
    }
}
