using System.Collections.Generic;
using Xunit;

// Minimal roster boundary for the linked production selection class; no game assemblies.
namespace WingCommand
{
    internal sealed partial class WingMember
    {
        public bool Alive = true;
        public bool DeliveryPending { get; set; }
        public bool IsPanicking { get; set; }
        public bool CanDeliverCargo { get; set; }
        public bool CanLandInPlace { get; set; }
        public bool CanJam { get; set; }
        public bool IsSurface { get; set; }
    }

    internal sealed partial class WingRegistry
    {
        public readonly List<WingMember> Members = new List<WingMember>();
        public int Count => Members.Count;
        public bool Contains(WingMember member) => Members.Contains(member);
    }
}

namespace WingCommand.PureTests
{
    public class WingCommandSelectionTests
    {
        [Fact]
        public void RosterAndMapClicksShareScopeAndSelectAllRestoresTheWholeFlight()
        {
            var first = new WingMember();
            var second = new WingMember();
            var third = new WingMember();
            var wing = new WingRegistry();
            wing.Members.AddRange(new[] { first, second, third });
            var selection = new WingCommandSelection();

            selection.ClickMember(second, toggle: false, wing);
            Assert.Equal(new[] { second }, selection.Snapshot(wing));
            selection.ClickMember(third, toggle: true, wing);
            Assert.Equal(new[] { second, third }, selection.Snapshot(wing));

            selection.ToggleSelectAll(wing);
            Assert.True(selection.IsAll);
            Assert.Equal(new[] { first, second, third }, selection.Snapshot(wing));

            selection.ClickMember(second, toggle: true, wing);
            Assert.Equal(new[] { first, third }, selection.Snapshot(wing));
            selection.ClickMember(first, toggle: false, wing);
            selection.ClickMember(first, toggle: false, wing);
            Assert.True(selection.IsNone);
            Assert.Empty(selection.Snapshot(wing));
        }

        [Theory]
        [InlineData(false, true, true, true, true)]  // A stock target remains clickable in WMC.
        [InlineData(false, true, true, false, false)] // Closing Tactical restores stock behavior.
        [InlineData(false, true, false, true, false)] // Unrelated selected contacts stay native.
        [InlineData(false, false, true, true, true)]
        [InlineData(false, false, true, false, true)]
        [InlineData(true, false, false, true, false)] // Never steal the player's own marker.
        [InlineData(true, true, true, true, false)]
        public void TacticalPointerSelectionIsIndependentOfWeaponTargetSelection(
            bool isPlayer, bool nativeSelected, bool wingMember, bool tactical, bool clickable)
        {
            Assert.Equal(clickable, MapSelectionPolicy.IconReceivesPointer(
                isPlayer, nativeSelected, wingMember, tactical));
        }

        [Fact]
        public void Snapshot_PreservesRosterOrderAndPrunesUnavailableMembers()
        {
            var first = new WingMember();
            var second = new WingMember();
            var removed = new WingMember();
            var dead = new WingMember { Alive = false };
            var wing = new WingRegistry();
            wing.Members.AddRange(new[] { first, second, dead, null });
            var selection = new WingCommandSelection();

            Assert.Equal(new[] { first, second }, selection.Snapshot(wing));
            selection.SelectOnly(second);
            selection.Toggle(first);
            selection.Toggle(removed);
            Assert.Equal(new[] { first, second }, selection.Snapshot(wing));
            Assert.False(selection.Contains(removed));

            first.Alive = false;
            Assert.Equal(new[] { second }, selection.Snapshot(wing));
            selection.DeselectAll();
            Assert.Empty(selection.Snapshot(wing));
            Assert.Equal(new[] { second }, selection.Snapshot(wing, wholeWing: true));
            Assert.True(selection.IsNone);
            Assert.Empty(selection.Snapshot(null));
        }
    }
}
