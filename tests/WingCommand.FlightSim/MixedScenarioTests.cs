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

        [Fact]
        public void JetAndHelicopterEscortAGroundUnit()
        {
            // E1: a truck at 15 m/s on flat ground, weaving gently; a helicopter and a jet escort it in close escort.
            var truck = new VirtualLeader(new Vec3(0f, 0f, 0f), 15f, 0f) { Kind = AnchorKind.Ground };
            var wing = new MixedSimWing(truck, SimFormations.Get("close-escort"), 60f) { FloorY = 0f };
            var helo = new RotaryPlant(RotaryParams.Utility, new Vec3(-60f, 60f, -30f), new Vec3(0f, 0f, 15f), 0f);
            wing.Add(helo, SimProfiles.Utility(), helo.Collective);
            AirframeProfile jetProfile = SimProfiles.GenericFighter();
            var jet = new FixedWingPlant(PlantParams.GenericFighter, new Vec3(500f, 1200f, -2000f), 150f, 0f);
            wing.Add(jet, jetProfile, jet.ThrottleActual);
            float heloMax = 0f, jetFar = 0f, minHeloHeight = float.MaxValue;
            Vec3 offsetSum = Vec3.Zero;
            int samples = 0;
            bool jetHeld = true;
            for (int i = 0; i < 400 * 60; i++)
            {
                float t = i * Dt;
                truck.Step((int)(t / 30f) % 2 == 0 ? 3f : -3f, Dt);
                wing.Step();
                if (t < 230f) continue;                       // one full orbit (~166 s) measured at the end
                heloMax = Math.Max(heloMax, wing.SlotError(0));
                minHeloHeight = Math.Min(minHeloHeight, helo.Position.Y);
                Vec3 offset = (jet.Position - truck.Position).Horizontal;
                offsetSum += offset;
                jetFar = Math.Max(jetFar, offset.Length);
                samples++;
                jetHeld &= wing.Pilots[1].Mind.Current == BehaviourId.HoldOverhead;
            }
            Assert.True(heloMax < 30f, $"helicopter up to {heloMax:0} m from its close-escort slot");
            Assert.True(minHeloHeight >= 30f, $"helicopter down to {minHeloHeight:0} m over the ground");
            Assert.True(jetHeld, "the jet left its high-cover orbit");
            float mean = (offsetSum / samples).Length;
            Assert.True(mean < 800f, $"jet orbit centre {mean:0} m off the truck");
            Assert.True(jetFar < HoldOrbit.RadiusFor(FormationPilot.OrbitSpeed(jetProfile)) + 1500f, $"jet up to {jetFar:0} m away");
        }

        private static AirframeProfile TiltwingProfile(out PlantParams pp)
        {
            pp = PlantParams.CoinTurboprop;
            float stall = (float)Math.Sqrt(pp.MassKg * Scalar.G / (0.5 * 1.225 * pp.WingAreaM2 * pp.ClMax));
            return AirframeProfile.Derive(new ProfileInputs
            {
                UnitName = "sim-tiltwing", Class = AirframeClass.Tiltwing, PublishedStallKmh = stall * 3.6f, MaxSpeed = 160f,
                CornerSpeed = pp.CornerSpeed, PidReferenceAirspeed = 110f, GLimit = pp.GLimit, CruiseThrottle = 0.55f,
                FbwMaxRollAngularVel = pp.MaxRollAngularVel, FbwGLimit = pp.GLimit, FbwCornerSpeed = pp.CornerSpeed, MaxRadius = 7f,
            });
        }

        [Fact]
        public void TiltwingConvertsOnceEachWayBehindALeaderThatSlowsAndSpeedsUp()
        {
            // T1: a tiltwing rejoins from 3 km at 120 m/s; the leader slows to 20 m/s at 90 s and speeds back up to
            // 120 m/s at 180 s. One conversion each way; on the first tick after each, the incoming pipeline's bank and
            // tilt commands are within 10 deg of the attitude it took over (no bump); no sag after converting; back in
            // its slot by the end (the turboprop accelerates slowly: it trails the re-acceleration and closes afterwards).
            AirframeProfile p = TiltwingProfile(out PlantParams pp);
            var leader = new VirtualLeader(new Vec3(0f, 1500f, 3000f), 120f, 0f) { CanHover = true, SpeedChangeRate = 2f };
            var wing = new MixedSimWing(leader, SimFormations.Get("echelon-right"), FormationCatalog.Standard);
            var plant = new TiltwingPlant(pp, RotaryParams.Utility, p.ConversionLow, p.ConversionHigh,
                new Vec3(80f, 1500f, 0f), new Vec3(0f, 0f, 120f), 0f);
            wing.Add(plant, p, 0.55f);
            var pipeline = (TiltwingPipeline)wing.Pilots[0].Pipeline;
            int seen = 0;
            float maxStep = 0f, maxSlotError = 0f, lowest = float.MaxValue;
            for (int i = 0; i < 400 * 60; i++)
            {
                float t = i * Dt;
                leader.Step(0f, Dt, t < 90f ? 120f : t < 180f ? 20f : 120f, 0f);
                AircraftState before = plant.Read(Dt);
                bool justConverted = pipeline.Conversions != seen;
                seen = pipeline.Conversions;
                wing.Step();
                if (justConverted)
                {
                    float bank = pipeline.Mode == TiltwingMode.Rotary ? pipeline.Rotary.Controller.RollTargetDeg : pipeline.Plane.LastAttitude.BankDeg;
                    maxStep = Math.Max(maxStep, Math.Abs(bank - before.BankDeg));
                    if (pipeline.Mode == TiltwingMode.Rotary)
                        maxStep = Math.Max(maxStep, Math.Abs(pipeline.Rotary.Controller.PitchTargetDeg - before.PitchDeg));
                }
                lowest = Math.Min(lowest, plant.Position.Y);
                if (t > 380f) maxSlotError = Math.Max(maxSlotError, wing.SlotError(0));
            }
            Assert.Equal(2, pipeline.Conversions);
            Assert.True(maxStep < 10f, $"a command stepped {maxStep:0.0} deg from the attitude at a conversion");
            Assert.True(lowest > 1500f - 150f, $"sank to {lowest:0} m (leader at 1500 m)");
            Assert.True(maxSlotError < 2f * FormationCatalog.Standard, $"up to {maxSlotError:0} m from its slot at the end");
        }

        [Fact]
        public void TiltwingBehindALeaderInsideItsConversionBandDoesNotChatter()
        {
            // Review M2b I4: a leader steady at 52 m/s (band 43-55) with S-turns converted the tiltwing 8 times in 300 s.
            AirframeProfile p = TiltwingProfile(out PlantParams pp);
            float mid = p.ConversionHigh - 2.7f;   // 52 m/s: near the top of the band, where the old caps chattered
            var leader = new VirtualLeader(new Vec3(0f, 1500f, 0f), mid, 0f) { CanHover = true };
            var wing = new MixedSimWing(leader, SimFormations.Get("echelon-right"), FormationCatalog.Standard);
            var plant = new TiltwingPlant(pp, RotaryParams.Utility, p.ConversionLow, p.ConversionHigh,
                new Vec3(80f, 1500f, -80f), new Vec3(0f, 0f, 40f), 0f);   // starts in rotary flight, below the band
            wing.Add(plant, p, 0.55f);
            var pipeline = (TiltwingPipeline)wing.Pilots[0].Pipeline;
            for (int i = 0; i < 300 * 60; i++)
            {
                float t = i * Dt;
                leader.Step((int)(t / 10f) % 2 == 0 ? 10f : -10f, Dt);
                wing.Step();
            }
            Assert.True(pipeline.Conversions <= 1, $"{pipeline.Conversions} conversions in 300 s");
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
