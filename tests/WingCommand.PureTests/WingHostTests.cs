using System;
using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>
    /// The host seam is global mutable state, which xunit runs in parallel across classes.
    /// Every test here resets it first and clears it after, and the whole class is one
    /// collection so no two of them are in flight at once.
    /// </summary>
    [Collection("WingHost")]
    public class WingHostTests : IDisposable
    {
        public WingHostTests() => WingHost.Reset();

        public void Dispose() => WingHost.Reset();

        private static WingHostProfile Surface(object owner = null, params string[] labels)
        {
            return new WingHostProfile(
                owner: owner ?? new object(),
                isSurfaceVehicle: true,
                vehicleClass: "bote",
                overwatch: true,
                allowMixedAirframes: true,
                overwatchAltitude: 1500f,
                labels: labels.Length == 0 ? null : labels);
        }

        [Fact]
        public void DefaultProfileIsInert()
        {
            Assert.False(WingHost.Current.Active);
            Assert.False(WingHost.Current.IsSurfaceVehicle);
            Assert.False(WingHost.Current.Overwatch);
            Assert.False(WingHost.Current.AllowMixedAirframes);
            Assert.Equal(0f, WingHost.Current.OverwatchAltitude);
            Assert.Null(WingHost.Current.LabelFor(WingOrder.Formation));
            Assert.Null(WingHost.Current.ShortLabelFor(WingOrder.Formation));
            Assert.False(WingHost.Current.IsHidden(WingOrder.Formation));
        }

        [Fact]
        public void SetAppliesTheProfile()
        {
            var owner = new object();
            WingHost.Set(Surface(owner));

            Assert.True(WingHost.Current.Active);
            Assert.True(WingHost.Current.Overwatch);
            Assert.Equal("bote", WingHost.Current.VehicleClass);
            Assert.Same(owner, WingHost.Current.Owner);
        }

        [Fact]
        public void SetRefusesAProfileWithNoOwner()
        {
            Assert.Throws<ArgumentException>(() => WingHost.Set(new WingHostProfile(owner: null)));
        }

        [Fact]
        public void RevisionMovesOnEveryChange()
        {
            int start = WingHost.Revision;

            WingHost.Set(Surface());
            Assert.Equal(start + 1, WingHost.Revision);

            WingHost.Clear();
            Assert.Equal(start + 2, WingHost.Revision);

            // Clearing what is already clear is not a change, so the cached UI that reads
            // this must not be told to rebuild.
            WingHost.Clear();
            Assert.Equal(start + 2, WingHost.Revision);
        }

        [Fact]
        public void NoteLeaderKeepsAProfileForItsOwnVehicle()
        {
            var owner = new object();
            WingHost.Set(Surface(owner));

            WingHost.NoteLeader(owner);

            Assert.True(WingHost.Current.Active);
        }

        [Fact]
        public void NoteLeaderDropsAProfileForSomeoneElsesVehicle()
        {
            WingHost.Set(Surface(new object()));

            WingHost.NoteLeader(new object());

            Assert.False(WingHost.Current.Active);
        }

        [Fact]
        public void NoteLeaderDropsAProfileWhenTheLeaderIsLost()
        {
            WingHost.Set(Surface(new object()));

            WingHost.NoteLeader(null);

            Assert.False(WingHost.Current.Active);
        }

        [Fact]
        public void LabelsOverrideOnlyWhatTheyName()
        {
            // Indexed by (int)WingOrder: Formation is 0, Engage is 1.
            WingHost.Set(Surface(null, "Overwatch"));

            Assert.Equal("Overwatch", WingHost.Current.LabelFor(WingOrder.Formation));
            Assert.Null(WingHost.Current.LabelFor(WingOrder.Engage));
            Assert.Null(WingHost.Current.LabelFor(WingOrder.Maneuver));
        }

        [Fact]
        public void AnEmptyLabelEntryFallsThroughToTheStockName()
        {
            WingHost.Set(Surface(null, "", null!, "RTB"));

            Assert.Null(WingHost.Current.LabelFor(WingOrder.Formation));
            Assert.Null(WingHost.Current.LabelFor(WingOrder.Engage));
            Assert.Equal("RTB", WingHost.Current.LabelFor(WingOrder.ReturnToBase));
        }

        [Fact]
        public void HiddenOrdersMaskExactlyWhatItNames()
        {
            uint mask = WingHostProfile.Mask(WingOrder.LandHere, WingOrder.Maneuver);
            WingHost.Set(new WingHostProfile(new object(), overwatch: true, hiddenOrders: mask,
                                             hiddenReason: "not from a boat"));

            Assert.True(WingHost.Current.IsHidden(WingOrder.LandHere));
            Assert.True(WingHost.Current.IsHidden(WingOrder.Maneuver));
            Assert.False(WingHost.Current.IsHidden(WingOrder.Formation));
            Assert.False(WingHost.Current.IsHidden(WingOrder.Attack));
            Assert.Equal("not from a boat", WingHost.Current.HiddenReason);
        }

        [Fact]
        public void MaskOfNothingHidesNothing()
        {
            Assert.Equal(0u, WingHostProfile.Mask());
            Assert.Equal(0u, WingHostProfile.Mask(null!));
        }

        [Fact]
        public void SurfaceWingmenAreOffByDefault()
        {
            WingHost.Set(new WingHostProfile(new object(), overwatch: true));
            Assert.False(WingHost.Current.AllowSurfaceWingmen);
        }

        [Fact]
        public void SurfaceWingmenWithoutOverwatchIsRefused()
        {
            // The roster may only hold something no flight state can fly once the aircraft
            // in it have stopped trying to hold slots on it.
            Assert.Throws<ArgumentException>(() =>
                WingHost.Set(new WingHostProfile(new object(), allowSurfaceWingmen: true)));
        }

        [Fact]
        public void MixedAirframesWithoutOverwatchIsRefused()
        {
            // The refusal it lifts exists because a helicopter cannot hold a slot on a jet.
            // Lifting it while the wing is still flying slots would fly helicopters into
            // the ground, so the two flags are only valid together.
            Assert.Throws<ArgumentException>(() =>
                WingHost.Set(new WingHostProfile(new object(), allowMixedAirframes: true)));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(-1f)]
        public void AnImpossibleOverwatchAltitudeIsRefused(float altitude)
        {
            Assert.Throws<ArgumentException>(() =>
                WingHost.Set(new WingHostProfile(new object(), overwatchAltitude: altitude)));
        }

        [Fact]
        public void ALabelTableLongerThanTheOrderEnumIsRefused()
        {
            // A caller built against a different Wing Command. Truncating silently would
            // hide the mismatch until an order came out under the wrong name.
            Assert.Throws<ArgumentException>(() =>
                WingHost.Set(new WingHostProfile(new object(), labels: new string[13])));
        }

        [Fact]
        public void ARejectedProfileDoesNotReplaceTheLiveOne()
        {
            var owner = new object();
            WingHost.Set(Surface(owner));
            int revision = WingHost.Revision;

            Assert.Throws<ArgumentException>(() =>
                WingHost.Set(new WingHostProfile(new object(), overwatchAltitude: -5f)));

            Assert.Same(owner, WingHost.Current.Owner);
            Assert.Equal(revision, WingHost.Revision);
        }
    }
}
