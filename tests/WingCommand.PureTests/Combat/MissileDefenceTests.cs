using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class MissileDefenceTests
    {
        private const float Dt = 0.1f;
        private static readonly Vec3 East = new Vec3(250f, 0f, 0f);
        private static readonly AircraftState Me = TestStates.Flying(new Vec3(0f, 3000f, 0f), East);
        private static readonly RefState Home = new RefState(new Vec3(0f, 3000f, 0f), East, Vec3.Zero);

        /// <summary>A missile north of the aircraft, <paramref name="range"/> away, closing at <paramref name="closing"/>.</summary>
        private static MissileThreat From(float range, float closing, MissileSeeker seeker = MissileSeeker.Radar) => new MissileThreat
        {
            Present = true, Pos = new Vec3(0f, 3000f, range), Vel = new Vec3(0f, 0f, -closing) + East, Seeker = seeker,
        };

        private static DefenceCommand Run(MissileDefence d, in MissileThreat t, float seconds, float precision = 1f,
            AircraftState? s = null, RefState? home = null)
        {
            DefenceCommand c = default;
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) c = d.Step(t, s ?? Me, home ?? Home, precision, Dt);
            return c;
        }

        [Fact]
        public void TheReactionDependsOnPrecision()
        {
            Assert.Equal(1f, MissileDefence.ReactionFor(1f), 3);
            Assert.Equal(4f, MissileDefence.ReactionFor(0f), 3);
            var d = new MissileDefence();
            Assert.False(Run(d, From(8000f, 600f), 0.9f).Active);
            Assert.True(Run(d, From(8000f, 600f), 0.2f).Active);
        }

        [Fact]
        public void EarlyWarningReactsSooner()
        {
            var d = new MissileDefence();
            DefenceCommand c = default;
            for (int i = 0; i < 26; i++) c = d.Step(From(8000f, 600f), Me, Home, 0f, Dt, reactionDelta: -1.5f);   // 2.6 s
            Assert.True(c.Active);                                             // 4 s − 1.5 s
            var never = new MissileDefence();
            for (int i = 0; i < 5; i++) c = never.Step(From(8000f, 600f), Me, Home, 1f, Dt, reactionDelta: -5f);
            Assert.True(c.Active);                                             // never below 0
        }

        [Fact]
        public void BreakTurnReactsAtOnceInsideItsRange()
        {
            var inside = new MissileDefence();
            Assert.True(inside.Step(From(2000f, 600f), Me, Home, 0f, Dt, breakRange: 2500f).Active);
            var outside = new MissileDefence();
            Assert.False(outside.Step(From(3000f, 600f), Me, Home, 0f, Dt, breakRange: 2500f).Active);
        }

        [Fact]
        public void AnInfraredMissileMeansIdleAndFlaresOnTheHomeReference()
        {
            DefenceCommand c = Run(new MissileDefence(), From(3000f, 500f, MissileSeeker.Infrared), 1.5f);
            Assert.True(c.Active && c.Idle && c.Countermeasures && !c.Full);
            Assert.Equal(Home.Pos.X, c.Ref.Pos.X, 3);
            Assert.Equal(Home.Pos.Z, c.Ref.Pos.Z, 3);
        }

        [Fact]
        public void ARadarMissileIsNotchedAndTheSideIsKept()
        {
            var d = new MissileDefence();
            DefenceCommand c = Run(d, From(3000f, 600f), 1.5f);   // impact 5 s: the full notch
            Assert.True(c.Active && c.Full && !c.Idle);
            // Perpendicular to the line of sight (north-south), on the side nearer the heading (east).
            Assert.True(c.Ref.Vel.X > 0.9f * c.Ref.Vel.Length, $"notch velocity {c.Ref.Vel}");
            Assert.True(c.Ref.Pos.X > 900f && Math.Abs(c.Ref.Pos.Z) < 1f, $"notch point {c.Ref.Pos}");
            Assert.True(c.Ref.Pos.Y < Me.Pos.Y, "the notch descends");
            // The formation reference moving across (west) never flips an established notch.
            var west = new RefState(new Vec3(-2000f, 3000f, 0f), East, Vec3.Zero);
            c = Run(d, From(3000f, 600f), 0.5f, home: west);
            Assert.True(c.Ref.Vel.X > 0f);
        }

        [Fact]
        public void FarAwayTheNotchIsBlendedCloseItIsFull()
        {
            DefenceCommand far = Run(new MissileDefence(), From(8000f, 600f), 1.5f);   // impact ≈ 13 s
            Assert.InRange(far.Ref.Pos.X, 250f, 350f);                                // 30 % of 1000 m
            DefenceCommand near = Run(new MissileDefence(), From(3000f, 600f), 1.5f);  // impact 5 s
            Assert.InRange(near.Ref.Pos.X, 950f, 1050f);
        }

        [Fact]
        public void ChaffOnlyInsideEightSecondsAndAlignedWithTheNotch()
        {
            Assert.False(Run(new MissileDefence(), From(8000f, 600f), 1.5f).Countermeasures);   // 13 s: too early
            Assert.True(Run(new MissileDefence(), From(3000f, 600f), 1.5f).Countermeasures);    // 5 s, flying east = the notch
            AircraftState north = TestStates.Flying(new Vec3(0f, 3000f, 0f), new Vec3(0f, 0f, 250f));
            Assert.False(Run(new MissileDefence(), From(3000f, 600f), 1.5f, s: north).Countermeasures);   // not yet in the notch
        }

        [Fact]
        public void InsideTwoSecondsItPullsUp()
        {
            DefenceCommand c = Run(new MissileDefence(), From(1000f, 600f), 1.5f);
            Assert.True(c.Ref.Pos.Y >= Me.Pos.Y + 900f, $"reference {c.Ref.Pos.Y - Me.Pos.Y:0} m above");
        }

        [Fact]
        public void AThreatGoneForASecondEndsTheDefence()
        {
            var d = new MissileDefence();
            Run(d, From(3000f, 600f), 1.5f);
            DefenceCommand c = Run(d, default, 0.5f);
            Assert.True(c.Active);
            Assert.False(c.Countermeasures);
            Assert.True(Run(d, From(3000f, 600f), 0.1f).Active, "back within the second: no new reaction delay");
            Run(d, default, 0.5f);
            Assert.False(Run(d, default, 0.6f).Active);
            Assert.Equal(0, d.Side);
        }

        [Fact]
        public void ANewMissileChoosesItsOwnSide()
        {
            // Review M5c I1: a second missile from the south kept the first's side and turned the member west (180°).
            var d = new MissileDefence();
            MissileThreat a = From(3000f, 600f);
            a.Id = 1;
            Run(d, a, 1.5f);
            var b = new MissileThreat { Present = true, Id = 2, Pos = new Vec3(0f, 3000f, -3000f), Vel = new Vec3(0f, 0f, 600f) + East, Seeker = MissileSeeker.Radar };
            DefenceCommand c = Run(d, b, 0.1f);
            Assert.True(c.Active, "no new reaction delay");
            Assert.True(c.Ref.Vel.X > 0f, $"notch velocity {c.Ref.Vel}");
        }

        [Fact]
        public void EndForgetsTheDefence()
        {
            // Review M5c I3: a member taken back for recovery or combat starts afresh next time.
            var d = new MissileDefence();
            Run(d, From(3000f, 600f), 1.5f);
            d.End();
            Assert.False(d.Active);
            Assert.Equal(0, d.Side);
            Assert.False(Run(d, From(3000f, 600f), 0.5f).Active, "the reaction applies again");
        }
    }
}
