using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Random = UnityEngine.Random;

namespace WingCommand
{
    /// <summary>R7's roster seams (research squadron-roster-lifecycle §6.8; the 2026-09-25 decision): a mission starts with the saved
    /// pilots enlisted as ROOKIEs (identity only, no XP, no perks), a studio rename keeps the seat, and a drafted pilot never takes a
    /// saved callsign.</summary>
    [Collection("static roster")]
    public class SavedRosterTests
    {
        public SavedRosterTests()
        {
            WingPilotRoster.SavedTaken = null;
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

        private static Aircraft Plane(int id)
        {
            var aircraft = new Aircraft { persistentID = id, NetworkHQ = new FactionHQ() };
            aircraft.Pilot = new Pilot { aircraft = aircraft };
            UnitRegistry.Units[id] = aircraft;
            return aircraft;
        }

        private static CustomPilotRecord Saved(string callsign, int xp = 0)
        {
            var r = new CustomPilotRecord { Callsign = callsign, Name = "O. Bae", Persona = ChatterPersona.Calm, Background = "bio", Xp = xp, Kills = 9, Sorties = 4, Missions = 3 };
            r.ApplySelection(new PortraitSelection(PortraitBody.Female, 2, 3, 1, 0, 2));
            return r;
        }

        private static List<WingPilot> Roster()
        {
            var into = new List<WingPilot>();
            WingPilotRoster.Roster(into);
            return into;
        }

        [Fact]
        public void ASavedPilotJoinsAsARookieWithNoXpKillsSortiesOrPerks()
        {
            WingPilotRoster.StartMission(new[] { Saved("HATCH", xp: 900) });
            WingPilot p = Assert.Single(Roster());
            Assert.Equal(("HATCH", "O. Bae", ChatterPersona.Calm, "bio"), (p.Callsign, p.Name, p.Persona, p.Background));
            Assert.Equal((0, 0, 0), (p.Xp, p.Kills, p.Sorties));
            Assert.Equal(WingRank.Rookie, p.Rank);
            Assert.Empty(p.Perks);
            Assert.Equal(new PortraitSelection(PortraitBody.Female, 2, 3, 1, 0, 2), p.PortraitSelection.Value);
            Assert.Same(p, WingPilotRoster.Upcoming);
        }

        [Fact]
        public void JoiningTheSameCallsignTwiceAddsOnePilot()
        {
            WingPilotRoster.StartMission(new[] { Saved("HATCH"), Saved("hatch") });
            Assert.Single(Roster());
            Assert.Null(WingPilotRoster.Enlist(Saved("HATCH")));
            Assert.Single(Roster());
        }

        [Fact]
        public void StartingAMissionForgetsSeatsReservationsAndTheLastRoster()
        {
            WingPilot flying = WingPilotRoster.Assign(Plane(1));
            WingPilot held = WingPilotRoster.ReserveForRequisition();
            int v = WingPilotRoster.Version;
            WingPilotRoster.StartMission(new[] { Saved("HATCH") });
            Assert.Equal(new[] { "HATCH" }, Roster().ConvertAll(p => p.Callsign));
            Assert.False(WingPilotRoster.IsFlying(flying));
            Assert.False(WingPilotRoster.IsReserved(held));
            Assert.Null(WingPilotRoster.Of(Plane(1)));
            Assert.NotEqual(v, WingPilotRoster.Version);
        }

        [Fact]
        public void ASavedPilotIsUpcomingBeforeADraftedOneAtEqualExperience()
        {
            WingPilotRoster.StartMission(new[] { Saved("HATCH") });
            WingPilotRoster.RecruitManual();
            WingPilotRoster.Select(null);
            Assert.Equal("HATCH", WingPilotRoster.Upcoming.Callsign);
        }

        [Fact]
        public void RenamingALiveSavedPilotKeepsTheSeatAndTheRecord()
        {
            WingPilotRoster.StartMission(new[] { Saved("HATCH") });
            var aircraft = Plane(1);
            WingPilot p = WingPilotRoster.Assign(aircraft);
            p.Xp = 70;
            int v = WingPilotRoster.Version, look = WingPilotRoster.LookVersion;
            CustomPilotRecord renamed = Saved("ZULU");
            renamed.Name = "Z. Ulu";
            Assert.True(WingPilotRoster.UpdateIdentity(p, renamed));
            Assert.Same(p, WingPilotRoster.Of(aircraft));
            Assert.Same(p, WingPilotRoster.FindByCallsign("ZULU"));
            Assert.Null(WingPilotRoster.FindByCallsign("HATCH"));
            Assert.Equal(("ZULU", "Z. Ulu", 70), (p.Callsign, p.Name, p.Xp));
            Assert.NotEqual(v, WingPilotRoster.Version);
            Assert.NotEqual(look, WingPilotRoster.LookVersion);
        }

        [Fact]
        public void ARenameOntoAnotherPilotOfThisMissionIsRefused()
        {
            WingPilotRoster.StartMission(new[] { Saved("HATCH"), Saved("IBIS") });
            WingPilot hatch = WingPilotRoster.FindByCallsign("HATCH");
            Assert.False(WingPilotRoster.UpdateIdentity(hatch, Saved("ibis")));
            Assert.Equal("HATCH", hatch.Callsign);
        }

        [Fact]
        public void ADraftedPilotNeverTakesASavedCallsign()
        {
            string first = WingPilotRoster.RecruitManual().Callsign;
            WingPilotRoster.StartMission(new CustomPilotRecord[0]);
            WingPilotRoster.SavedTaken = c => c == first;
            Assert.NotEqual(first, WingPilotRoster.RecruitManual().Callsign);
            WingPilotRoster.SavedTaken = null;
        }
    }
}
