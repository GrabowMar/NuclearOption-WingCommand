using Xunit;

namespace WingCommand.PureTests
{
    public class CombatSupervisorTests
    {
        private static CombatSituation Fighting() =>
            new CombatSituation { AnchorDistance = 5000f, Ammo = 0.6f, AnchorPresent = true, Exit = NativeExit.None };

        [Fact]
        public void AMemberFightingNearItsAnchorWithAmmunitionStays()
        {
            Assert.False(CombatSupervisor.TakeBack(Fighting(), out TransitionReason reason));
            Assert.Equal(TransitionReason.None, reason);
        }

        [Fact]
        public void TheGamesWayOutOfCombatBringsItBackWithTheReason()
        {
            CombatSituation s = Fighting();
            s.Exit = NativeExit.Landing;
            Assert.True(CombatSupervisor.TakeBack(s, out TransitionReason reason));
            Assert.Equal(TransitionReason.NoTarget, reason);
            s.Bingo = true;
            Assert.True(CombatSupervisor.TakeBack(s, out reason));
            Assert.Equal(TransitionReason.Fuel, reason);
            s = Fighting();
            s.Exit = NativeExit.Transport;
            Assert.True(CombatSupervisor.TakeBack(s, out reason));
            Assert.Equal(TransitionReason.NoTarget, reason);
        }

        [Fact]
        public void AMemberWithoutATargetForLongIsTakenBack()
        {
            // Review M5a I2: with nothing to fight the game's combat state flies to mission objectives until the leash.
            CombatSituation s = Fighting();
            s.NoTargetSeconds = CombatSupervisor.NoTargetSeconds - 1f;
            Assert.False(CombatSupervisor.TakeBack(s, out _));
            s.NoTargetSeconds = CombatSupervisor.NoTargetSeconds + 0.1f;
            Assert.True(CombatSupervisor.TakeBack(s, out TransitionReason reason));
            Assert.Equal(TransitionReason.NoTarget, reason);
        }

        [Fact]
        public void BingoWinchesterAndTheLeashTakeItBack()
        {
            CombatSituation s = Fighting();
            s.Bingo = true;
            Assert.True(CombatSupervisor.TakeBack(s, out TransitionReason reason));
            Assert.Equal(TransitionReason.Fuel, reason);
            s = Fighting();
            s.Ammo = 0f;
            Assert.True(CombatSupervisor.TakeBack(s, out reason));
            Assert.Equal(TransitionReason.Winchester, reason);
            s = Fighting();
            s.AnchorDistance = CombatSupervisor.LeashMetres + 1f;
            Assert.True(CombatSupervisor.TakeBack(s, out reason));
            Assert.Equal(TransitionReason.Leash, reason);
            s.AnchorPresent = false;
            Assert.False(CombatSupervisor.TakeBack(s, out _), "no anchor, no leash");
        }
    }
}
