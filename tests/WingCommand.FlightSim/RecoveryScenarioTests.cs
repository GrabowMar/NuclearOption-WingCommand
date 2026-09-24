using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>The recovery approach (spec M3 §4) flown by the production pipeline on the in-game boscali_north: a jet on
    /// RTB must reach the approach point and be handed to the game's landing.</summary>
    public class RecoveryScenarioTests
    {
        private const float Dt = 1f / 60f;

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void AJetOnRtbFromOverTheFieldReachesTheApproachPointAndIsHandedOver(int stack)
        {
            AirbaseSample sample = AirbaseSample.FromDumpJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "airbases",
                "boscali-north-hangars.json"))).Single();
            Assert.True(FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse));
            var field = new FieldTraffic(sample, runway, reverse);
            AirframeProfile p = SimProfiles.GenericFighter();
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(-18632f, 1100f, 32049f), 150f, 0f);
            var pilot = new FormationPilot(stack, AirframeClass.FixedWing);
            pilot.Track(SimSensor.Read(plant, Dt), new ControlOutput { Throttle = plant.ThrottleActual }, p);
            var recovery = new RecoveryPilot(1 + stack, field, AirframeClass.FixedWing, RecoveryIntent.Rtb, stack);
            var wing = new FormationWing(SimFormations.Get("finger-four-right"), 80f);
            var members = new WingMemberInput[stack + 1];
            float handedAt = float.NaN, closest = float.MaxValue;
            for (int i = 0; i < 400 * 60 && float.IsNaN(handedAt); i++)
            {
                AircraftState s = SimSensor.Read(plant, Dt);
                for (int k = 0; k <= stack; k++)
                    members[k] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = p.MilSpeed, MinSpeed = p.MinimumSpeed(1f) }, Radius = p.MaxRadius };
                WingFrame frame = wing.Update(new AnchorSample { Present = false }, members, stack + 1, float.NaN, 60f, 8f, Dt);
                FlightIntent intent = recovery.ApproachIntent(s, p, i * Dt, Dt, out bool go);
                closest = Math.Min(closest, (recovery.ApproachPoint(s) - s.Pos).Horizontal.Length);
                if (go) handedAt = i * Dt;
                plant.Step(pilot.FlyIntent(intent, frame, s, p, Dt), Dt);
            }
            Assert.False(float.IsNaN(handedAt), $"never handed over; closest {closest:0} m, ended at {plant.Position}, point {recovery.ApproachPoint(SimSensor.Read(plant, Dt))}");
            Assert.True(handedAt < 200f, $"handed over only after {handedAt:0} s");
        }
    }
}
