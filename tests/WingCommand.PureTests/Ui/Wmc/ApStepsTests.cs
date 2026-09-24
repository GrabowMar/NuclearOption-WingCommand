using Xunit;

namespace WingCommand.PureTests
{
    public class ApStepsTests
    {
        [Fact]
        public void ApStepsWrapAndClamp()
        {
            var h = new HoldSpec { HeadingDeg = 357f, AltitudeM = 14950f, VerticalSpeedMps = -29.5f, SpeedMps = 1f };
            ApSteps.Adjust(ref h, ApField.Heading, 1);
            Assert.Equal(2f, h.HeadingDeg, 3);
            ApSteps.Adjust(ref h, ApField.Heading, -1);
            Assert.Equal(357f, h.HeadingDeg, 3);
            ApSteps.Adjust(ref h, ApField.Altitude, 1);
            Assert.Equal(ApSteps.MaxAltitude, h.AltitudeM);
            ApSteps.Adjust(ref h, ApField.VerticalSpeed, -1);
            Assert.Equal(-ApSteps.MaxVerticalSpeed, h.VerticalSpeedMps);
            ApSteps.Adjust(ref h, ApField.Speed, -1);
            Assert.Equal(0f, h.SpeedMps);
        }

        [Fact]
        public void ANanTargetStartsFromZero()
        {
            var h = new HoldSpec { AltitudeM = float.NaN, HeadingDeg = float.NaN };
            ApSteps.Adjust(ref h, ApField.Altitude, 1);
            ApSteps.Adjust(ref h, ApField.Heading, -1);
            Assert.Equal(ApSteps.AltitudeStep, h.AltitudeM);
            Assert.Equal(360f - ApSteps.HeadingStep, h.HeadingDeg);
        }

        [Fact]
        public void SpeedStepsInKilometresPerHour()
        {
            var h = new HoldSpec { SpeedMps = 100f };
            ApSteps.Adjust(ref h, ApField.Speed, 1);
            Assert.Equal(100f + ApSteps.SpeedStepKmh / 3.6f, h.SpeedMps, 3);
        }

        [Fact]
        public void ReadoutsMatchTheHud()
        {
            var h = new HoldSpec { HeadingDeg = 5.4f, AltitudeM = 3200.4f, VerticalSpeedMps = 2f, SpeedMps = 50f };
            Assert.Equal("005°", ApSteps.Readout(h, ApField.Heading));
            Assert.Equal("3200 m", ApSteps.Readout(h, ApField.Altitude));
            Assert.Equal("+2.0 m/s", ApSteps.Readout(h, ApField.VerticalSpeed));
            Assert.Equal("180 km/h", ApSteps.Readout(h, ApField.Speed));
        }

        [Fact]
        public void ClampNeverMovesAgainstThePress()
        {
            // Review M7b-1 minor: a captured value beyond a limit never jumps the wrong way.
            var h = new HoldSpec { VerticalSpeedMps = -40f, AltitudeM = 16000f };
            ApSteps.Adjust(ref h, ApField.VerticalSpeed, 1);
            Assert.Equal(-39f, h.VerticalSpeedMps, 3);
            h.VerticalSpeedMps = 45f;
            ApSteps.Adjust(ref h, ApField.VerticalSpeed, 1);
            Assert.Equal(45f, h.VerticalSpeedMps, 3);
            ApSteps.Adjust(ref h, ApField.VerticalSpeed, -1);
            Assert.Equal(44f, h.VerticalSpeedMps, 3);
            ApSteps.Adjust(ref h, ApField.Altitude, 1);
            Assert.Equal(16000f, h.AltitudeM, 3);
        }

        [Fact]
        public void ReadoutIsADashWhenNotHeld()
        {
            var h = new HoldSpec { HeadingDeg = 90f };
            Assert.Equal("—", ApSteps.Readout(h, ApField.Heading, held: false));
            Assert.Equal("090°", ApSteps.Readout(h, ApField.Heading, held: true));
        }
    }
}
