using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>R7: the saved pilots (research squadron-studio §2.1, lifecycle §6.7): identity and look persist, a rename happens in
    /// place, the store holds 24, a file is cleaned on load, and a service record grows once per mission.</summary>
    public class SavedPilotStoreTests
    {
        private static CustomPilotRecord Draft(string callsign, string name = "O. Bae") =>
            new CustomPilotRecord { Callsign = callsign, Name = name, Persona = ChatterPersona.Calm, Background = "Flies low." };

        private static SavedPilotStore With(params string[] callsigns)
        {
            var s = new SavedPilotStore();
            foreach (string c in callsigns) Assert.True(s.Save(Draft(c), null, null, out string why), why);
            return s;
        }

        private static string Json(params string[] callsigns)
        {
            var list = callsigns.Select(c => Draft(c)).ToList();
            return CustomPilotCodec.Encode(list);
        }

        [Fact]
        public void AFileIsCleanedOnLoad()
        {
            var problems = new List<string>();
            SavedPilotStore s = SavedPilotStore.FromJson(
                "{\"pilots\":[{\"callsign\":\"  hatch!! \",\"name\":\"Łucja   Bae\",\"background\":\"Wait…\",\"xp\":900}]}", problems);
            CustomPilotRecord r = Assert.Single(s.Records);
            Assert.Equal("HATCH", r.Callsign);
            Assert.Equal("Lucja Bae", r.Name);
            Assert.Equal("Wait...", r.Background);
            Assert.True(r.HasCustomPortrait);
        }

        [Fact]
        public void ADuplicateCallsignLoadsOnceWithAReason()
        {
            var problems = new List<string>();
            SavedPilotStore s = SavedPilotStore.FromJson(Json("HATCH", "hatch", "IBIS"), problems);
            Assert.Equal(new[] { "HATCH", "IBIS" }, s.Records.Select(r => r.Callsign));
            Assert.Single(problems);
        }

        [Fact]
        public void AFileThatIsNotJsonLoadsNothingAndSaysWhy()
        {
            var problems = new List<string>();
            Assert.Empty(SavedPilotStore.FromJson("{ this is not json", problems).Records);
            Assert.Single(problems);
            problems.Clear();
            Assert.Empty(SavedPilotStore.FromJson(null, problems).Records);
            Assert.Empty(problems);
        }

        [Fact]
        public void TheStoreHoldsAtMostTwentyFourPilots()
        {
            var problems = new List<string>();
            string[] many = Enumerable.Range(0, 30).Select(i => "P" + i).ToArray();
            SavedPilotStore s = SavedPilotStore.FromJson(Json(many), problems);
            Assert.Equal(SavedPilotStore.Max, s.Records.Count);
            Assert.Single(problems);
            Assert.False(s.Save(Draft("NEW"), null, null, out string why));
            Assert.False(string.IsNullOrEmpty(why));
            // A full store still saves an edit.
            Assert.True(s.Save(Draft("P3", "Edited"), "P3", null, out why), why);
        }

        [Fact]
        public void SavingANewPilotAddsItAndBumpsTheVersion()
        {
            var s = new SavedPilotStore();
            int v = s.Version;
            Assert.True(s.Save(Draft("hatch"), null, null, out _));
            Assert.Equal("HATCH", s.Records[0].Callsign);
            Assert.NotEqual(v, s.Version);
        }

        [Fact]
        public void SavingUnderTheOriginalCallsignRenamesInPlace()
        {
            SavedPilotStore s = With("ALPHA", "HATCH", "IBIS");
            Assert.True(s.Save(Draft("ZULU"), "hatch", null, out string why), why);
            Assert.Equal(new[] { "ALPHA", "ZULU", "IBIS" }, s.Records.Select(r => r.Callsign));
        }

        [Fact]
        public void ARenameOntoAnotherSavedCallsignIsRefused()
        {
            SavedPilotStore s = With("HATCH", "IBIS");
            Assert.False(s.Save(Draft("IBIS"), "HATCH", null, out string why));
            Assert.False(string.IsNullOrEmpty(why));
            Assert.Equal(new[] { "HATCH", "IBIS" }, s.Records.Select(r => r.Callsign));
        }

        [Fact]
        public void ACallsignUsedByALiveUnsavedPilotIsRefused()
        {
            var s = new SavedPilotStore();
            Assert.False(s.Save(Draft("HATCH"), null, c => c == "HATCH", out string why));
            Assert.False(string.IsNullOrEmpty(why));
            Assert.Empty(s.Records);
        }

        [Fact]
        public void AnEmptyCallsignIsRefused()
        {
            var s = new SavedPilotStore();
            Assert.False(s.Save(Draft(" !! "), null, null, out string why));
            Assert.False(string.IsNullOrEmpty(why));
        }

        [Fact]
        public void SavingKeepsTheStoredServiceRecord()
        {
            SavedPilotStore s = With("HATCH");
            s.Records[0].Missions = 3;
            s.Records[0].Kills = 7;
            s.Records[0].Sorties = 5;
            s.Records[0].Xp = 400;
            CustomPilotRecord d = Draft("HATCH", "Renamed");
            d.Xp = 9999;
            d.Kills = 99;
            Assert.True(s.Save(d, "HATCH", null, out _));
            CustomPilotRecord r = s.Records[0];
            Assert.Equal("Renamed", r.Name);
            Assert.Equal((3, 7, 5, 400), (r.Missions, r.Kills, r.Sorties, r.Xp));
        }

        [Fact]
        public void ANewPilotStartsWithAnEmptyServiceRecordAndAFrozenFace()
        {
            CustomPilotRecord d = Draft("HATCH", "O. Bae");
            d.Xp = 500;
            var s = new SavedPilotStore();
            Assert.True(s.Save(d, null, null, out _));
            CustomPilotRecord r = s.Records[0];
            Assert.Equal((0, 0, 0, 0), (r.Missions, r.Kills, r.Sorties, r.Xp));
            Assert.True(r.HasCustomPortrait);
            Assert.Equal(PilotPortraitGenerator.Select("O. Bae|HATCH"), r.Selection);
        }

        [Fact]
        public void TheStoreKeepsItsOwnCopyOfADraft()
        {
            var s = new SavedPilotStore();
            CustomPilotRecord d = Draft("HATCH");
            Assert.True(s.Save(d, null, null, out _));
            d.Name = "Changed after saving";
            Assert.Equal("O. Bae", s.Records[0].Name);
        }

        [Fact]
        public void RemovingAPilotIgnoresCaseAndLeavesTheOthersInOrder()
        {
            SavedPilotStore s = With("ALPHA", "HATCH", "IBIS");
            int v = s.Version;
            Assert.True(s.Remove("hatch"));
            Assert.Equal(new[] { "ALPHA", "IBIS" }, s.Records.Select(r => r.Callsign));
            Assert.NotEqual(v, s.Version);
            Assert.False(s.Remove("NOBODY"));
        }

        [Fact]
        public void CloneTakesTheFirstFreeSuffixWithinFourteenCharacters()
        {
            SavedPilotStore s = With("HATCH", "HATCH-2", "ABCDEFGHIJKLMN");
            Assert.Equal("HATCH-3", s.CloneName("HATCH", null));
            Assert.Equal("ABCDEFGHIJKL-2", s.CloneName("ABCDEFGHIJKLMN", null));
            Assert.Equal("IBIS-3", s.CloneName("IBIS", c => c == "IBIS-2"));
        }

        [Fact]
        public void ImportAddsOnlyUnknownCallsignsUpToTheCapWithAnEmptyRecord()
        {
            SavedPilotStore s = With("HATCH");
            var incoming = new List<CustomPilotRecord> { Draft("HATCH"), Draft("IBIS"), Draft("ibis"), Draft("TORCH") };
            incoming[1].Xp = 800;
            incoming[1].Kills = 12;
            s.Import(incoming, null, out int added, out int skipped);
            Assert.Equal(2, added);
            Assert.Equal(2, skipped);
            CustomPilotRecord ibis = s.Find("IBIS");
            Assert.Equal((0, 0, 0), (ibis.Xp, ibis.Kills, ibis.Missions));
        }

        [Fact]
        public void ALegacyZeroNineFileImportsWithItsPortrait()
        {
            CustomPilotPayload p = CustomPilotCodec.Decode("{\"pilots\":[{\"callsign\":\"OLD\",\"name\":\"Old Timer\",\"face\":3,\"hair\":2,\"uniform\":1,\"backdrop\":1}]}");
            var s = new SavedPilotStore();
            s.Import(p.Pilots, null, out int added, out _);
            Assert.Equal(1, added);
            Assert.Equal(p.Pilots[0].Selection, s.Find("OLD").Selection);
        }

        [Fact]
        public void EncodeAndDecodeRoundTripKeepsTheServiceRecordAndTheLook()
        {
            SavedPilotStore s = With("HATCH");
            CustomPilotRecord r = s.Records[0];
            r.Missions = 4;
            r.Sorties = 6;
            r.Kills = 9;
            r.Xp = 360;
            var problems = new List<string>();
            SavedPilotStore back = SavedPilotStore.FromJson(s.ToJson(), problems);
            Assert.Empty(problems);
            CustomPilotRecord b = back.Records[0];
            Assert.Equal((4, 6, 9, 360), (b.Missions, b.Sorties, b.Kills, b.Xp));
            Assert.Equal(r.Selection, b.Selection);
            Assert.Equal(r.Persona, b.Persona);
            Assert.Equal(r.Background, b.Background);
        }

        [Fact]
        public void TallyAddsSortiesAndKillsCountsAMissionAndKeepsTheBestXp()
        {
            SavedPilotStore s = With("HATCH", "IBIS");
            s.Records[0].Xp = 500;
            bool changed = s.Tally(new[]
            {
                new MissionLine { Callsign = "hatch", Seated = true, Sorties = 2, Kills = 3, Xp = 130 },
                new MissionLine { Callsign = "IBIS", Seated = true, Sorties = 1, Kills = 0, Xp = 40 },
            });
            Assert.True(changed);
            CustomPilotRecord h = s.Find("HATCH"), i = s.Find("IBIS");
            Assert.Equal((1, 2, 3, 500), (h.Missions, h.Sorties, h.Kills, h.Xp));
            Assert.Equal((1, 1, 0, 40), (i.Missions, i.Sorties, i.Kills, i.Xp));
        }

        [Fact]
        public void TallyIgnoresAPilotWhoNeverSatAndOneNotSaved()
        {
            SavedPilotStore s = With("HATCH");
            int v = s.Version;
            Assert.False(s.Tally(new[]
            {
                new MissionLine { Callsign = "HATCH", Seated = false, Xp = 50 },
                new MissionLine { Callsign = "DRAFTED", Seated = true, Sorties = 1 },
            }));
            Assert.Equal(0, s.Find("HATCH").Missions);
            Assert.Equal(v, s.Version);
        }
    }
}
