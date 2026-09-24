using Xunit;

namespace WingCommand.PureTests
{
    public class HotasBindingTests
    {
        [Fact]
        public void ParsesDeviceAndButton()
        {
            Assert.True(HotasBinding.TryParse(" T.16000M : 5 ", out HotasBinding b));
            Assert.Equal("T.16000M", b.Device);
            Assert.Equal(5, b.Button);
        }

        [Fact]
        public void AnyMatchesEveryDevice()
        {
            Assert.True(HotasBinding.TryParse("any:3", out HotasBinding b));
            Assert.Null(b.Device);
            Assert.True(b.Matches("Saitek X56 Throttle"));
            Assert.True(b.Matches(""));
        }

        [Fact]
        public void NameFragmentsMatchCaseInsensitively()
        {
            Assert.True(HotasBinding.TryParse("x56 throttle:12", out HotasBinding b));
            Assert.True(b.Matches("Saitek X56 Throttle"));
            Assert.False(b.Matches("Saitek X56 Stick"));
            Assert.False(b.Matches(null));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData(":")]
        [InlineData("x:0")]
        [InlineData("x:-2")]
        [InlineData("x:five")]
        [InlineData(":4")]
        public void MalformedTextIsRejected(string text) => Assert.False(HotasBinding.TryParse(text, out _));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyIsUnbound(string text)
        {
            Assert.False(HotasBinding.TryParse(text, out _));
            Assert.True(HotasBinding.IsUnbound(text));
        }

        [Fact]
        public void ADeviceNameWithAColonKeepsItsFirstPart()
        {
            Assert.True(HotasBinding.TryParse("VKB: Gladiator:7", out HotasBinding b));
            Assert.Equal("VKB: Gladiator", b.Device);
            Assert.Equal(7, b.Button);
        }
    }
}
