using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class ProfileFitTests
    {
        private static TelemetryRing Synthetic()
        {
            var ring = new TelemetryRing();
            for (int i = 0; i < (int)(StepSequence.Duration * 60f); i++)
            {
                float t = i / 60f;
                StepCommand c = StepSequence.At(t);
                bool open = c.Phase == StepPhase.Open;
                float along = t >= StepSequence.ThrustFrom && t < StepSequence.ThrustTo ? 6f
                    : t >= StepSequence.BrakeFrom && t < StepSequence.BrakeTo ? -8f : 0f;
                ring.Push(new TelemetryRow
                {
                    // Between the pulses the brain rolls too, faster than the test's steps: it must not be fitted.
                    Time = t, Roll = open ? c.Roll : 0.4f, Pitch = c.Pitch, Throttle = c.Throttle,
                    RollRate = open ? c.Roll * 300f : 500f,   // open loop: full stick = 300 deg/s
                    AccelAlong = along, Vel = new Vec3(0f, 0f, 200f),
                });
            }
            return ring;
        }

        [Fact]
        public void FitsRollRateThrustAndAirbrakeFromAStepTest()
        {
            Dictionary<string, float> fit = ProfileFit.Fit(Synthetic());
            Assert.Equal(300f, fit["RollRateMaxDps"], 1);
            Assert.Equal(6f, fit["ThrustAccelMax"], 2);
            Assert.Equal(8f, fit["AirbrakeDecel"], 2);
        }

        [Fact]
        public void ClimbingDoesNotReadAsDrag()
        {
            // Climbing at 10 m/s on 200 m/s while the kinematic acceleration is zero: thrust = g·sinγ ≈ 0.49 m/s².
            var ring = new TelemetryRing();
            for (int i = (int)((StepSequence.ThrustTo - 4f) * 60f); i < (int)(StepSequence.ThrustTo * 60f); i++)
                ring.Push(new TelemetryRow { Time = i / 60f, AccelAlong = 0f, Vel = new Vec3(0f, 10f, 199.75f) });
            Assert.Equal(Scalar.G * 10f / 200f, ProfileFit.Fit(ring)["ThrustAccelMax"], 2);
        }

        [Fact]
        public void MergeKeepsOtherAirframesAndWritesInvariantNumbers()
        {
            string merged = ProfileFit.Merge("{ \"A-19\": { \"RollGain\": 2 }, \"FS-20\": { \"TauCross\": 3 } }", "FS-20",
                new Dictionary<string, float> { ["RollRateMaxDps"] = 287.5f });
            var lib = new ProfileLibrary();
            Assert.Empty(lib.AddLayer("calibrated", merged));
            AirframeProfile fs20 = lib.Build(new ProfileInputs { UnitName = "FS-20" }, new List<string>());
            Assert.Equal(287.5f, fs20.RollRateMaxDps);
            Assert.Equal(3f, fs20.TauCross);
            Assert.Equal(2f, lib.Build(new ProfileInputs { UnitName = "A-19" }, new List<string>()).RollGain);
            Assert.Contains("287.5", merged);
        }

        [Fact]
        public void MergeStartsAFreshFile() =>
            Assert.Contains("\"FS-20\"", ProfileFit.Merge(null, "FS-20", new Dictionary<string, float> { ["AirbrakeDecel"] = 7f }));
    }
}
