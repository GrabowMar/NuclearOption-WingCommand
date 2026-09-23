using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.FlightSim
{
    public class MixedScenarioTests
    {
        private const float Dt = MixedSimWing.Dt;

        [Fact]
        public void JetBehindASlowHelicopterTakesHighCoverAndComesBackWhenItSpeedsUp()
        {
            // X1: a jet wingman behind a helicopter leader at 40 m/s; after 120 s the leader speeds up to 150 m/s.
            var leader = new VirtualLeader(new Vec3(0f, 1500f, 0f), 40f, 0f) { CanHover = true, SpeedChangeRate = 3f };
            var wing = new MixedSimWing(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard);
            AirframeProfile jet = SimProfiles.GenericFighter();
            var plant = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(-80f, 1500f, -1500f), 150f, 0f);
            wing.Add(plant, jet, plant.ThrottleActual);
            FormationPilot pilot = wing.Pilots[0];
            float coverAt = float.NaN, fastAt = float.NaN, backAt = float.NaN, minSpeed = float.MaxValue;
            for (int i = 0; i < 240 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt, t < 120f ? 40f : 150f, 0f);
                wing.Step();
                minSpeed = Math.Min(minSpeed, plant.Speed);
                if (float.IsNaN(coverAt) && pilot.Mind.Current == BehaviourId.HoldOverhead) coverAt = t;
                if (float.IsNaN(fastAt) && leader.Speed > RolePolicy.RecoverFactor * jet.MinimumSpeed(1f)) fastAt = t;
                if (!float.IsNaN(fastAt) && float.IsNaN(backAt) && pilot.Roles.Current == Role.Slot) backAt = t;
            }
            Assert.True(coverAt <= 5f, $"high cover at {coverAt:0.0} s");
            Assert.True(minSpeed >= jet.MinimumSpeed(1f), $"slowest {minSpeed:0} m/s (Vmin {jet.MinimumSpeed(1f):0})");
            Assert.True(backAt - fastAt <= 5f, $"back to its slot role {backAt - fastAt:0.0} s after the leader passed 1.3 Vmin");
            Assert.Equal(BehaviourId.StationKeep, pilot.Mind.Current);
        }

        [Fact]
        public void HelicopterBehindAFastJetTrailsOnItsRouteAndRejoinsWhenItSlows()
        {
            // X2: a helicopter behind a jet leader dashing at 200 m/s through a ~35° right turn, then slowing to 50 m/s
            // at 20 s (the spec's slow jet). Closing at cruise − 50 ≈ 14 m/s takes minutes from kilometres behind, so the
            // capture bound scales with the gap (the spec's flat 90 s assumed a short gap).
            var leader = new VirtualLeader(new Vec3(0f, 1000f, 0f), 200f, 0f);
            var wing = new MixedSimWing(leader, SimFormations.Get("staggered-trail"), FormationCatalog.Standard);
            AirframeProfile helo = SimProfiles.Utility();
            var plant = new RotaryPlant(RotaryParams.Utility, new Vec3(40f, 1000f, -80f), new Vec3(0f, 0f, 60f), 0f);
            wing.Add(plant, helo, plant.Collective);
            FormationPilot pilot = wing.Pilots[0];
            var route = new List<Vec3>();
            bool trailed = false;
            float maxCross = 0f, slowAt = float.NaN, gap = 0f, capturedAt = float.NaN;
            for (int i = 0; i < 480 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(t >= 5f && t < 17f ? 45f : 0f, Dt, t < 20f ? 200f : 50f, 0f);
                wing.Step();
                if (i % 10 == 0) route.Add(leader.Position);
                if (pilot.Mind.Current == BehaviourId.Trail)
                {
                    trailed = true;
                    if (i % 30 == 0) maxCross = Math.Max(maxCross, CrossTrack(route, plant.Position));
                }
                if (float.IsNaN(slowAt) && t > 20f && leader.Speed <= 50.5f)
                {
                    slowAt = t;
                    gap = (leader.Position - plant.Position).Horizontal.Length;
                }
                if (!float.IsNaN(slowAt) && float.IsNaN(capturedAt) && pilot.Mind.Current == BehaviourId.StationKeep) capturedAt = t;
            }
            Assert.True(trailed, "never took the trail");
            Assert.True(maxCross < 150f, $"up to {maxCross:0} m off the leader's route");
            float allowed = gap / (helo.CruiseSpeed - 50f) + 60f;
            Assert.True(capturedAt - slowAt <= allowed,
                $"captured {capturedAt - slowAt:0} s after the leader slowed ({gap:0} m behind; allowed {allowed:0} s)");
        }

        /// <summary>Horizontal distance from <paramref name="p"/> to the polyline <paramref name="route"/>.</summary>
        private static float CrossTrack(List<Vec3> route, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vec3 a = route[i], seg = (route[i + 1] - a).Horizontal;
                float t = seg.SqrLength > 1e-6f ? Scalar.Clamp01(Vec3.Dot((p - a).Horizontal, seg) / seg.SqrLength) : 0f;
                best = Math.Min(best, (a.Horizontal + seg * t - p.Horizontal).Length);
            }
            return best;
        }
    }
}
