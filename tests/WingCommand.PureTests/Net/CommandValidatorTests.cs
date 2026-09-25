using Xunit;

namespace WingCommand.PureTests
{
    public class CommandValidatorTests
    {
        private static CommandContext Context(bool ownsWing = true) => new CommandContext
        {
            OwnsWing = ownsWing, MapHalfSize = 50000f, Knows = id => id == 7u,
        };

        private static WcCommand Move(uint seq, float x = 1000f, float z = -2000f, float alt = float.NaN) => new WcCommand
        {
            Seq = seq, Kind = CommandKind.Task, Waypoints = new[] { new WcWaypoint { X = x, Z = z, Alt = alt } },
        };

        [Fact]
        public void AValidCommandIsAcceptedAndAdvancesTheSender()
        {
            var s = new SenderState();
            Assert.True(CommandValidator.Check(Move(1), Context(), s, 0f, out string reason), reason);
            Assert.Equal(1u, s.LastSeq);
            Assert.Equal(CommandValidator.Burst - 1f, s.Tokens, 3);
        }

        [Theory]
        [InlineData(0, 1000f, "command")]
        [InlineData(200, 1000f, "command")]
        [InlineData((int)CommandKind.Task, 60000f, "map")]
        [InlineData((int)CommandKind.Task, float.NaN, "map")]
        [InlineData((int)CommandKind.Task, float.PositiveInfinity, "map")]
        public void EachRefusalHasItsReasonAndCostsAToken(int kind, float x, string word)
        {
            // Review M6a I3 (ruling): every command past the ownership check costs a token, refused or not.
            WcCommand c = Move(1, x);
            c.Kind = (CommandKind)kind;
            var s = new SenderState();
            Assert.False(CommandValidator.Check(c, Context(), s, 0f, out string reason));
            Assert.Contains(word, reason);
            Assert.Equal(0u, s.LastSeq);
            Assert.Equal(CommandValidator.Burst - 1f, s.Tokens, 3);
        }

        [Fact]
        public void ASenderWithoutAWingIsRefusedForFree()
        {
            var s = new SenderState();
            Assert.False(CommandValidator.Check(Move(1), Context(false), s, 0f, out string reason));
            Assert.Contains("wing", reason);
            Assert.Equal(CommandValidator.Burst, s.Tokens, 3);
        }

        [Fact]
        public void RefusalsCannotBeSentFasterThanTheBucket()
        {
            var s = new SenderState();
            for (int i = 0; i < (int)CommandValidator.Burst; i++) CommandValidator.Check(Move(1, float.NaN), Context(), s, 0f, out _);
            Assert.False(CommandValidator.Check(Move(1), Context(), s, 0f, out string reason));
            Assert.Contains("fast", reason);
        }

        [Theory]
        [InlineData(-2000f, float.NaN, true)]
        [InlineData(-2000f, 1e30f, false)]                 // review M6a I2: altitude beyond the ceiling
        [InlineData(float.NegativeInfinity, float.NaN, false)]
        [InlineData(float.NaN, float.NaN, false)]
        [InlineData(-2000f, float.NegativeInfinity, false)]
        public void WaypointsAreFiniteOnTheMapAndUnderTheCeiling(float z, float alt, bool ok) =>
            Assert.Equal(ok, CommandValidator.Check(Move(1, 1000f, z, alt), Context(), new SenderState(), 0f, out _));

        [Fact]
        public void ArgumentsMustBeFinite()
        {
            // Review M6a I2: a NaN spacing would reach the slot solver.
            var c = new WcCommand { Seq = 1, Kind = CommandKind.Spacing, Args = new[] { float.NaN } };
            Assert.False(CommandValidator.Check(c, Context(), new SenderState(), 0f, out string reason));
            Assert.Contains("argument", reason);
            c.Args = new[] { 1.5f };
            Assert.True(CommandValidator.Check(c, Context(), new SenderState(), 0f, out _));
        }

        [Fact]
        public void AReplayedOrReorderedCommandIsRefused()
        {
            var s = new SenderState();
            Assert.True(CommandValidator.Check(Move(5), Context(), s, 0f, out _));
            Assert.False(CommandValidator.Check(Move(5), Context(), s, 0f, out string replay));
            Assert.Contains("sequence", replay);
            Assert.False(CommandValidator.Check(Move(4), Context(), s, 0f, out _));
            Assert.True(CommandValidator.Check(Move(9), Context(), s, 0f, out _));
        }

        [Fact]
        public void TheTokenBucketLimitsBurstsAndRefills()
        {
            var s = new SenderState();
            uint seq = 0;
            for (int i = 0; i < (int)CommandValidator.Burst; i++) Assert.True(CommandValidator.Check(Move(++seq), Context(), s, 0f, out _));
            Assert.False(CommandValidator.Check(Move(++seq), Context(), s, 0f, out string reason));
            Assert.Contains("fast", reason);
            Assert.True(CommandValidator.Check(Move(++seq), Context(), s, 0.1f, out _), "one token back after 0.1 s at 10/s");
        }

        [Fact]
        public void AnAltitudeMayBeLeftOutButNotBeInfinite()
        {
            Assert.True(CommandValidator.Check(Move(1, alt: float.NaN), Context(), new SenderState(), 0f, out _));
            Assert.False(CommandValidator.Check(Move(1, alt: float.PositiveInfinity), Context(), new SenderState(), 0f, out _));
        }

        [Fact]
        public void OnlyUnitsTheHostKnowsMayBeNamed()
        {
            var c = new WcCommand { Seq = 1, Kind = CommandKind.Attack, Units = new[] { 7u } };
            Assert.True(CommandValidator.Check(c, Context(), new SenderState(), 0f, out _));
            c.Units = new[] { 7u, 8u };
            Assert.False(CommandValidator.Check(c, Context(), new SenderState(), 0f, out string reason));
            Assert.Contains("unit", reason);
        }
    }
}
