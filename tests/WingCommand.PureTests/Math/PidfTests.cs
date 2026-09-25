using Xunit;

namespace WingCommand.PureTests
{
    public class PidfTests
    {
        private const float Dt = 1f / 60f;

        [Fact]
        public void ProportionalOutputIsClamped()
        {
            var pid = new Pidf { Kp = 2f };
            Assert.Equal(1f, pid.Update(1f, 0f, Dt));
        }

        [Fact]
        public void IntegralAccumulatesError()
        {
            var pid = new Pidf { Ki = 1f };
            // Output reflects the integral before this tick's increment, so 61 ticks read 1 s of error.
            for (int i = 0; i < 61; i++) pid.Update(0.5f, 0f, Dt);
            Assert.Equal(0.5f, pid.Output, 3);
        }

        [Fact]
        public void BackCalculationLetsASaturatedLoopRecoverQuickly()
        {
            var pid = new Pidf { Kp = 0.5f, Ki = 1f };
            for (int i = 0; i < 600; i++) pid.Update(10f, 0f, Dt);
            Assert.Equal(1f, pid.Output);
            int steps = 0;
            while (pid.Update(-0.5f, 0f, Dt) >= 0.99f && steps < 600) steps++;
            Assert.True(steps * Dt < 3f, $"left saturation after {steps * Dt:0.00} s");
        }

        [Fact]
        public void ZeroSetpointWeightOnDerivativeAvoidsKick()
        {
            var noKick = new Pidf { Kd = 1f, B = 0f, C = 0f };
            var kick = new Pidf { Kd = 1f, B = 0f, C = 1f };
            noKick.Update(0f, 0f, Dt);
            kick.Update(0f, 0f, Dt);
            Assert.Equal(0f, noKick.Update(1f, 0f, Dt));
            Assert.True(kick.Update(1f, 0f, Dt) > 0.1f);
        }

        [Fact]
        public void OutputRateIsLimited()
        {
            var pid = new Pidf { Kp = 1f, MaxRate = 1f };
            pid.Update(0f, 0f, Dt);
            Assert.Equal(Dt, pid.Update(1f, 0f, Dt), 4);
        }

        [Fact]
        public void TrackMakesTheNextUpdateContinueFromTheAppliedOutput()
        {
            var pid = new Pidf { Kp = 1f, Ki = 1f };
            pid.Track(0f, 0f, 0.4f);
            Assert.Equal(0.4f, pid.Update(0f, 0f, Dt), 3);
        }

        [Fact]
        public void TrackOnAProportionalOnlyLoopLeavesNoBias()
        {
            var pid = new Pidf { Kp = 1f };
            pid.Track(0f, 0f, 0.4f);
            Assert.Equal(0f, pid.Update(0f, 0f, Dt), 4);
        }

        [Fact]
        public void ZeroDtReturnsPreviousOutput()
        {
            var pid = new Pidf { Kp = 1f, Ki = 1f, Kd = 1f };
            float first = pid.Update(0.3f, 0f, Dt);
            Assert.Equal(first, pid.Update(0.9f, 0f, 0f));
        }
    }
}
