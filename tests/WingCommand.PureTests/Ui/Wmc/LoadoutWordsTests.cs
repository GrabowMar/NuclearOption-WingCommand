using Xunit;

namespace WingCommand.PureTests
{
    public class LoadoutWordsTests
    {
        private static FitSummary Fit(int stations, int fitted, int blocked = 0, int aam = 0, int agm = 0, int bombs = 0, int cargo = 0,
            int ecm = 0, int msl = 0, float mass = 0f) => new FitSummary
        {
            Stations = stations, Fitted = fitted, Blocked = blocked, Aam = aam, Agm = agm, Bombs = bombs, Cargo = cargo, Ecm = ecm,
            MslDef = msl, Mass = mass,
        };

        [Fact]
        public void NoTemplateIsNamedOneWayEverywhere()
        {
            Assert.Equal("VT-7 · NO TEMPLATE", LoadoutWords.Title("VT-7", null));
            Assert.Equal("NO TEMPLATE", LoadoutWords.Chip(false, false, false, out _));
            Assert.Equal("NO TEMPLATE ›", LoadoutWords.Picker(null));
            Assert.Equal("NO TEMPLATE", LoadoutWords.StationsCaption(Fit(5, 0), false, 0));
        }

        [Fact]
        public void TheBuildCardChipPutsNoTemplateThenEditedThenSupplyFitThenSaved()
        {
            Assert.Equal("EDITED", LoadoutWords.Chip(true, true, true, out string s1));
            Assert.Equal("warn", s1);
            Assert.Equal("SUPPLY FIT", LoadoutWords.Chip(true, false, true, out string s2));
            Assert.Equal("info", s2);
            Assert.Equal("SAVED", LoadoutWords.Chip(true, false, false, out string s3));
            Assert.Equal("live", s3);
            foreach (bool a in new[] { true, false })
                foreach (bool b in new[] { true, false })
                    Assert.True(LoadoutWords.Chip(a, b, !b, out _).Length <= LoadoutWords.ChipChars);
        }

        [Fact]
        public void TheChainNamesTheAirframeTheTemplateAndItsStations()
        {
            Assert.Equal("VT-7 › CAP TWO › 5 STATIONS", LoadoutWords.Chain("VT-7", "CAP TWO", 5));
            Assert.Equal("VT-7 › NO TEMPLATE", LoadoutWords.Chain("VT-7", null, 5));
            Assert.Equal("VT-7 › CAP › 1 STATION", LoadoutWords.Chain("VT-7", "CAP", 1));
        }

        [Fact]
        public void AMirroredStationSaysHowManyPylonsItSets()
        {
            Assert.Equal("Fuselage Pylon ×2", LoadoutWords.StationName("Fuselage Pylon", 2));
            Assert.Equal("Centerline", LoadoutWords.StationName("Centerline", 1));
            Assert.True(LoadoutWords.StationName(new string('A', 60), 4).Length <= LoadoutWords.StationChars);
        }

        [Fact]
        public void TheMetricValuesFitSevenCharacters()
        {
            Assert.Equal("4/5", LoadoutWords.Stations(Fit(5, 4)));
            Assert.Equal("1,240", LoadoutWords.Mass(1240.4f));
            Assert.True(LoadoutWords.Mass(9_999_999f).Length <= LoadoutWords.ValueChars);
            foreach (FitSummary f in new[] { Fit(5, 0), Fit(5, 2, cargo: 2), Fit(5, 2, aam: 4), Fit(5, 2, agm: 4), Fit(5, 2, aam: 2, agm: 2),
                         Fit(5, 1, ecm: 1), Fit(5, 1, msl: 20) })
                Assert.True(LoadoutWords.Role(f).Length <= LoadoutWords.ValueChars, LoadoutWords.Role(f));
        }

        [Fact]
        public void RoleNeedsHalfAgainToDominate()
        {
            Assert.Equal("A-A", LoadoutWords.Role(Fit(5, 3, aam: 4, agm: 2)));
            Assert.Equal("A-G", LoadoutWords.Role(Fit(5, 3, aam: 1, agm: 2, bombs: 2)));
            Assert.Equal("MULTI", LoadoutWords.Role(Fit(5, 3, aam: 3, agm: 3)));
            Assert.Equal("CARGO", LoadoutWords.Role(Fit(5, 1, cargo: 2)));
            Assert.Equal("MSL DEF", LoadoutWords.Role(Fit(5, 1, msl: 20)));
            Assert.Equal("ECM", LoadoutWords.Role(Fit(5, 1, ecm: 1)));
            Assert.Equal("UNARMED", LoadoutWords.Role(Fit(5, 0)));
        }

        [Fact]
        public void TheMetricCaptionsFitTwentyTwoCharactersAndNeverCutAPart()
        {
            string role = LoadoutWords.RoleCaption(Fit(9, 9, aam: 12, agm: 16, bombs: 24, cargo: 4, ecm: 2, msl: 40));
            Assert.True(role.Length <= LoadoutWords.CaptionChars, role);
            Assert.StartsWith("12 AAM · 16 AGM", role);
            Assert.Equal("ALL FITTED", LoadoutWords.StationsCaption(Fit(5, 5), true, 0));
            Assert.Equal("2 EMPTY", LoadoutWords.StationsCaption(Fit(5, 3), true, 0));
            Assert.Equal("1 BLOCKED · 1 EMPTY", LoadoutWords.StationsCaption(Fit(5, 3, blocked: 1), true, 0));
            Assert.Equal("2 EMPTY HERE · 1 EMPTY", LoadoutWords.StationsCaption(Fit(5, 2), true, 2));
            Assert.Equal("STORES ONLY", LoadoutWords.MassCaption);
        }

        [Fact]
        public void TheRowsSayEmptyAndMassInWords()
        {
            Assert.Equal("EMPTY", LoadoutWords.EmptyStore);
            Assert.Equal("—", LoadoutWords.RowMass(0f));
            Assert.Equal("340 kg", LoadoutWords.RowMass(340.2f));
            Assert.Equal("5 STATIONS", LoadoutWords.HardpointsNote(5, 0));
            Assert.Equal("5 STATIONS · 1 BLOCKED", LoadoutWords.HardpointsNote(5, 1));
            Assert.Equal("1 STATION", LoadoutWords.HardpointsNote(1, 0));
        }

        [Fact]
        public void DeleteAsksWithAQuestionMarkAndNamesTheTemplate()
        {
            Assert.Equal("DELETE?", LoadoutWords.DeleteLabel(true));
            Assert.Equal("DELETE", LoadoutWords.DeleteLabel(false));
            Assert.Contains("CAP TWO", LoadoutWords.DeleteAsk("CAP TWO"));
            Assert.Contains("keep their fit", LoadoutWords.DeleteAsk("CAP TWO"));
            Assert.Contains("AUTO", LoadoutWords.Deleted("CAP TWO", true));
            Assert.DoesNotContain("AUTO", LoadoutWords.Deleted("CAP TWO", false));
        }

        [Fact]
        public void TemplateActionsSayWhyTheyCannotRun()
        {
            Assert.Null(LoadoutWords.NewWhy(true, 4));
            Assert.Contains("5", LoadoutWords.NewWhy(true, 5));
            Assert.NotNull(LoadoutWords.NewWhy(false, 0));
            Assert.NotNull(LoadoutWords.CopyWhy(false, 0));
            Assert.NotNull(LoadoutWords.CopyWhy(true, 5));
            Assert.Null(LoadoutWords.CopyWhy(true, 2));
            Assert.NotNull(LoadoutWords.DeleteWhy(false));
            Assert.Null(LoadoutWords.DeleteWhy(true));
        }

        [Fact]
        public void TheLiveryRowNamesTheAirframe()
        {
            Assert.Equal("LIVERY · VT-7", LoadoutWords.LiveryKey("VT-7"));
            Assert.True(LoadoutWords.Livery(new string('x', 80)).Length <= LoadoutWords.LiveryChars);
            Assert.Equal("STANDARD (FACTION)", LoadoutWords.Standard);
        }

        [Fact]
        public void TheAlertCountsStationsThatLaunchEmptyHere()
        {
            Assert.Null(LoadoutWords.EmptyHereAlert(0));
            Assert.Equal("1 STATION LAUNCHES EMPTY HERE · clear it or fit another store", LoadoutWords.EmptyHereAlert(1));
            Assert.StartsWith("2 STATIONS LAUNCH EMPTY HERE", LoadoutWords.EmptyHereAlert(2));
        }

        [Fact]
        public void AClientIsToldTemplatesAreSavedOnThisPc() =>
            Assert.Contains("THIS PC", LoadoutWords.Hint(true, true, true, "VT-7"));

        [Fact]
        public void NoLoadoutWordEndsInAnEllipsis()
        {
            foreach (string s in new[]
                     {
                         LoadoutWords.Title("VT-7", "CAP"), LoadoutWords.Chain("VT-7", "CAP", 5), LoadoutWords.StationName(new string('A', 60), 2),
                         LoadoutWords.RoleCaption(Fit(9, 9, 0, 99, 99, 99, 99, 99, 99)), LoadoutWords.Hint(false, true, false, "VT-7"),
                         LoadoutWords.Hint(false, false, false, null), LoadoutWords.Hint(false, true, true, "VT-7"), LoadoutWords.DeleteAsk("X"),
                         LoadoutWords.Livery(new string('x', 80)),
                     })
            {
                Assert.DoesNotContain("…", s);
                Assert.DoesNotContain("...", s);
            }
        }
    }
}
