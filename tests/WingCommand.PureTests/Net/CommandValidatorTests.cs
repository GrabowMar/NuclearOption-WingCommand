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
        [InlineData(false, (int)CommandKind.Task, 1f, 1000f, "wing")]
        [InlineData(true, 0, 1f, 1000f, "command")]
        [InlineData(true, 200, 1f, 1000f, "command")]
        [InlineData(true, (int)CommandKind.Task, 1f, 60000f, "map")]
        [InlineData(true, (int)CommandKind.Task, 1f, float.NaN, "map")]
        [InlineData(true, (int)CommandKind.Task, 1f, float.PositiveInfinity, "map")]
        public void EachRefusalHasItsReason(bool owns, int kind, float unused, float x, string word)
        {
            WcCommand c = Move(1, x);
            c.Kind = (CommandKind)kind;
            var s = new SenderState();
            Assert.False(CommandValidator.Check(c, Context(owns), s, unused, out string reason));
            Assert.Contains(word, reason);
            Assert.Equal(0u, s.LastSeq);
            Assert.Equal(CommandValidator.Burst, s.Tokens, 3);
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
