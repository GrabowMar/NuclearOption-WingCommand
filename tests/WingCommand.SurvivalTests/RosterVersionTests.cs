using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Random = UnityEngine.Random;

namespace WingCommand
{
    /// <summary>WING's roster seams (R6; research squadron-roster-lifecycle): the version moves on every change WING shows, the roster keeps
    /// join order, drafted pilots start at 0 XP, a pilot leaves the squadron only while free, and a local search costs half the lost
    /// airframe's value (spec WMC rebuild §WING; SarRules).</summary>
    [Collection("static roster")]
    public class RosterVersionTests
    {
        public RosterVersionTests()
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
            Random.RangeBias = 0;
        }

        private static Aircraft Plane(int id, float value = 0f)
        {
            var aircraft = new Aircraft { persistentID = id, NetworkHQ = new FactionHQ() };
            aircraft.definition.value = value;
            aircraft.Pilot = new Pilot { aircraft = aircraft };
            UnitRegistry.Units[id] = aircraft;
            return aircraft;
        }

        private static PilotDismounted Eject(Aircraft aircraft)
        {
            var native = new PilotDismounted {
                parentUnit = aircraft.persistentID, NetworkHQ = aircraft.NetworkHQ,
                animationState = PilotDismounted.PilotState.landing
            };
            WingSearchAndRescue.Track(native);
            aircraft.Pilot.ejected = true;
            return native;
        }

        [Fact]
        public void AKillMovesTheVersionWithProgressionOff()
        {
            Plugin.Settings.PilotProgression.Value = false;
            var aircraft = Plane(1);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            int before = WingPilotRoster.Version;
            WingPilotRoster.NoteKill(aircraft, 9, "tank", award: true);
            Assert.Equal(1, pilot.Kills);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void ASortieMovesTheVersionWithProgressionOff()
        {
            Plugin.Settings.PilotProgression.Value = false;
            var aircraft = Plane(1);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            int before = WingPilotRoster.Version;
            WingPilotRoster.NoteSortie(aircraft);
            Assert.Equal(1, pilot.Sorties);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void TheKillerMovesTheVersion()
        {
            var aircraft = Plane(1);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            Plane(2).definition.unitName = "SAM";
            int before = WingPilotRoster.Version;
            WingPilotRoster.RecordKiller(1, 2);
            Assert.Equal("SAM", pilot.KilledBy);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void ASurvivorSeenAfterTheSeatWasSettledMovesTheVersion()
        {
            var aircraft = Plane(1);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            WingPilotRoster.Retire(1, false);
            Assert.Equal(PilotRecoveryStatus.Missing, pilot.RecoveryStatus);
            int before = WingPilotRoster.Version;
            Eject(aircraft);
            Assert.Equal(PilotRecoveryStatus.Downed, pilot.RecoveryStatus);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void ALocalSearchMovesTheVersion()
        {
            var aircraft = Plane(1, 40f);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 100f;
            int before = WingPilotRoster.Version;
            Assert.True(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void LosingTheLastSelectablePilotMovesTheVersion()
        {
            WingPilot pilot = WingPilotRoster.RecruitManual();
            Assert.Same(pilot, WingPilotRoster.Selected);
            pilot.Lost = true;
            int before = WingPilotRoster.Version;
            WingPilotRoster.AdvanceSelected();
            Assert.Null(WingPilotRoster.Selected);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void RecruitingMovesTheVersion()
        {
            int before = WingPilotRoster.Version;
            WingPilotRoster.RecruitManual();
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        [Fact]
        public void TheRosterKeepsJoinOrder()
        {
            var p = new[] { WingPilotRoster.RecruitManual(), WingPilotRoster.RecruitManual(), WingPilotRoster.RecruitManual() };
            p[0].Xp = 50;
            p[1].Xp = 300;
            p[2].Xp = 100;
            var into = new List<WingPilot> { p[2] };
            WingPilotRoster.Roster(into);
            Assert.Equal(p, into);
        }

        [Fact]
        public void DraftedPilotsStartAtZeroXp()
        {
            Random.RangeBias = 5;
            Assert.Equal(0, WingPilotRoster.RecruitManual().Xp);
            Assert.Equal(0, WingPilotRoster.Assign(Plane(1)).Xp);
        }

        [Fact]
        public void OnlyAFreePilotCanBeDischarged()
        {
            var downed = Plane(1);
            WingPilot lost = WingPilotRoster.Assign(downed);
            Eject(downed);
            WingPilotRoster.Retire(1, false);
            WingPilot flying = WingPilotRoster.Assign(Plane(2));
            WingPilot reserved = WingPilotRoster.ReserveForRequisition();
            WingPilot free = WingPilotRoster.RecruitManual();

            Assert.False(WingPilotRoster.RemoveFromSquadron(lost));
            Assert.False(WingPilotRoster.RemoveFromSquadron(flying));
            Assert.False(WingPilotRoster.RemoveFromSquadron(reserved));
            Assert.True(WingPilotRoster.RemoveFromSquadron(free));
            Assert.False(WingPilotRoster.Contains(free));
        }

        [Fact]
        public void APilotOffTheRosterIsNeverUpcomingEvenWhenRescued()
        {
            var stranger = new WingPilot { Callsign = "GHOST", RecoveryStatus = PilotRecoveryStatus.Downed };
            WingPilotRoster.SettleRescue(7, stranger, PilotRecoveryStatus.None, false, "rescued");
            Assert.False(WingPilotRoster.IsSelectable(stranger));
            Assert.Null(WingPilotRoster.Upcoming);
            Assert.Null(WingPilotRoster.Selected);
            WingPilotRoster.Select(stranger);
            Assert.Null(WingPilotRoster.Selected);
        }

        [Fact]
        public void ApplyIdentityChangesTheIdentityNeverTheRecord()
        {
            WingPilot live = WingPilotRoster.RecruitManual();
            live.Xp = 70;
            live.Kills = 2;
            int before = WingPilotRoster.Version;
            WingPilotRoster.ApplyIdentity(live, new CustomPilotRecord
            {
                Name = "Ada Byrne", Callsign = live.Callsign, DialogueTag = "ADA", Persona = ChatterPersona.Aggressive, Background = "b", Xp = 900, Kills = 40,
            });
            Assert.Equal("Ada Byrne", live.Name);
            Assert.Equal("ADA", live.DialogueTag);
            Assert.Equal(ChatterPersona.Aggressive, live.Persona);
            Assert.Equal("b", live.Background);
            Assert.Equal(70, live.Xp);
            Assert.Equal(2, live.Kills);
            Assert.NotEqual(before, WingPilotRoster.Version);
        }

        // ---- LOCAL SAR's price (SarRules) and its window

        [Fact]
        public void LocalSearchCostsHalfTheValueCapturedWhenThePilotWentDown()
        {
            var aircraft = Plane(1, 40f);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            UnitRegistry.Units.Remove(1);
            GameManager.LocalPlayer.Allocation = 100f;
            Assert.Equal(20f, WingSearchAndRescue.LocalCost(pilot));
            Assert.True(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Equal(80f, GameManager.LocalPlayer.Allocation);
            Assert.Contains(WingCommandManager.Instance.Messages, m => m.Contains("20 CR"));
        }

        [Fact]
        public void LocalSearchIsFreeInTheSandbox()
        {
            Plugin.Settings.SandboxFreeCalls.Value = true;
            var aircraft = Plane(1, 40f);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 0f;
            Assert.True(WingSearchAndRescue.CanOrganizeLocalRecovery(pilot, out string why), why);
            Assert.True(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Equal(0f, GameManager.LocalPlayer.Allocation);
        }

        [Fact]
        public void LocalSearchWaitsOutTheEjectionCheck()
        {
            var aircraft = Plane(1, 40f);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 100f;
            Time.timeSinceLevelLoad = 5f;
            Assert.False(WingSearchAndRescue.CanOrganizeLocalRecovery(pilot, out string why));
            Assert.False(string.IsNullOrEmpty(why));
            Assert.False(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Equal(100f, GameManager.LocalPlayer.Allocation);

            Time.timeSinceLevelLoad = 31f;
            WingSearchAndRescue.Tick();
            Assert.True(WingSearchAndRescue.CanOrganizeLocalRecovery(pilot, out why), why);
        }

        [Fact]
        public void LocalSearchShortOfFundsSaysWhatItNeeds()
        {
            var aircraft = Plane(1, 40f);
            WingPilot pilot = WingPilotRoster.Assign(aircraft);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 19f;
            Assert.False(WingSearchAndRescue.CanOrganizeLocalRecovery(pilot, out string why));
            Assert.Contains("20 CR", why);
            Assert.False(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Equal(19f, GameManager.LocalPlayer.Allocation);
        }

        [Fact]
        public void ARescueBountyIsHalfTheValueCapturedWhenThePilotWentDown()
        {
            var aircraft = Plane(1, 40f);
            WingPilotRoster.Assign(aircraft);
            PilotDismounted native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            UnitRegistry.Units.Remove(1);
            GameManager.LocalPlayer.Allocation = 0f;
            var rescuer = Plane(2);
            rescuer.NetworkHQ = aircraft.NetworkHQ;
            WingSearchAndRescue.Captured(native, rescuer);
            Assert.Equal(20f, GameManager.LocalPlayer.Allocation);
        }
    }
}
