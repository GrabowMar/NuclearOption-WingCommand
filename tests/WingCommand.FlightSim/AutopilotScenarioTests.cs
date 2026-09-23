using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class AutopilotScenarioTests
    {
        private const float Dt = 1f / 60f;

        private static (FixedWingPlant plant, SimPilot pilot) Start(float altitude = 2000f, float speed = 200f)
        {
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(0f, altitude, 0f), speed, 0f);
            return (plant, new SimPilot(plant, SimProfiles.GenericFighter()));
        }

        [Fact]
        public void AltitudeStepOfThreeHundredMetresOvershootsLessThanTenPercentAndSettles()
        {
            var (plant, pilot) = Start();
            var hold = new HoldSpec { Lateral = LateralHold.Level, Vertical = VerticalHold.Altitude, AltitudeM = 2300f, Speed = true, SpeedMps = 200f };
            float peak = 0f, settledAt = float.NaN;
            for (int i = 0; i < 60 * 60; i++)
            {
                pilot.StepHold(hold, Dt);
                peak = Math.Max(peak, plant.Position.Y);
                if (float.IsNaN(settledAt) && Math.Abs(plant.Position.Y - 2300f) < 10f) settledAt = i * Dt;
            }
            Assert.True(peak < 2330f, $"peak {peak:0} m");
            Assert.True(settledAt < 30f, $"within 10 m at {settledAt:0.0} s");
            Assert.InRange(plant.Position.Y, 2290f, 2310f);
        }

        [Fact]
        public void AltitudeStepDownOfThreeHundredMetresStaysWingsLevelOnHeading()
        {
            var (plant, pilot) = Start();
            var hold = new HoldSpec { Lateral = LateralHold.Level, Vertical = VerticalHold.Altitude, AltitudeM = 1700f, Speed = true, SpeedMps = 200f };
            float maxBank = 0f, maxHeading = 0f, trough = float.MaxValue;
            for (int i = 0; i < 60 * 60; i++)
            {
                pilot.StepHold(hold, Dt);
                maxBank = Math.Max(maxBank, Math.Abs(plant.BankDeg));
                float heading = plant.HeadingDeg > 180f ? plant.HeadingDeg - 360f : plant.HeadingDeg;
                maxHeading = Math.Max(maxHeading, Math.Abs(heading));
                trough = Math.Min(trough, plant.Position.Y);
            }
            Assert.True(maxBank < 5f, $"max |bank| {maxBank:0.0}");
            Assert.True(maxHeading < 2f, $"max heading deviation {maxHeading:0.0}");
            Assert.True(trough > 1670f, $"trough {trough:0} m");
            Assert.InRange(plant.Position.Y, 1690f, 1710f);
        }

        [Fact]
        public void SpeedStepDownOpensTheAirbrakeOnceInsteadOfPulsing()
        {
            var (plant, pilot) = Start(speed: 250f);
            var hold = new HoldSpec { Lateral = LateralHold.Level, Vertical = VerticalHold.Altitude, AltitudeM = 2000f, Speed = true, SpeedMps = 200f };
            int engagements = 0, run = 0, shortest = int.MaxValue;
            bool previous = false;
            for (int i = 0; i < 60 * 60; i++)
            {
                pilot.StepHold(hold, Dt);
                bool open = pilot.Last.Airbrake;
                if (open) run++;
                if (open && !previous) engagements++;
                if (!open && previous) { shortest = Math.Min(shortest, run); run = 0; }
                previous = open;
            }
            Assert.InRange(engagements, 1, 2);
            Assert.True(shortest * Dt >= FixedWingController.AirbrakeMinOnSeconds - 1e-3f, $"shortest opening {shortest * Dt:0.00} s");
            Assert.InRange(plant.Speed, 195f, 205f);
        }

        [Fact]
        public void HeadingStepOfNinetyDegreesOvershootsLessThanNineDegrees()
        {
            var (plant, pilot) = Start();
            var hold = new HoldSpec { Lateral = LateralHold.Heading, HeadingDeg = 90f, Vertical = VerticalHold.Altitude, AltitudeM = 2000f };
            float peak = 0f;
            // A 30° bank at 200 m/s turns 1.6°/s, so 90° takes about 56 s plus roll-in and capture.
            for (int i = 0; i < 90 * 60; i++)
            {
                pilot.StepHold(hold, Dt);
                peak = Math.Max(peak, plant.HeadingDeg < 270f ? plant.HeadingDeg : plant.HeadingDeg - 360f);
            }
            Assert.True(peak < 99f, $"peak heading {peak:0.0}");
            Assert.InRange(plant.HeadingDeg, 88f, 92f);
        }

        [Fact]
        public void SpeedStepOfFiftyMetresPerSecondOvershootsLessThanFive()
        {
            var (plant, pilot) = Start();
            var hold = new HoldSpec { Lateral = LateralHold.Level, Vertical = VerticalHold.Altitude, AltitudeM = 2000f, Speed = true, SpeedMps = 250f };
            float peak = 0f;
            for (int i = 0; i < 60 * 60; i++)
            {
                pilot.StepHold(hold, Dt);
                peak = Math.Max(peak, plant.Speed);
            }
            Assert.True(peak < 255f, $"peak speed {peak:0.0}");
            Assert.InRange(plant.Speed, 245f, 255f);
        }
    }
}
