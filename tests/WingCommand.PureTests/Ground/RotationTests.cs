using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>Night-1 sim runs (2026-09-28): an SFB-81 rolled 2.9 km to 97 m/s (take-off speed 80) with its nose at 1.5° and ran off
    /// the end of the runway: the rotation asked for (10° − pitch) × 0.08 of stick, which the fly-by-wire passes at a fifth on the
    /// ground. The rotation asks for full nose-up stick until close to the rotation pitch, and for full stick past 1.1 × take-off
    /// speed still on the wheels.</summary>
    public class RotationTests
    {
        [Fact]
        public void BelowTheRotationSpeedTheStickStaysNeutral() => Assert.Equal(0f, GroundPilot.RotateStick(50f, 80f, 1f));

        [Fact]
        public void AtTheRotationSpeedWithTheNoseDownTheStickIsFullyBack() => Assert.Equal(1f, GroundPilot.RotateStick(60f, 80f, 1.5f));

        [Fact]
        public void NearTheRotationPitchTheStickEasesOff()
        {
            float stick = GroundPilot.RotateStick(70f, 80f, GroundPilot.RotatePitchDeg - 1f);
            Assert.InRange(stick, 0.05f, 0.5f);
            Assert.Equal(0f, GroundPilot.RotateStick(70f, 80f, GroundPilot.RotatePitchDeg + 2f));
        }

        [Fact]
        public void PastTheTakeoffSpeedStillOnTheWheelsTheStickIsFullyBack() =>
            Assert.Equal(1f, GroundPilot.RotateStick(90f, 80f, GroundPilot.RotatePitchDeg));
    }
}
