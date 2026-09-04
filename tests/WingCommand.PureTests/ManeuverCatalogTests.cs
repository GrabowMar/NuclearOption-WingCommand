using Xunit;

namespace WingCommand.Tests
{
    public class ManeuverCatalogTests
    {
        [Fact]
        public void AllContainsEveryEnumValueExactlyOnce()
        {
            var values = (ManeuverKind[])System.Enum.GetValues(typeof(ManeuverKind));
            Assert.Equal(values.Length, ManeuverCatalog.All.Length);
            foreach (ManeuverKind v in values)
                Assert.Contains(v, ManeuverCatalog.All);
        }

        [Fact]
        public void EveryKindHasLabelsAndFiniteGates()
        {
            foreach (ManeuverKind kind in ManeuverCatalog.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(ManeuverCatalog.Label(kind)));
                Assert.False(string.IsNullOrWhiteSpace(ManeuverCatalog.ShortLabel(kind)));

                float floor = ManeuverCatalog.MinEntryAltitudeAgl(kind);
                Assert.True(floor > 0f && !float.IsInfinity(floor) && !float.IsNaN(floor));

                float speed = ManeuverCatalog.MinEntrySpeedFraction(kind);
                Assert.InRange(speed, 0.01f, 1f);
            }
        }

        [Fact]
        public void BreakDirectionIsSignedForBreaksAndZeroOtherwise()
        {
            Assert.Equal(-1, ManeuverCatalog.BreakDirection(ManeuverKind.BreakLeft));
            Assert.Equal(1, ManeuverCatalog.BreakDirection(ManeuverKind.BreakRight));
            Assert.Equal(0, ManeuverCatalog.BreakDirection(ManeuverKind.Loop));
            Assert.Equal(0, ManeuverCatalog.BreakDirection(ManeuverKind.SplitS));
            Assert.Equal(0, ManeuverCatalog.BreakDirection(ManeuverKind.WingWaggle));
            Assert.Equal(0, ManeuverCatalog.BreakDirection(ManeuverKind.NotchThreat));
            Assert.Equal(0, ManeuverCatalog.BreakDirection(ManeuverKind.MaskTerrain));
        }

        [Fact]
        public void OnlyBreaksAndWaggleAreRotaryCapable()
        {
            Assert.True(ManeuverCatalog.RotaryCapable(ManeuverKind.BreakLeft));
            Assert.True(ManeuverCatalog.RotaryCapable(ManeuverKind.BreakRight));
            Assert.True(ManeuverCatalog.RotaryCapable(ManeuverKind.WingWaggle));
            Assert.True(ManeuverCatalog.RotaryCapable(ManeuverKind.NotchThreat));
            Assert.True(ManeuverCatalog.RotaryCapable(ManeuverKind.MaskTerrain));
            Assert.False(ManeuverCatalog.RotaryCapable(ManeuverKind.Loop));
            Assert.False(ManeuverCatalog.RotaryCapable(ManeuverKind.SplitS));
            Assert.False(ManeuverCatalog.RotaryCapable(ManeuverKind.Immelmann));
            Assert.False(ManeuverCatalog.RotaryCapable(ManeuverKind.BarrelRoll));
            Assert.False(ManeuverCatalog.RotaryCapable(ManeuverKind.AileronRoll));
        }

        [Fact]
        public void VerticalManoeuvresDemandMoreHeightThanTheBreaks()
        {
            float breakFloor = ManeuverCatalog.MinEntryAltitudeAgl(ManeuverKind.BreakLeft);
            Assert.True(ManeuverCatalog.MinEntryAltitudeAgl(ManeuverKind.SplitS) > breakFloor);
            Assert.True(ManeuverCatalog.MinEntryAltitudeAgl(ManeuverKind.Loop) > breakFloor);
        }

        [Fact]
        public void JamAndManoeuvreAreDefensiveOnly()
        {
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                         OrderRoePolicy.Authority(WingOrder.JamTarget));
            Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                         OrderRoePolicy.Authority(WingOrder.Maneuver));
        }
    }
}
