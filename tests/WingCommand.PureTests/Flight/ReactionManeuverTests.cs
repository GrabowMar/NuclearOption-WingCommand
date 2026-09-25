using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class ReactionManeuverTests
    {
        private static readonly AirframeProfile Fighter = new AirframeProfile();   // Vmin(1) 66 m/s, max 300 m/s

        /// <summary>Level at <paramref name="alt"/>, flying north at <paramref name="speed"/> (sea-level EAS = TAS).</summary>
        private static AircraftState Flying(float speed = 200f, float alt = 2000f, float headingDeg = 0f)
        {
            Vec3 v = Vec3.FromHeading(headingDeg, speed);
            return new AircraftState
            {
                Pos = new Vec3(0f, alt, 0f), Vel = v, Fwd = v.Normalized, Up = Vec3.Up, Tas = speed,
                Qbar = 0.5f * Isa.SeaLevelDensity * speed * speed, RadarAlt = alt,
            };
        }

        private static RefState Home(in AircraftState s) => new RefState(s.Pos + new Vec3(0f, 0f, -200f), s.Vel, Vec3.Zero);

        private static float Turn(Vec3 from, Vec3 to)
        {
            float d = Vec3.HeadingDeg(to) - Vec3.HeadingDeg(from);
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            return d;
        }

        [Theory]
        [InlineData((int)ReactionKind.BreakLeft)]
        [InlineData((int)ReactionKind.BreakRight)]
        [InlineData((int)ReactionKind.Split)]
        public void BreaksAndSplitRefuseBelowTheCatalogsHundredTwentyMetres(int kind)
        {
            AircraftState s = Flying();
            Assert.Equal("too low (below 120 m)", ReactionManeuver.Refusal((ReactionKind)kind, s, 119f, Fighter, false));
            Assert.Null(ReactionManeuver.Refusal((ReactionKind)kind, s, 121f, Fighter, false));
        }

        [Fact]
        public void BeamRefusesBelowEightyMetresAndWithoutAThreat()
        {
            AircraftState s = Flying();
            Assert.Equal("no threat to beam", ReactionManeuver.Refusal(ReactionKind.Beam, s, 1000f, Fighter, false));
            Assert.Equal("too low (below 80 m)", ReactionManeuver.Refusal(ReactionKind.Beam, s, 79f, Fighter, true));
            Assert.Null(ReactionManeuver.Refusal(ReactionKind.Beam, s, 81f, Fighter, true));
        }

        [Fact]
        public void PullUpHasNoHeightGateButRefusesBelowTheProtectedSpeed()
        {
            Assert.Null(ReactionManeuver.Refusal(ReactionKind.PullUp, Flying(200f, 30f), 30f, Fighter, false));
            Assert.Equal("too slow", ReactionManeuver.Refusal(ReactionKind.PullUp, Flying(90f), 2000f, Fighter, false));
        }

        [Fact]
        public void EntrySpeedIsNeverBelowOnePointFiveLoadedMinimum()
        {
            // The catalog's 0.20 × max (60 m/s) is below the stall: the speed-protect floor (1.5 × Vmin(1) = 99 m/s EAS) wins.
            float floor = ConstraintChain.SpeedProtectFactor * Fighter.MinimumSpeed(1f);
            Assert.Equal("too slow", ReactionManeuver.Refusal(ReactionKind.BreakLeft, Flying(floor - 1f), 2000f, Fighter, false));
            Assert.Null(ReactionManeuver.Refusal(ReactionKind.BreakLeft, Flying(floor + 1f), 2000f, Fighter, false));
        }

        [Theory]
        [InlineData((int)ReactionKind.BreakLeft, -90f)]
        [InlineData((int)ReactionKind.BreakRight, 90f)]
        public void ABreakCommandsAVelocityNinetyDegreesOffAtHeldSpeed(int kind, float turn)
        {
            AircraftState s = Flying(210f);
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = (ReactionKind)kind }, s, Home(s));
            RefState reference = r.Step(s, Home(s), 0.02f, out bool done);
            Assert.False(done);
            Assert.Equal(turn, Turn(s.Vel, reference.Vel), 1);
            Assert.Equal(210f, reference.Vel.Length, 1);
            Assert.Equal(2000f, reference.Pos.Y, 1);
            Assert.Equal(0f, reference.Vel.Y, 3);
        }

        [Theory]
        [InlineData(1, 60f)]
        [InlineData(-1, -60f)]
        public void SplitTurnsSixtyDegreesAwayOnItsSide(int side, float turn)
        {
            AircraftState s = Flying(200f, 2000f, 30f);
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = ReactionKind.Split, Side = side }, s, Home(s));
            Assert.Equal(turn, Turn(s.Vel, r.Step(s, Home(s), 0.02f, out _).Vel), 1);
        }

        [Fact]
        public void SplitSideIsAwayFromTheScopeCentroidThenTheLeadThenSeatParity()
        {
            Vec3 lead = new Vec3(0f, 2000f, 0f), north = Vec3.Forward;
            // A pair: east of the pair's middle turns right (+1), west turns left.
            Assert.Equal(1, ReactionManeuver.SplitSide(new Vec3(100f, 2000f, -50f), new Vec3(0f, 2000f, -50f), true, lead, north, 0));
            Assert.Equal(-1, ReactionManeuver.SplitSide(new Vec3(-100f, 2000f, -50f), new Vec3(0f, 2000f, -50f), true, lead, north, 1));
            // Alone (its own position is the centroid): away from the lead.
            Assert.Equal(-1, ReactionManeuver.SplitSide(new Vec3(-60f, 2000f, -100f), new Vec3(-60f, 2000f, -100f), true, lead, north, 0));
            // Straight behind the lead: seat parity.
            Assert.Equal(1, ReactionManeuver.SplitSide(new Vec3(0f, 2000f, -100f), Vec3.Zero, false, lead, north, 0));
            Assert.Equal(-1, ReactionManeuver.SplitSide(new Vec3(0f, 2000f, -100f), Vec3.Zero, false, lead, north, 1));
        }

        [Fact]
        public void BeamIsPerpendicularToTheLineOfSightOnTheSideNearerTheHeading()
        {
            // Heading 350°, threat at 045°: the beam headings are 135° or 315°; 315° is nearer the heading.
            AircraftState s = Flying(200f, 2000f, 350f);
            Vec3 threat = s.Pos + Vec3.FromHeading(45f, 20000f);
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = ReactionKind.Beam, HasThreat = true, Threat = threat }, s, Home(s));
            float heading = Vec3.HeadingDeg(r.Step(s, Home(s), 0.02f, out _).Vel);
            Assert.Equal(315f, heading, 1);
        }

        [Theory]
        [InlineData(3f)]
        [InlineData(-3f)]
        public void ABeamNearTheNoseTakesTheNearerSideWhateverTheSlotError(float slotErrorEast)
        {
            // Review R3b I1: home is the member's own slot a few metres away; its direction must not pick the side
            // (a bandit 30° right: 300° is a 60° turn, 120° would cross the threat's bearing).
            AircraftState s = Flying();
            var home = new RefState(s.Pos + new Vec3(slotErrorEast, 0f, 0f), s.Vel, Vec3.Zero);
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = ReactionKind.Beam, HasThreat = true, Threat = s.Pos + Vec3.FromHeading(30f, 30000f) }, s, home);
            Assert.Equal(300f, Vec3.HeadingDeg(r.Step(s, home, 0.02f, out _).Vel), 1);
        }

        [Fact]
        public void PullUpClimbsToEntryPlusHeightThenEnds()
        {
            AircraftState s = Flying();
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = ReactionKind.PullUp }, s, Home(s));
            RefState reference = r.Step(s, Home(s), 0.02f, out bool done);
            Assert.False(done);
            Assert.Equal(2000f + ReactionManeuver.PullUpHeight, reference.Pos.Y, 1);
            Assert.Equal(0f, Turn(s.Vel, reference.Vel), 1);
            s.Pos = new Vec3(0f, 2000f + ReactionManeuver.PullUpHeight - ReactionManeuver.PullUpDone + 1f, 0f);
            r.Step(s, Home(s), 0.02f, out done);
            Assert.True(done, "within 50 m of the goal the pull-up is over");
        }

        [Fact]
        public void EndsAfterItsDurationNeverAboveTheCap()
        {
            AircraftState s = Flying();
            var r = new ReactionManeuver();
            r.Begin(new ReactionOrder { Kind = ReactionKind.BreakRight }, s, Home(s));
            bool done = false;
            float t = 0f;
            for (; t < 60f && !done; t += 0.5f) r.Step(s, Home(s), 0.5f, out done);
            Assert.Equal(ReactionManeuver.BreakSeconds, t, 1);

            float was = ReactionManeuver.BeamSeconds;
            try
            {
                ReactionManeuver.BeamSeconds = 1000f;
                r.Begin(new ReactionOrder { Kind = ReactionKind.Beam, HasThreat = true, Threat = s.Pos + Vec3.FromHeading(90f, 9000f) }, s, Home(s));
                done = false;
                for (t = 0f; t < 1000f && !done; t += 1f) r.Step(s, Home(s), 1f, out done);
                Assert.Equal(ReactionManeuver.MaxSeconds, t, 1);
            }
            finally
            {
                ReactionManeuver.BeamSeconds = was;
            }
            r.End();
            Assert.False(r.Active);
            Assert.Equal(ReactionKind.Beam, r.Kind);
        }

        [Fact]
        public void ReferenceStaysFiniteWithNoHorizontalVelocity()
        {
            var s = new AircraftState { Pos = new Vec3(5f, 800f, 5f), Fwd = Vec3.Up, Up = Vec3.Forward };
            var r = new ReactionManeuver();
            foreach (ReactionKind k in Enum.GetValues(typeof(ReactionKind)))
            {
                r.Begin(new ReactionOrder { Kind = k, Side = 1, HasThreat = true, Threat = s.Pos }, s, new RefState(s.Pos, Vec3.Zero, Vec3.Zero));
                RefState reference = r.Step(s, new RefState(s.Pos, Vec3.Zero, Vec3.Zero), 0.02f, out _);
                Assert.False(float.IsNaN(reference.Vel.X) || float.IsNaN(reference.Vel.Z) || float.IsNaN(reference.Pos.Y), k.ToString());
            }
        }

        [Fact]
        public void EachKindHasItsLabelAndWords()
        {
            Assert.Equal("BRK L", ReactionManeuver.Label(ReactionKind.BreakLeft));
            Assert.Equal("BEAM", ReactionManeuver.Label(ReactionKind.Beam));
            Assert.Equal("breaking right", ReactionManeuver.Words(ReactionKind.BreakRight));
            Assert.Equal("pulling up", ReactionManeuver.Words(ReactionKind.PullUp));
        }
    }
}
