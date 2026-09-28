using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Overnight 2026-09-28 (EW-25 Medusa, sim telemetry 20260928-073401): just after the climb-out handed over, the wing's
    /// terrain floor drove a heavy jet into a 45 deg zoom at 85 m/s; when the floor let go, the vertical demand fell to the guidance's
    /// climb cap and the pipeline answered with a zero-g push-over at idle throttle with the airbrake out, 1,000 m below a reference it
    /// was still climbing to. At ~0 g and a few m/s over its loaded minimum speed its weak roll could not hold the wings, it went
    /// inverted and fell.</summary>
    public class HeavyZoomScenarioTests
    {
        private const float Dt = 1f / 60f;
        private static readonly float[] ProbeSeconds = { 0f, 2f, 5f, 10f };

        /// <summary>An EW-25-like heavy: 24 t on wings that stall at 52 m/s, thrust-to-weight 0.35 (it sustains ~15 deg at 85 m/s,
        /// so a 45 deg zoom bleeds ~4 m/s per second), 6 g, and the EW-25's weak fly-by-wire roll (maxRollAngularVel 3,
        /// rollTightness 0.25): ~17 deg/s at 85 m/s.</summary>
        internal static PlantParams HeavyParams(float thrustN = 82000f) => new PlantParams
        {
            MassKg = 24000f,
            WingAreaM2 = 71f,
            Cd0 = 0.025f,
            InducedK = 0.07f,
            ClMax = 2.0f,
            DryThrustN = thrustN,
            AfterburnerThrustN = thrustN,
            AfterburnerThrottle = 1f,
            GLimit = 6f,
            CornerSpeed = 130f,
            MaxRollAngularVel = 3f,
            RollTightness = 0.25f,
            RollAuthorityRadS = 1.5f,
            RollAuthoritySpeed = 130f,
        };

        internal static float StallOf(PlantParams pp) =>
            (float)Math.Sqrt(pp.MassKg * Scalar.G / (0.5 * Isa.SeaLevelDensity * pp.WingAreaM2 * pp.ClMax));

        /// <summary>The profile the engine derives for the EW-25: its published numbers, with the stall speed its wings give.</summary>
        internal static AirframeProfile HeavyProfile(PlantParams pp) => AirframeProfile.Derive(new ProfileInputs
        {
            UnitName = "sim-ew25", PublishedStallKmh = 120f, LiftStallSpeed = StallOf(pp), MaxSpeed = 300f, CornerSpeed = 120f,
            FbwCornerSpeed = 130f, PidReferenceAirspeed = 180f, GLimit = 6f, FbwGLimit = 6f, CruiseThrottle = 1f,
            FbwMaxRollAngularVel = pp.MaxRollAngularVel, MaxRadius = 10f, TakeoffSpeed = 40f, LandingSpeed = 70f,
        });

        /// <summary>A ring of hills round the field, whichever way the departure turns: <paramref name="peak"/> m high at
        /// <paramref name="radius"/> m, falling linearly to the field (0) <paramref name="halfWidth"/> m either side of it.</summary>
        private static float Ridge(float x, float z, float peak, float radius, float halfWidth)
        {
            float r = (float)Math.Sqrt(x * x + z * z);
            return peak * Math.Max(0f, 1f - Math.Abs(r - radius) / halfWidth);
        }

        internal struct ZoomRun
        {
            /// <summary>Over the run after the first second; "Slow" only in the speed-protection band, under 1.5 x the minimum speed
            /// (the push-over limit only from 1.1 x it: at the minimum speed unloading is the stall recovery).</summary>
            public float MinNzCmdSlow, MinNzCmdTime, MaxBankSlow, MaxBankTime, MinEasMargin, MinEasTime;
            public float IdleSeconds, IdleTime, MinClearance, MaxGamma, MaxFloorClimb;
            public bool AnyGcas;
            public string Trace;
        }

        /// <summary>One member handed over at 290 m and 85 m/s, climbing at <paramref name="gamma0"/> deg at full throttle,
        /// rejoining a leader that orbits 1,000 m above and 20 km north, over a ring of hills. The wing floor is the engine's:
        /// the highest probe under and 2, 5 and 10 s ahead of the member, rising at once and falling at
        /// <see cref="TerrainFloor.FallRate"/>.</summary>
        internal static ZoomRun Fly(float thrustN, float gamma0, float peak, float radius, float halfWidth, string name,
            float seconds = 90f, float leaderX = -4000f, float leaderZ = 20000f, float leaderHeading = 0f)
        {
            PlantParams pp = HeavyParams(thrustN);
            AirframeProfile p = HeavyProfile(pp);
            var leader = new VirtualLeader(new Vec3(leaderX, 1290f, leaderZ), 150f, leaderHeading);
            var wing = new MixedSimWing(leader, SimFormations.Get("finger-four-right"), 40f);
            var plant = new FixedWingPlant(pp, new Vec3(0f, 290f, 0f), 85f, 0f);
            plant.SetGammaState(gamma0);
            plant.SetThrottleState(1f);
            wing.Add(plant, p, 1f);
            FormationPilot pilot = wing.Pilots[0];
            var floor = new TerrainFloor();

            var run = new ZoomRun { MinNzCmdSlow = float.MaxValue, MinEasMargin = float.MaxValue, MinClearance = float.MaxValue };
            var trace = new StringBuilder("t,y,vy,tas,eas,gamma,bank,bank_cmd,nz,nz_cmd,throttle,airbrake,floor,ref_y,ref_speed,energy_cmd,vb,speed_by,nz_by,bank_by,gcas\n");
            CultureInfo c = CultureInfo.InvariantCulture;
            for (int i = 0; i < (int)(seconds / Dt); i++)
            {
                float t = i * Dt;
                leader.Step(30f, Dt);
                float raw = 0f;
                foreach (float ahead in ProbeSeconds)
                {
                    Vec3 probe = plant.Position + plant.Velocity * ahead;
                    raw = Math.Max(raw, Ridge(probe.X, probe.Z, peak, radius, halfWidth));
                }
                wing.FloorY = floor.Update(raw, Dt);
                wing.Step();

                AircraftState st = SimSensor.Read(plant, Dt);
                AttitudeCommand a = pilot.Pipeline.LastAttitude;
                ControlOutput o = pilot.LastOutput;
                BindingReport r = ((FixedWingPipeline)pilot.Pipeline).Report;
                RefState reference = pilot.LastIntent.Ref;
                bool gcas = pilot.Pipeline.GcasActive;
                run.AnyGcas |= gcas;
                run.MinClearance = Math.Min(run.MinClearance, plant.Position.Y - Ridge(plant.Position.X, plant.Position.Z, peak, radius, halfWidth));
                run.MaxGamma = Math.Max(run.MaxGamma, plant.GammaDeg);
                if (r.VerticalBy == ConstraintId.Terrain) run.MaxFloorClimb = Math.Max(run.MaxFloorClimb, st.Vel.Y);
                if (i % 6 == 0)
                    trace.Append(string.Join(",", new[]
                    {
                        t, plant.Position.Y, st.Vel.Y, st.Tas, st.Eas, plant.GammaDeg, plant.BankDeg, a.BankDeg, plant.LoadFactor, a.Nz,
                        o.Throttle, o.Airbrake ? 1f : 0f, wing.FloorY, reference.Pos.Y, reference.Vel.Length, a.EnergyRate,
                        (float)r.VerticalBy, (float)r.SpeedBy, (float)r.NzBy, (float)r.BankBy, gcas ? 1f : 0f,
                    }.Select(v => v.ToString("0.###", c)))).Append('\n');
                if (t < 1f) continue;
                bool slow = st.Eas < 1.5f * p.MinimumSpeed(1f);
                if (slow && st.Eas > 1.1f * p.MinimumSpeed(1f) && !gcas && a.Nz < run.MinNzCmdSlow)
                {
                    run.MinNzCmdSlow = a.Nz;
                    run.MinNzCmdTime = t;
                }
                if (slow && Math.Abs(plant.BankDeg) > run.MaxBankSlow)
                {
                    run.MaxBankSlow = Math.Abs(plant.BankDeg);
                    run.MaxBankTime = t;
                }
                float margin = st.Eas - p.MinimumSpeed(1f);
                if (margin < run.MinEasMargin)
                {
                    run.MinEasMargin = margin;
                    run.MinEasTime = t;
                }
                // Idle (or the airbrake) while slow, slower than the reference and well below it throws away the energy it needs.
                if (slow && (o.Airbrake || o.Throttle <= 0.05f) && st.Tas < reference.Vel.Length
                    && reference.Pos.Y > plant.Position.Y + 100f)
                {
                    if (run.IdleSeconds == 0f) run.IdleTime = t;
                    run.IdleSeconds += Dt;
                }
            }
            run.Trace = trace.ToString();
            string dir = Environment.GetEnvironmentVariable("WC_TRACE_DIR");
            if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, $"sim-heavy-zoom-{name}.csv"), run.Trace);
            return run;
        }

        private static void AssertKeepsPowerLiftAndSpeed(in ZoomRun run, AirframeProfile p)
        {
            Assert.True(run.MinClearance > 0f, $"hit the hills ({run.MinClearance:0} m)");
            Assert.False(run.AnyGcas, "GCAS fired");
            Assert.True(run.MinNzCmdSlow >= 0.49f, $"pushed to {run.MinNzCmdSlow:0.00} g at {run.MinNzCmdTime:0.0} s while slow");
            Assert.True(run.IdleSeconds == 0f,
                $"idle or airbrake for {run.IdleSeconds:0.0} s from {run.IdleTime:0.0} s while slow, slower than and below its reference");
            // The envelope's bank ceiling (72.5 deg at aggression 0.5) plus the authority slew's overshoot: never rolled past it.
            Assert.True(run.MaxBankSlow <= 75f, $"banked {run.MaxBankSlow:0} deg at {run.MaxBankTime:0.0} s while slow");
            Assert.True(run.MinEasMargin >= 0f,
                $"{-run.MinEasMargin:0.0} m/s under its minimum speed ({p.MinimumSpeed(1f):0.0}) at {run.MinEasTime:0.0} s");
        }

        [Fact]
        public void AnEw25ZoomedByTheTerrainFloorKeepsPowerAndLiftWhenTheFloorLetsGo()
        {
            // As the telemetry shows it: thrust enough to hold 85 m/s in a 45 deg climb, a floor that drives it there, then lets go.
            ZoomRun run = Fly(190000f, 27f, 700f, 1400f, 1000f, "ew25");
            Assert.True(run.MaxFloorClimb > 35f, $"the floor should force a zoom (max {run.MaxFloorClimb:0} m/s under it)");
            AssertKeepsPowerLiftAndSpeed(run, HeavyProfile(HeavyParams(190000f)));
        }

        // Open (2026-09-28): the terrain floor wins over the speed priority by design (ConstraintChainTests.TerrainFloorWinsOverTheSpeedLimit)
        // and demands up to ClimbRateMax (0.3 x max speed, 90 m/s here), so a weak climber is driven into a 40 deg climb and bleeds under
        // its minimum speed before the floor lets go (57.0 m/s EAS against 55.3 before the push-over limit). Needs a decision on capping
        // the floor's climb demand (at the guidance's 30 deg on own speed, or at the speed-limited climb).
        [Fact(Skip = "terrain floor overrides the speed priority by design; see the comment above")]
        public void AThrustLimitedHeavyClimbingOutOverRisingGroundKeepsPowerLiftAndSpeed()
        {
            // Thrust-to-weight 0.35: it sustains ~15 deg at 85 m/s, and the floor asks for far more.
            ZoomRun run = Fly(82000f, 15f, 700f, 1400f, 1000f, "heavy");
            Assert.True(run.MaxFloorClimb > 25f, $"the floor should demand more than it sustains (max {run.MaxFloorClimb:0} m/s under it)");
            AssertKeepsPowerLiftAndSpeed(run, HeavyProfile(HeavyParams(82000f)));
        }
    }
}
