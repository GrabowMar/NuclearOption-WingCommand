using Xunit;

namespace WingCommand.PureTests
{
    public class MemberDoctrinesTests
    {
        [Fact]
        public void AnAircraftWithNothingOfItsOwnFliesItsElementsDoctrine()
        {
            var m = new MemberDoctrines();
            Assert.Equal(WingDoctrine.Escort, m.Resolve(7u, WingDoctrine.Escort));
            Assert.False(m.Has(7u, DoctrineAxis.Targets));
        }

        [Fact]
        public void AnOverrideKeepsItsAxisAndFollowsLaterElementChangesOnTheOthers()
        {
            var m = new MemberDoctrines();
            Assert.True(m.SetOverride(7u, DoctrineAxis.Radar, (byte)RadarPolicy.Off));
            WingDoctrine d = m.Resolve(7u, WingDoctrine.Sweep);
            Assert.Equal(RadarPolicy.Off, d.Radar);
            Assert.Equal(TargetPolicy.Both, d.Targets);
            d = m.Resolve(7u, WingDoctrine.Reserve);
            Assert.Equal(RadarPolicy.Off, d.Radar);
            Assert.Equal(TargetPolicy.Hold, d.Targets);
            Assert.True(m.Has(7u, DoctrineAxis.Radar));
            Assert.False(m.Has(7u, DoctrineAxis.Weapons));
            Assert.Equal(WingDoctrine.Reserve, m.Resolve(8u, WingDoctrine.Reserve));
        }

        [Fact]
        public void SettingABaseReplacesTheElementAndClearsTheOverrides()
        {
            var m = new MemberDoctrines();
            m.SetOverride(7u, DoctrineAxis.Targets, (byte)TargetPolicy.Air);
            Assert.True(m.SetBase(7u, WingDoctrine.Escort));
            Assert.Equal(WingDoctrine.Escort, m.Resolve(7u, WingDoctrine.Sweep));
            Assert.True(m.Has(7u, DoctrineAxis.Targets), "a member with its own profile keeps every axis");
            m.SetOverride(7u, DoctrineAxis.Weapons, (byte)WeaponsPolicy.Guns);
            Assert.Equal(WeaponsPolicy.Guns, m.Resolve(7u, WingDoctrine.Sweep).Weapons);
            Assert.Equal(TargetPolicy.Cover, m.Resolve(7u, WingDoctrine.Sweep).Targets);
        }

        [Theory]
        [InlineData((int)DoctrineAxis.Guard)]
        [InlineData((int)DoctrineAxis.Response)]
        [InlineData((int)DoctrineAxis.Interval)]
        [InlineData((int)DoctrineAxis.Spread)]
        public void ElementWideAxesCannotDifferPerAircraft(int axis)
        {
            var m = new MemberDoctrines();
            Assert.False(MemberDoctrines.PerAircraft((DoctrineAxis)axis));
            Assert.False(m.SetOverride(7u, (DoctrineAxis)axis, 1));
            Assert.Equal(WingDoctrine.Reserve, m.Resolve(7u, WingDoctrine.Reserve));
        }

        [Fact]
        public void ForgetAndClearDropWhatAnAircraftHad()
        {
            var m = new MemberDoctrines();
            m.SetOverride(7u, DoctrineAxis.Radar, (byte)RadarPolicy.Off);
            m.SetOverride(8u, DoctrineAxis.Radar, (byte)RadarPolicy.Off);
            m.Forget(7u);
            Assert.Equal(RadarPolicy.On, m.Resolve(7u, WingDoctrine.Reserve).Radar);
            Assert.Equal(RadarPolicy.Off, m.Resolve(8u, WingDoctrine.Reserve).Radar);
            m.Clear();
            Assert.Equal(RadarPolicy.On, m.Resolve(8u, WingDoctrine.Reserve).Radar);
        }

        [Fact]
        public void ANinthAircraftAndIdZeroAreRefused()
        {
            var m = new MemberDoctrines();
            for (uint id = 1; id <= MemberDoctrines.Max; id++) Assert.True(m.SetOverride(id, DoctrineAxis.Targets, 1));
            Assert.False(m.SetOverride(100u, DoctrineAxis.Targets, 1));
            Assert.False(m.SetBase(100u, WingDoctrine.Sweep));
            Assert.True(m.SetOverride(3u, DoctrineAxis.Reach, 1), "an aircraft already held changes freely");
            m.Forget(3u);
            Assert.True(m.SetOverride(100u, DoctrineAxis.Targets, 1), "a forgotten slot is free again");
            Assert.False(new MemberDoctrines().SetOverride(0u, DoctrineAxis.Targets, 1));
        }
    }
}
