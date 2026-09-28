using Xunit;

namespace WingCommand.PureTests
{
    public class BraTests
    {
        private static readonly Vec3 Origin = new Vec3(0f, 0f, 0f);

        [Theory]
        [InlineData(0f, 10000f, "000")]
        [InlineData(10000f, 0f, "090")]
        [InlineData(0f, -10000f, "180")]
        [InlineData(-10000f, 0f, "270")]
        public void BearingIsTrueFromTheListener(float x, float z, string bearing) =>
            Assert.StartsWith("BRA " + bearing + ",", Bra.Format(Origin, new Vec3(x, 3000f, z), Vec3.Zero, true));

        [Fact]
        public void ImperialSaysMilesAndThousandsOfFeet() =>
            Assert.Equal("BRA 000, 10, 10 thousand", Bra.Format(Origin, new Vec3(0f, 3048f, 18520f), Vec3.Zero, true));

        [Fact]
        public void MetricSaysKilometresAndMetres() =>
            Assert.Equal("BRA 000, 19 kilometres, 3000 metres", Bra.Format(Origin, new Vec3(0f, 3000f, 18520f), Vec3.Zero, false));

        [Theory]
        [InlineData(0f, -200f, "hot")]        // flying straight at the listener (target north of it, flying south)
        [InlineData(200f, -150f, "flanking")]
        [InlineData(200f, 0f, "beaming")]
        [InlineData(0f, 200f, "cold")]
        public void AspectBands(float vx, float vz, string aspect) =>
            Assert.Equal(aspect, Bra.Aspect(Origin, new Vec3(0f, 3000f, 20000f), new Vec3(vx, 0f, vz)));

        [Fact]
        public void ASlowTargetHasNoAspect() =>
            Assert.Equal("", Bra.Aspect(Origin, new Vec3(0f, 0f, 20000f), new Vec3(5f, 0f, 0f)));

        [Fact]
        public void TheAspectIsAppendedWhenThereIsOne() =>
            Assert.EndsWith(", hot", Bra.Format(Origin, new Vec3(0f, 3048f, 18520f), new Vec3(0f, 0f, -200f), true));
    }
}
