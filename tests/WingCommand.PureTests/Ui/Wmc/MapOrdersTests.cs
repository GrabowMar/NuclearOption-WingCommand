using Xunit;

namespace WingCommand.PureTests
{
    public class MapOrdersTests
    {
        [Fact]
        public void NoModeAndNoSelectionLeavesTheClickToTheGame()
        {
            Assert.False(MapOrders.Consumes(MapMode.Off, false, false));
            Assert.Equal(MapClick.None, MapOrders.Resolve(MapMode.Off, false, MapPointer.Empty, false));
        }

        [Fact]
        public void SelectedWingmenMoveOnARightClick()
        {
            // Spec §5: with no mode armed and a selection, right-click is MOVE (the 0.9 behaviour).
            Assert.True(MapOrders.Consumes(MapMode.Off, true, false));
            Assert.Equal(MapClick.Move, MapOrders.Resolve(MapMode.Off, true, MapPointer.Enemy, false));
        }

        [Fact]
        public void AnotherOwnerKeepsTheClick()
        {
            // Review focus 1: Boscali's support picker armed - WMC never takes the right-click.
            Assert.False(MapOrders.Consumes(MapMode.Move, true, true));
            Assert.False(MapOrders.Consumes(MapMode.Off, true, true));
        }

        [Theory]
        [InlineData(MapMode.Move, MapClick.Move)]
        [InlineData(MapMode.Route, MapClick.AddPoint)]
        [InlineData(MapMode.Orbit, MapClick.Orbit)]
        [InlineData(MapMode.Hold, MapClick.Hold)]
        [InlineData(MapMode.Cargo, MapClick.Cargo)]
        internal void EachModePlacesItsOrder(MapMode mode, MapClick click)
        {
            Assert.True(MapOrders.Consumes(mode, false, false));
            Assert.Equal(click, MapOrders.Resolve(mode, false, MapPointer.Empty, false));
        }

        [Fact]
        public void AttackTakesAnEnemyAndShiftAddsOne()
        {
            Assert.Equal(MapClick.NeedEnemy, MapOrders.Resolve(MapMode.Attack, false, MapPointer.Empty, false));
            Assert.Equal(MapClick.NeedEnemy, MapOrders.Resolve(MapMode.Attack, false, MapPointer.Other, true));
            Assert.Equal(MapClick.Attack, MapOrders.Resolve(MapMode.Attack, false, MapPointer.Enemy, false));
            Assert.Equal(MapClick.AddTarget, MapOrders.Resolve(MapMode.Attack, false, MapPointer.Enemy, true));
        }

        [Fact]
        public void PromptsNameTheModeAndTheScope()
        {
            Assert.Null(MapOrders.Prompt(MapMode.Off, "WING"));
            Assert.Equal("MOVE · #3 #4 · RIGHT-CLICK THE MAP", MapOrders.Prompt(MapMode.Move, "#3 #4"));
            Assert.Equal("ATTACK · WING · RIGHT-CLICK AN ENEMY (SHIFT ADDS)", MapOrders.Prompt(MapMode.Attack, "WING"));
            Assert.Equal("ROUTE · ELEMENT B · RIGHT-CLICK TO ADD POINTS, THEN SEND", MapOrders.Prompt(MapMode.Route, "ELEMENT B"));
        }

        [Fact]
        public void LandPlacesALandingPoint()
        {
            Assert.Equal(MapClick.Land, MapOrders.Resolve(MapMode.Land, false, MapPointer.Empty, false));
            Assert.Equal("LAND · WING · RIGHT-CLICK THE MAP", MapOrders.Prompt(MapMode.Land, "WING"));
        }
    }
}
