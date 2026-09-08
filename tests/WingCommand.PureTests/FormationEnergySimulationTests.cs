using System;
using System.Numerics;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationEnergySimulationTests
    {
        [Fact]
        public void ArrivalClosesFasterButRestoresDampingAtStation()
        {
            float arrival = FormationControlRules.RejoinClosure(150f, 20f,
                2f, 1f, 1f, 0.45f, 3f, 90f, 0.75f);
            float previous = 0.45f * 150f - 3f * 20f;
            Assert.True(arrival > previous);
            Assert.True(arrival <= FormationClosure.SafeClosure(150f, 2f, 0.75f));
            Assert.Equal(0.45f * 50f - 3f * 20f,
                FormationControlRules.RejoinClosure(50f, 20f,
                    2f, 1f, 1f, 0.45f, 3f, 90f, 0.75f));
        }

        [Theory]
        [InlineData(6000f, 120f, false)]
        [InlineData(2000f, 250f, true)]
        [InlineData(300f, 120f, false)]
        [InlineData(150f, 140f, false)]
        public void AcceleratingAndHotJoinsSettleWithEngineLagAndFiniteDrag(
            float initialGap, float initialSpeed, bool mustBrake)
        {
            const float dt = 0.05f, leaderSpeed = 120f, maximum = 320f, minimum = 60f, spacing = 120f;
            float gap = initialGap, speed = initialSpeed, engine = speed / maximum;
            float minimumGap = gap, firstCapture = float.PositiveInfinity;
            bool braking = false, usedBrake = false, usedFullPower = false;
            for (int step = 0; step < 8000; step++)
            {
                var toSlot = new Vector2(0f, gap);
                var velocity = new Vector2(0f, speed);
                var leaderVelocity = new Vector2(0f, leaderSpeed);
                var plan = FormationIntercept.Solve(toSlot, leaderVelocity, Vector2.Zero,
                    leaderVelocity, speed, maximum, 0f);
                float closing = speed - leaderSpeed;
                float blend = Clamp(Math.Abs(gap) / WingTuning.CaptureDistance, 0f, 1f);
                blend = blend * blend * (3f - 2f * blend);
                float station = leaderSpeed + FormationControlRules.RejoinClosure(gap, closing,
                    2f, 1f, 1f, 0.45f, 3f, 90f, 0.75f);
                float approach = FormationTracking.ApproachSpeed(0f, gap, 0f, speed,
                    0f, leaderSpeed, 2f, 1f, 1f, 0.75f);
                float desired = station + (approach - station) * blend;
                desired = FormationClosure.PursuitSpeed(toSlot, velocity, plan,
                    desired, maximum, 2f, 0.75f, spacing);
                desired = Clamp(desired, minimum, maximum);
                float error = desired - speed;
                float rawThrottle = desired / maximum + error * WingTuning.ThrottleGain;
                var controls = FormationClosure.Resolve(rawThrottle, error, speed, minimum,
                    Math.Max(0f, gap), closing, spacing, 2f, 0.75f, 1f, 0f, 1000f, 0f,
                    false, true, true, braking);
                braking = controls.Airbrake;
                usedBrake |= braking;
                usedFullPower |= controls.Throttle == 1f;

                // Independent plant with engine lag, 4 m/s² thrust acceleration, 2 m/s² idle drag, and
                // 6 m/s² extra airbrake drag; driven by production throttle outputs.
                engine += (controls.Throttle - engine) * (1f - (float)Math.Exp(-dt / 0.75f));
                float acceleration = Clamp((engine * maximum - speed) / 8f, -2f, 4f);
                if (braking) acceleration -= 6f;
                speed += acceleration * dt;
                gap -= (speed - leaderSpeed) * dt;
                minimumGap = Math.Min(minimumGap, gap);
                if (Math.Abs(gap) < 100f && float.IsPositiveInfinity(firstCapture)) firstCapture = step * dt;
                Assert.True(speed >= minimum && float.IsFinite(gap));
            }
            Assert.InRange(Math.Abs(gap), 0f, 50f);
            Assert.InRange(Math.Abs(speed - leaderSpeed), 0f, 3f);
            Assert.InRange(firstCapture, 0f, 180f);
            Assert.True(minimumGap > -spacing, $"Overshot by {-minimumGap:F1}m");
            if (mustBrake) Assert.True(usedBrake);
            else if (initialGap > WingTuning.CaptureDistance) Assert.True(usedFullPower);
            Assert.False(braking);
        }

        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
    }
}
