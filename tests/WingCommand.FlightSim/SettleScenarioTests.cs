using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class SettleScenarioTests
    {
        private const float Dt = 1f / 60f;

        [Fact]
        public void AHelicopterLandsAtAPointAndTakesOffAgain()
        {
            // Spec M4 §5.4: from 60 m up and 30 m off, down within 30 s, within 5 m, touching down under 1.5 m/s.
            var plant = new RotaryPlant(RotaryParams.Utility, new Vec3(30f, 60f, 0f), Vec3.Zero, 0f) { GroundY = 0f };
            AirframeProfile p = SimProfiles.Utility();
            IFlightPipeline pipe = FlightStack.NewPipeline(AirframeClass.Rotary);
            pipe.Track(plant.Read(Dt), new ControlOutput { Throttle = plant.Collective }, p);
            var settle = new SettlePilot(Vec3.Zero, 0f, 0f);
            var events = new WingEventRing();
            float t = 0f;
            for (; t < 60f && settle.Phase != SettlePhase.Down; t += Dt)
                plant.Step(settle.Step(plant.Read(Dt), p, pipe, t, Dt, events, 0), Dt);
            Assert.Equal(SettlePhase.Down, settle.Phase);
            Assert.True(t < 30f, $"down after {t:0.0} s");
            Assert.True(plant.Position.Horizontal.Length < 5f, $"{plant.Position.Horizontal.Length:0.0} m from the point");
            Assert.True(plant.ContactSpeed < 1.5f, $"touched down at {plant.ContactSpeed:0.00} m/s");
            for (float hold = 0f; hold < 5f; hold += Dt, t += Dt)
                plant.Step(settle.Step(plant.Read(Dt), p, pipe, t, Dt, events, 0), Dt);
            Assert.Equal(SettlePhase.Down, settle.Phase);
            Assert.True(plant.Position.Y < 0.5f, "stays down");

            settle.TakeOff();
            float liftStart = t;
            for (; t - liftStart < 30f && settle.Phase != SettlePhase.Done; t += Dt)
                plant.Step(settle.Step(plant.Read(Dt), p, pipe, t, Dt, events, 0), Dt);
            Assert.Equal(SettlePhase.Done, settle.Phase);
            Assert.True(t - liftStart < 20f, $"lifted off in {t - liftStart:0.0} s");
        }
    }
}
