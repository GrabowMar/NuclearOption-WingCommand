using Xunit;

namespace WingCommand.PureTests
{
    public class WingMapPresentationTests
    {
        [Fact]
        public void ClearingCommandScopeLeavesEveryMemberIdentifiable()
        {
            var first = new WingMember();
            var second = new WingMember();
            var wing = new WingRegistry();
            wing.Members.AddRange(new[] { first, second });
            var selection = new WingCommandSelection();

            Assert.True(Paint(first).CommandBrackets);
            Assert.True(Paint(second).CommandBrackets);
            selection.SelectOnly(second);
            Assert.False(Paint(first).CommandBrackets);
            Assert.True(Paint(second).CommandBrackets);
            selection.DeselectAll();
            Assert.False(Paint(first).CommandBrackets);
            Assert.False(Paint(second).CommandBrackets);
            Assert.Equal(WingMapPresentation.OutlineKind.Member, Paint(first).Outline);
            Assert.Equal(WingMapPresentation.OutlineKind.Member, Paint(second).Outline);

            WingMapPresentation Paint(WingMember member) => WingMapPresentation.Resolve(
                wing.Contains(member), isWingTarget: false, highlightWing: true,
                highlightTargets: true, tacticalActive: true, selection.Contains(member));
        }

        [Fact]
        public void NativeTargetSelectionCanClearWithoutChangingWingIdentityOrCommandScope()
        {
            // Native target/filter changes may alter clicks, but membership and command scope remain
            // stable.
            foreach (bool nativeSelected in new[] { true, false })
            {
                WingMapPresentation appearance = WingMapPresentation.Resolve(
                    isWingMember: true, isWingTarget: false, highlightWing: true,
                    highlightTargets: true, tacticalActive: true, commandSelected: true);
                Assert.Equal(WingMapPresentation.OutlineKind.Member, appearance.Outline);
                Assert.True(appearance.CommandBrackets);
                Assert.True(MapSelectionPolicy.IconReceivesPointer(isPlayerAircraft: false,
                    nativeSelected, isWingMember: true, tacticalCommandsActive: true));
                Assert.Equal(!nativeSelected, MapSelectionPolicy.IconReceivesPointer(
                    isPlayerAircraft: false, nativeSelected, isWingMember: true,
                    tacticalCommandsActive: false));
            }
        }

        [Fact]
        public void ReusedIconDropsMembershipEvenWhenOldCommandScopeStillContainsIt()
        {
            WingMapPresentation released = Paint(member: false, target: false);
            Assert.Equal(WingMapPresentation.OutlineKind.None, released.Outline);
            Assert.False(released.CommandBrackets);

            WingMapPresentation enemy = Paint(member: false, target: true);
            Assert.Equal(WingMapPresentation.OutlineKind.Target, enemy.Outline);
            Assert.False(enemy.CommandBrackets);

            WingMapPresentation recruit = Paint(member: true, target: true);
            Assert.Equal(WingMapPresentation.OutlineKind.Member, recruit.Outline);
            Assert.True(recruit.CommandBrackets);

            static WingMapPresentation Paint(bool member, bool target) =>
                WingMapPresentation.Resolve(member, target, highlightWing: true,
                    highlightTargets: true, tacticalActive: true, commandSelected: true);
        }

        [Fact]
        public void ClosingTacticalHidesCommandBracketsButPreservesMembership()
        {
            WingMapPresentation appearance = WingMapPresentation.Resolve(
                isWingMember: true, isWingTarget: false, highlightWing: true,
                highlightTargets: true, tacticalActive: false, commandSelected: true);
            Assert.Equal(WingMapPresentation.OutlineKind.Member, appearance.Outline);
            Assert.False(appearance.CommandBrackets);
        }

        [Fact]
        public void HighlightPreferencesNeverHideTheRecipientsOfAnActiveTacticalOrder()
        {
            WingMapPresentation member = WingMapPresentation.Resolve(
                isWingMember: true, isWingTarget: true, highlightWing: false,
                highlightTargets: false, tacticalActive: true, commandSelected: true);
            Assert.Equal(WingMapPresentation.OutlineKind.None, member.Outline);
            Assert.True(member.CommandBrackets);

            WingMapPresentation target = WingMapPresentation.Resolve(
                isWingMember: false, isWingTarget: true, highlightWing: true,
                highlightTargets: false, tacticalActive: true, commandSelected: true);
            Assert.Equal(WingMapPresentation.OutlineKind.None, target.Outline);
            Assert.False(target.CommandBrackets);
        }

        [Fact]
        public void DownedPilotAlwaysGetsDistinctSarOutline()
        {
            WingMapPresentation appearance = WingMapPresentation.Resolve(
                isWingMember: false, isWingTarget: true, highlightWing: false,
                highlightTargets: false, tacticalActive: false, commandSelected: false,
                isDowned: true);

            Assert.Equal(WingMapPresentation.OutlineKind.Downed, appearance.Outline);
            Assert.False(appearance.CommandBrackets);
        }
    }
}
