using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Random = UnityEngine.Random;

namespace WingCommand
{
    /// <summary>SUPPLY step 1 (spec WMC rebuild §SUPPLY; design supply-pilots-adopt-reserve §2a): the pilot the card shows is
    /// the pilot who flies.</summary>
    // The roster is static: classes that reset it must not run in parallel.
    [Collection("static roster")]
    public class RosterPickTests
    {
        public RosterPickTests()
        {
            WingPilotRoster.Reset();
            UnitRegistry.Units.Clear();
            Plugin.Settings = new Config();
            Plugin.Logger = new Log();
            WingCommandManager.Instance = new WingCommandManager();
            GameManager.LocalPlayer = new Player();
            Time.timeSinceLevelLoad = 0f;
            Random.Next = 0f;
            Random.Rolls = 0;
        }

        private static Aircraft Plane(int id)
        {
            var aircraft = new Aircraft { persistentID = id, NetworkHQ = new FactionHQ() };
            aircraft.Pilot = new Pilot { aircraft = aircraft };
            UnitRegistry.Units[id] = aircraft;
            return aircraft;
        }

        /// <summary>Three free pilots, the second most senior first: XP 50, 300, 100.</summary>
        private static WingPilot[] Three()
        {
            var p = new[] { WingPilotRoster.RecruitManual(), WingPilotRoster.RecruitManual(), WingPilotRoster.RecruitManual() };
            p[0].Xp = 50;
            p[1].Xp = 300;
            p[2].Xp = 100;
            return p;
        }

        [Fact]
        public void UpcomingIsTheSelectionWhileItIsFree()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[2]);
            Assert.Same(p[2], WingPilotRoster.Upcoming);
        }

        [Fact]
        public void UpcomingIsTheMostSeniorFreePilotWhenTheSelectionFlies()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[1]);
            WingPilotRoster.Assign(Plane(1), p[1]);
            Assert.Same(p[2], WingPilotRoster.Upcoming);
        }

        [Fact]
        public void UpcomingIsNullWhenNobodyIsFree()
        {
            Assert.Null(WingPilotRoster.Upcoming);
            WingPilot one = WingPilotRoster.RecruitManual();
            WingPilotRoster.Assign(Plane(1), one);
            Assert.Null(WingPilotRoster.Upcoming);
        }

        [Fact]
        public void ARequisitionReservesTheUpcomingPilotAndAdvancesTheSelection()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[0]);
            WingPilot got = WingPilotRoster.ReserveForRequisition();
            Assert.Same(p[0], got);
            Assert.True(WingPilotRoster.IsReserved(p[0]));
            Assert.NotSame(p[0], WingPilotRoster.Upcoming);
            Assert.True(WingPilotRoster.IsFree(WingPilotRoster.Upcoming));
        }

        [Fact]
        public void WithNobodyFreeARequisitionDraftsANewPilot()
        {
            WingPilot drafted = WingPilotRoster.ReserveForRequisition();
            Assert.NotNull(drafted);
            Assert.True(WingPilotRoster.IsReserved(drafted));
        }

        [Fact]
        public void AReservedPilotLeavesTheFreeList()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[1]);
            WingPilotRoster.ReserveForRequisition();
            var free = new List<WingPilot>();
            WingPilotRoster.FreePilots(free);
            Assert.DoesNotContain(p[1], free);
            Assert.Equal(2, free.Count);
        }

        [Fact]
        public void ADroppedLaunchGivesThePilotBackAndSelectsThemAgain()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[2]);
            WingPilot got = WingPilotRoster.ReserveForRequisition();
            WingPilotRoster.ReleaseReservation(got, restoreSelection: true);
            Assert.True(WingPilotRoster.IsFree(p[2]));
            Assert.Same(p[2], WingPilotRoster.Upcoming);
        }

        [Fact]
        public void TheReservedPilotSitsInTheLaunchedAircraftAndAnAdoptionNeverTakesThem()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[1]);
            WingPilot reservedPilot = WingPilotRoster.ReserveForRequisition();
            // An adoption before the launch joins takes the card's next pilot, never the reserved one.
            WingPilot adopted = WingPilotRoster.Assign(Plane(7));
            Assert.NotSame(reservedPilot, adopted);
            Assert.Same(reservedPilot, WingPilotRoster.Assign(Plane(8), reservedPilot));
        }

        [Fact]
        public void AnAdoptionWithNoPreferenceSeatsTheUpcomingPilot()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Select(p[0]);
            WingPilot upcoming = WingPilotRoster.Upcoming;
            Assert.Same(upcoming, WingPilotRoster.Assign(Plane(3)));
        }

        [Fact]
        public void FreePilotsListsOnlyFreePilotsBySeniority()
        {
            WingPilot[] p = Three();
            WingPilotRoster.Assign(Plane(1), p[0]);
            var free = new List<WingPilot> { p[0] };
            WingPilotRoster.FreePilots(free);
            Assert.Equal(new[] { p[1], p[2] }, free);
        }

        [Fact]
        public void TheVersionChangesWithEveryRosterChange()
        {
            int v = WingPilotRoster.Version;
            WingPilot one = WingPilotRoster.RecruitManual();
            Assert.NotEqual(v, v = WingPilotRoster.Version);
            WingPilotRoster.Select(one);
            Assert.NotEqual(v, v = WingPilotRoster.Version);
            WingPilot got = WingPilotRoster.ReserveForRequisition();
            Assert.NotEqual(v, v = WingPilotRoster.Version);
            WingPilotRoster.ReleaseReservation(got);
            Assert.NotEqual(v, v = WingPilotRoster.Version);
            WingPilotRoster.Assign(Plane(4), got);
            Assert.NotEqual(v, v = WingPilotRoster.Version);
            WingPilotRoster.Retire(new PersistentID(4), survived: true);
            Assert.NotEqual(v, WingPilotRoster.Version);
        }
    }
}
