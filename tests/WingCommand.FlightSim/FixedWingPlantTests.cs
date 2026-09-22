using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class FixedWingPlantTests
    {
        private static FixedWingPlant LevelAt(float speed, float altitude, float heading = 0f) =>
            new FixedWingPlant(PlantParams.GenericFighter, new Vec3(0f, altitude, 0f), speed, heading);

        [Fact]
        public void ZeroStickAtTrimThrottleHoldsHeightSpeedAndTrack()
        {
            var plant = LevelAt(200f, 3000f, heading: 45f);
            float trim = plant.TrimThrottle();
            plant.SetThrottleState(trim);
            var input = new PlantInput(pitch: 0f, roll: 0f, throttle: trim);

            for (int i = 0; i < 60 * 50; i++) plant.Step(input, 0.02f);

            Assert.InRange(plant.Position.Y, 2970f, 3030f);
            Assert.InRange(plant.Speed, 195f, 205f);
            Assert.Equal(45f, plant.HeadingDeg, 1);
            Assert.Equal(0f, plant.BankDeg, 3);
        }

        [Fact]
        public void RollStickCommandsARateSoBankHoldsWhenReleased()
        {
            var plant = LevelAt(200f, 3000f);
            var right = new PlantInput(0f, 1f, 0.6f);
            var neutral = new PlantInput(0f, 0f, 0.6f);

            for (int i = 0; i < 10; i++) plant.Step(right, 0.02f); // 0.2 s full right stick
            float atRelease = plant.BankDeg;
            for (int i = 0; i < 50; i++) plant.Step(neutral, 0.02f); // rate decays through the lag
            float settled = plant.BankDeg;
            for (int i = 0; i < 50; i++) plant.Step(neutral, 0.02f);

            Assert.InRange(atRelease, 10f, PlantParams.GenericFighter.RollRateMaxDps * 0.2f);
            Assert.True(settled > atRelease, "Released stick must not level the wings.");
            Assert.Equal(settled, plant.BankDeg, 0);
        }

        [Fact]
        public void PullIsLimitedByTheGLimitAndByAvailableLift()
        {
            var fast = LevelAt(280f, 2000f);
            var slow = LevelAt(70f, 2000f);
            var pull = new PlantInput(1f, 0f, 1f);
            float fastPeak = 0f, slowPeak = 0f;

            for (int i = 0; i < 150; i++)
            {
                fast.Step(pull, 0.02f);
                slow.Step(pull, 0.02f);
                fastPeak = Math.Max(fastPeak, fast.LoadFactor);
                slowPeak = Math.Max(slowPeak, slow.LoadFactor);
            }

            Assert.InRange(fastPeak, PlantParams.GenericFighter.GLimit - 0.5f, PlantParams.GenericFighter.GLimit + 1e-3f);
            Assert.True(slowPeak < 3f, $"70 m/s must not reach high g, got {slowPeak:0.00}");
        }

        [Fact]
        public void BankWithoutBackPressureLosesHeight()
        {
            // Pitch stick commands pitch rate; holding zero pitch rate in a bank means n = cos(bank),
            // so the flight path sags. Formation guidance must supply the pull itself.
            var plant = LevelAt(200f, 3000f);
            plant.SetBankState(60f);
            var hold = new PlantInput(0f, 0f, plant.TrimThrottle());

            for (int i = 0; i < 250; i++) plant.Step(hold, 0.02f);

            Assert.True(plant.Position.Y < 2950f);
        }

        [Fact]
        public void EngineRespondsWithAFirstOrderLag()
        {
            var plant = LevelAt(200f, 3000f);
            plant.SetThrottleState(0.3f);
            var step = new PlantInput(0f, 0f, 0.8f);
            float tau = PlantParams.GenericFighter.EngineLagS;

            for (float t = 0f; t < tau - 1e-4f; t += 0.02f) plant.Step(step, 0.02f);

            float expected = 0.3f + 0.5f * (1f - (float)Math.Exp(-1.0));
            Assert.Equal(expected, plant.ThrottleActual, 1);
        }

        [Fact]
        public void AirbrakeOpensOnlyAtExactlyZeroThrottle()
        {
            var idle = LevelAt(250f, 3000f);
            var braked = LevelAt(250f, 3000f);
            idle.SetThrottleState(0.01f);
            braked.SetThrottleState(0f);

            for (int i = 0; i < 100; i++)
            {
                idle.Step(new PlantInput(0f, 0f, 0.01f), 0.02f);
                braked.Step(new PlantInput(0f, 0f, 0f), 0.02f);
            }

            Assert.True(braked.Speed < idle.Speed - 5f, $"braked {braked.Speed:0.0} vs idle {idle.Speed:0.0}");
            Assert.False(idle.AirbrakeOpen);
            Assert.True(braked.AirbrakeOpen);
        }

        [Fact]
        public void AboveCornerSpeedFullStickRollsFasterThanTheRateLoop()
        {
            var slow = LevelAt(160f, 1000f);
            var fast = LevelAt(320f, 1000f);
            var right = new PlantInput(0f, 1f, 0.6f);
            for (int i = 0; i < 60; i++)
            {
                slow.Step(right, 1f / 60f);
                fast.Step(right, 1f / 60f);
            }
            Assert.True(fast.RollRateDps > slow.RollRateDps * 1.2f,
                $"fast {fast.RollRateDps:0} vs slow {slow.RollRateDps:0} deg/s");
        }

        [Fact]
        public void SensorReportsLevelFlightAsOneGWingsLevel()
        {
            var plant = LevelAt(200f, 2000f);
            plant.Step(new PlantInput(0f, 0f, plant.TrimThrottle()), 1f / 60f);
            AircraftState s = SimSensor.Read(plant, 1f / 60f);
            Assert.Equal(1f, s.Nz, 2);
            Assert.Equal(0f, s.BankDeg, 3);
            Assert.Equal(200f, s.Tas, 0);
            Assert.True(s.FbwActive);
        }
    }
}
