// OrderGridTests.cs
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class OrderGridTests
    {
        [Fact]
        public void JetsGetTheFourLabelledRows()
        {
            string[] want =
            {
                "ATTACK", "SPLASH", "ENGAGE", "SWEEP",
                "MOVE", "ORBIT", "CAP", "HOLD",
                "BREAK", "FORM UP", "ECM", "DETACH",
                "RTB", "REFIT", "LAND", "CARGO",
            };
            for (int r = 0; r < OrderGrid.Rows; r++)
                for (int c = 0; c < OrderGrid.Columns; c++)
                    Assert.Equal(want[r * 4 + c], OrderGrid.At(r, c, false).Label);
            Assert.Equal(new[] { "OFFENSE", "MOVE", "DEFENSE", "SUPPORT" }, OrderGrid.RowLabels);
        }

        [Fact]
        public void HelosSwapScoutAndTheSupportRow()
        {
            // The user's answer 2026-09-24: SWEEP -> SCOUT, SUPPORT -> TAKE OFF · RESCUE · LAND · CARGO.
            Assert.Equal("SCOUT", OrderGrid.At(0, 3, true).Label);
            Assert.Equal(new[] { "TAKE OFF", "RESCUE", "LAND", "CARGO" },
                new[] { OrderGrid.At(3, 0, true).Label, OrderGrid.At(3, 1, true).Label, OrderGrid.At(3, 2, true).Label, OrderGrid.At(3, 3, true).Label });
            Assert.Equal("ORBIT", OrderGrid.At(1, 1, true).Label);
        }

        [Fact]
        public void PointAndTargetOrdersArmTheirMapMode()
        {
            Assert.Equal(MapMode.Attack, OrderGrid.At(0, 0, false).Map);
            Assert.Equal(GridInput.Target, OrderGrid.At(0, 0, false).Input);
            Assert.Equal(MapMode.Move, OrderGrid.At(1, 0, false).Map);
            Assert.Equal(MapMode.Orbit, OrderGrid.At(1, 1, false).Map);
            Assert.Equal(MapMode.Hold, OrderGrid.At(1, 3, false).Map);
            Assert.Equal(MapMode.Land, OrderGrid.At(3, 2, false).Map);
            Assert.Equal(MapMode.Cargo, OrderGrid.At(3, 3, false).Map);
            Assert.Equal(GridInput.Now, OrderGrid.At(0, 1, false).Input);
            Assert.Equal(MapMode.Off, OrderGrid.At(2, 1, false).Map);
        }

        [Fact]
        public void UnbuiltOrdersSayWhyAndAreNeverPressable()
        {
            foreach (var (r, c) in new[] { (0, 3), (1, 2), (2, 2) })
            {
                GridCell cell = OrderGrid.At(r, c, false);
                Assert.False(cell.Built);
                Assert.Equal(cell.Pending, OrderGrid.Why(cell, true, 3, true));
            }
        }

        [Fact]
        public void WhyNamesTheBlocker()
        {
            GridCell engage = OrderGrid.At(0, 2, false), detach = OrderGrid.At(2, 3, false);
            Assert.Null(OrderGrid.Why(engage, true, 3, false));
            Assert.Equal("Orders are host only for now", OrderGrid.Why(engage, false, 3, false));
            Assert.Equal("No wingmen: call or recruit some first", OrderGrid.Why(engage, true, 0, false));
            Assert.Equal("Select the wingmen to detach", OrderGrid.Why(detach, true, 3, false));
            Assert.Null(OrderGrid.Why(detach, true, 3, true));
        }

        [Fact]
        public void IdsAreUniqueAndTipsNeverEmpty()
        {
            var seen = new HashSet<string>();
            foreach (bool helos in new[] { false, true })
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        GridCell cell = OrderGrid.At(r, c, helos);
                        Assert.StartsWith("tac.orders.", cell.Id);
                        Assert.False(string.IsNullOrEmpty(cell.Tip));
                        seen.Add(cell.Id);
                    }
            Assert.Equal(19, seen.Count);   // 16 jet cells + SCOUT, TAKE OFF, RESCUE
        }

        [Fact]
        public void HereExistsForOrbitAndHoldOnly()
        {
            Assert.True(OrderGrid.HasHere(GridOrder.Orbit));
            Assert.True(OrderGrid.HasHere(GridOrder.Hold));
            Assert.False(OrderGrid.HasHere(GridOrder.Move));
            Assert.False(OrderGrid.HasHere(GridOrder.Attack));
        }
    }
}
