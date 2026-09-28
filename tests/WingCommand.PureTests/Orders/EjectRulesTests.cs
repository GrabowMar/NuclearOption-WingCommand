using Xunit;

namespace WingCommand.PureTests
{
    public class EjectRulesTests
    {
        private static EjectFacts Flying() => new EjectFacts { Host = true, RadarAlt = 1500f, Speed = 200f, Phase = GroundPhase.Done };

        [Fact]
        public void AMemberFlyingInFormationMayEject() => Assert.Null(EjectRules.Refusal(Flying()));

        [Fact]
        public void EachRefusalHasItsReason()
        {
            EjectFacts f = Flying();
            f.Host = false;
            Assert.Equal("host only", EjectRules.Refusal(f));
            f = Flying();
            f.Released = true;
            Assert.Equal("not flying for the wing", EjectRules.Refusal(f));
            f = Flying();
            f.PlayerFlown = true;
            Assert.Equal("not flying for the wing", EjectRules.Refusal(f));
            f = Flying();
            f.Lost = true;
            Assert.Equal("aircraft already lost", EjectRules.Refusal(f));
            f = Flying();
            f.PilotDead = true;
            Assert.Equal("pilot dead", EjectRules.Refusal(f));
            f = Flying();
            f.Ejected = true;
            Assert.Equal("already ejected", EjectRules.Refusal(f));
            f = Flying();
            f.Supervised = true;
            f.Phase = GroundPhase.TaxiOut;
            Assert.Equal("on the ground, send it home", EjectRules.Refusal(f));
            f = Flying();
            f.Landing = true;
            Assert.Equal("landing", EjectRules.Refusal(f));
            f = Flying();
            f.SettledDown = true;
            Assert.Equal("on the ground", EjectRules.Refusal(f));
        }

        [Theory]
        [InlineData((int)GroundPhase.ClimbOut)]
        [InlineData((int)GroundPhase.LiftOff)]
        public void AClimbOutIsStillTheGroundCrewsJob(int phase)
        {
            EjectFacts f = Flying();
            f.Supervised = true;
            f.Phase = (GroundPhase)phase;
            Assert.Equal("still climbing out", EjectRules.Refusal(f));
        }

        [Fact]
        public void TheFirstMatchWins()
        {
            EjectFacts f = Flying();
            f.Lost = true;
            f.Ejected = true;
            f.Landing = true;
            Assert.Equal("aircraft already lost", EjectRules.Refusal(f));
        }

        [Theory]
        [InlineData(4.9f, 2.4f, "on the ground")]
        [InlineData(20f, 1.9f, "hovering, land it or send it home")]   // a stopped airframe goes back to the game's stock
        [InlineData(3f, 30f, null)]
        [InlineData(4.9f, 2.5f, null)]
        public void LandedAndHoveringFollowTheGamesOwnTest(float radarAlt, float speed, string expected)
        {
            EjectFacts f = Flying();
            f.RadarAlt = radarAlt;
            f.Speed = speed;
            Assert.Equal(expected, EjectRules.Refusal(f));
        }

        [Fact]
        public void EveryCaseTheEjectGuardBlocksIsRefused()
        {
            // Guard parity (eject.md): if the native guard would swallow the call, the order refuses it with a reason first.
            for (int bits = 0; bits < 16; bits++)
            {
                bool disabled = (bits & 1) != 0, released = (bits & 2) != 0, supervised = (bits & 4) != 0, landing = (bits & 8) != 0;
                EjectFacts f = Flying();
                f.Lost = disabled;
                f.Released = released;
                f.Supervised = supervised;
                f.Phase = GroundPhase.Roll;
                f.Landing = landing;
                if (EjectRules.Guarded(disabled, released, supervised, landing)) Assert.NotNull(EjectRules.Refusal(f));
            }
            Assert.True(EjectRules.Guarded(false, false, true, false));
            Assert.False(EjectRules.Guarded(true, false, true, true));
            Assert.False(EjectRules.Guarded(false, false, false, false));
        }
    }
}
