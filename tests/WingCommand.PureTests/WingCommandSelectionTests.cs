using System.Collections.Generic;
using Xunit;

// Minimal engine-free roster stubs for production command selection.
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
        public void NamedGroupsRecallOnlyTheirSurvivingMembersAndResetBetweenMissions()
        {
            var wing = new WingRegistry();
            var first = new WingMember();
            var second = new WingMember();
            var third = new WingMember();
            wing.Members.AddRange(new[] { first, second, third });
            var selection = new WingCommandSelection();
            for (int i = 0; i < FlightGroups<WingMember>.Count; i++)
            {
                Assert.False(selection.Groups.Exists(i));
                Assert.Null(selection.Groups.Name(i));
            }
            selection.Groups.Save(0, "  ", selection.Snapshot(wing));
            Assert.False(selection.Groups.Exists(0));
            selection.SelectOnly(first);
            selection.Toggle(second);
            selection.Groups.Save(0, "  Cover  ", selection.Snapshot(wing));
            selection.SelectOnly(third);
            selection.Groups.Save(1, "Strike", selection.Snapshot(wing));
            Assert.Equal("Cover", selection.Groups.Name(0));
            selection.RecallGroup(0, wing);
            Assert.Equal(new[] { first, second }, selection.Snapshot(wing));
            second.Alive = false;
            wing.Members.Remove(first);
            selection.RecallGroup(0, wing);
            Assert.True(selection.IsNone);
            Assert.Empty(selection.Snapshot(wing));
            selection.RecallGroup(1, wing);
            Assert.Equal(new[] { third }, selection.Snapshot(wing));
            selection.Groups.Clear(1);
            Assert.False(selection.Groups.Exists(1));
            Assert.Null(selection.Groups.Name(1));
            Assert.True(third.Alive);
            Assert.Contains(third, wing.Members);
            selection.Reset();
            Assert.Null(selection.Groups.Name(0));
            Assert.False(selection.Groups.Exists(0));
            Assert.Empty(selection.GroupMembers(0, wing));
            Assert.Empty(selection.GroupMembers(1, wing));
        }

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
        [InlineData(false, true, true, true, true)]  // Keep native weapon targets tactically clickable.
        [InlineData(false, true, true, false, false)] // Restore stock hit behaviour after Tactical closes.
        [InlineData(false, true, false, true, false)] // Leave unrelated selected contacts under native handling.
        [InlineData(false, false, true, true, true)]
        [InlineData(false, false, true, false, true)]
        [InlineData(true, false, false, true, false)] // Never capture the player's own icon.
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
