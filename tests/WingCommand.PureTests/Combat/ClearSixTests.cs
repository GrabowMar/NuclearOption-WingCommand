using Xunit;

namespace WingCommand.PureTests
{
    public class ClearSixTests
    {
        private static readonly Vec3 North = new Vec3(0f, 0f, 1f);

        [Theory]
        [InlineData(0f, -3000f, true)]      // dead six
        [InlineData(2000f, -2000f, true)]   // 135°: in the rear quarter
        [InlineData(3000f, 0f, false)]      // abeam
        [InlineData(0f, 3000f, false)]      // ahead
        [InlineData(0f, -9000f, false)]     // behind but beyond the range
        public void OnlyAircraftInTheRearQuarterWithinRangeAreOnYourSix(float east, float north, bool onSix) =>
            Assert.Equal(onSix, ClearSix.OnSix(new Vec3(east, 0f, north), North, 6000f));
    }
}
