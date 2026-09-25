using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class ProfileLibraryTests
    {
        private static readonly ProfileInputs Fs20 = new ProfileInputs { UnitName = "FS-20", MaxSpeed = 300f };

        [Fact]
        public void LaterLayersWin()
        {
            var lib = new ProfileLibrary();
            Assert.Empty(lib.AddLayer("shipped", "{ \"FS-20\": { \"TauCross\": 3 } }"));
            Assert.Empty(lib.AddLayer("user", "{ \"fs-20\": { \"TauCross\": 5 } }"));
            Assert.Equal(5f, lib.Build(Fs20, new List<string>()).TauCross);
        }

        [Fact]
        public void WildcardAppliesToEveryAirframeAndTheExactEntryBeatsIt()
        {
            var lib = new ProfileLibrary();
            lib.AddLayer("shipped", "{ \"*\": { \"RollGain\": 2, \"TauVel\": 1.2 }, \"FS-20\": { \"RollGain\": 3 } }");
            AirframeProfile p = lib.Build(Fs20, new List<string>());
            Assert.Equal(3f, p.RollGain);
            Assert.Equal(1.2f, p.TauVel);
            Assert.Equal(2f, lib.Build(new ProfileInputs { UnitName = "A-19" }, new List<string>()).RollGain);
        }

        [Fact]
        public void UnknownKeysAreReportedWithTheirSource()
        {
            var lib = new ProfileLibrary();
            lib.AddLayer("user", "{ \"FS-20\": { \"Bogus\": 1 } }");
            var rejected = new List<string>();
            lib.Build(Fs20, rejected);
            Assert.Equal(new[] { "user:FS-20.Bogus" }, rejected.ToArray());
        }

        [Fact]
        public void MalformedLayersAreReportedAndIgnored()
        {
            var lib = new ProfileLibrary();
            Assert.NotEmpty(lib.AddLayer("user", "not json"));
            Assert.NotEmpty(lib.AddLayer("user2", "{ \"FS-20\": 5 }"));
            Assert.Equal(AirframeProfile.Derive(Fs20).TauCross, lib.Build(Fs20, new List<string>()).TauCross);
        }
    }
}
