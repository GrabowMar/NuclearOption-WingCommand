using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class FlightPoolTests
    {
        [Fact]
        public void ClassesFollowTheWeaponFlags()
        {
            Assert.Equal(StoreClass.Gun, StoreClasses.Of(true, false, false, false, 0f, 0f));
            Assert.Equal(StoreClass.Ecm, StoreClasses.Of(false, true, false, false, 0f, 0f));
            Assert.Equal(StoreClass.AirMissile, StoreClasses.Of(false, false, true, false, 0.8f, 0.1f));
            Assert.Equal(StoreClass.StrikeMissile, StoreClasses.Of(false, false, true, false, 0.1f, 0.8f));
            Assert.Equal(StoreClass.Bomb, StoreClasses.Of(false, false, false, true, 0f, 1f));
            Assert.Equal(StoreClass.Other, StoreClasses.Of(false, false, false, false, 0f, 0f));
        }

        [Fact]
        public void TotalsCountRoundsLeftAndLinesNameThem()
        {
            var t = new PoolTotals();
            FlightPool.Add(ref t, new StoreLine { Name = "AAM", Ammo = 2, Full = 2, Class = StoreClass.AirMissile });
            FlightPool.Add(ref t, new StoreLine { Name = "AGM", Ammo = 1, Full = 2, Class = StoreClass.StrikeMissile });
            FlightPool.Add(ref t, new StoreLine { Name = "GBU", Ammo = 4, Full = 4, Class = StoreClass.Bomb });
            FlightPool.Add(ref t, new StoreLine { Name = "GUN", Ammo = 540, Full = 600, Class = StoreClass.Gun });
            var lines = new List<string>();
            FlightPool.Lines(t, lines);
            Assert.Equal(new[] { "MISSILES  2 READY", "STRIKE  4 BOMBS · 1 AGM", "GUN  540 RDS" }, lines);
        }

        [Fact]
        public void AnEmptyPoolSaysSo()
        {
            var lines = new List<string>();
            FlightPool.Lines(new PoolTotals(), lines);
            Assert.Equal(new[] { "NO STORES" }, lines);
        }

        [Fact]
        public void OnePlaneListsItsStations()
        {
            var d = new MemberDetail { Stores = new StoreLine[6], StoreCount = 2 };
            d.Stores[0] = new StoreLine { Name = "AIM-9", Ammo = 1, Full = 2 };
            d.Stores[1] = new StoreLine { Name = "GUN", Ammo = 300, Full = 600 };
            var lines = new List<string>();
            FlightPool.StationLines(d, lines);
            Assert.Equal(new[] { "ST1  AIM-9  1/2", "ST2  GUN  300/600" }, lines);
        }
    }
}
