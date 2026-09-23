using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationCatalogTests
    {
        private static List<FormationDefinition> BuiltIns(List<string> errors) =>
            FormationCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "formations.json")), errors);

        private static FormationDefinition Single(params SlotDef[] slots) => new FormationDefinition
        {
            Id = "test", Slots = slots, Element = new int[slots.Length],
            SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
        };

        [Fact]
        public void EveryBuiltInFormationParsesAndValidates()
        {
            var errors = new List<string>();
            List<FormationDefinition> all = BuiltIns(errors);
            Assert.Empty(errors);
            Assert.Equal(15, all.Count);
        }

        [Fact]
        public void BuiltInsCoverTheClassicAndTacticalSets()
        {
            List<FormationDefinition> all = BuiltIns(new List<string>());
            string[] ids =
            {
                "echelon-right", "echelon-left", "line-abreast", "trail", "vic", "finger-four-right",
                "finger-four-left", "diamond", "box", "ladder",
                "combat-spread", "fluid-four", "wall", "offset-box", "card",
            };
            foreach (string id in ids) Assert.NotNull(FormationCatalog.Find(all, id));
            Assert.Equal(10, all.Count(d => d.Family == "classic"));
            Assert.Equal(5, all.Count(d => d.Family == "tactical"));
            Assert.True(all.Where(d => d.Family == "tactical").All(d => (d.Modifiers & FormationModifiers.Crossover) != 0));
        }

        [Fact]
        public void SlotsTooCloseAreRejectedWithAReason()
        {
            Assert.False(FormationCatalog.Validate(Single(new SlotDef(0.3f, 0f, 0f)), out string reason));
            Assert.Contains("closer than", reason);
        }

        [Fact]
        public void TooManySlotsAreRejected()
        {
            var slots = new SlotDef[FormationCatalog.MaxSlots + 1];
            for (int i = 0; i < slots.Length; i++) slots[i] = new SlotDef(0f, i + 1f, -i);
            Assert.False(FormationCatalog.Validate(Single(slots), out string reason));
            Assert.Contains("slots", reason);
        }

        [Fact]
        public void MalformedEntriesAreSkippedAndReported()
        {
            const string json = @"{ ""formations"": [
                { ""id"": ""ok"", ""family"": ""classic"", ""spacing"": { ""min"": 40, ""max"": 160, ""default"": 80 },
                  ""slots"": [ { ""right"": 1, ""aft"": 1, ""up"": 0 } ] },
                { ""id"": ""no-slots"", ""family"": ""classic"" },
                { ""id"": ""bad-number"", ""slots"": [ { ""right"": ""far"", ""aft"": 1 } ] } ] }";
            var errors = new List<string>();
            List<FormationDefinition> all = FormationCatalog.Parse(json, errors);
            Assert.Single(all);
            Assert.Equal("ok", all[0].Id);
            Assert.Equal(2, errors.Count);
            Assert.Contains("no-slots", errors[0]);
            Assert.Contains("bad-number", errors[1]);
        }

        [Fact]
        public void InvalidJsonYieldsNoDefinitionsAndOneError()
        {
            var errors = new List<string>();
            Assert.Empty(FormationCatalog.Parse("{ not json", errors));
            Assert.Single(errors);
        }

        [Fact]
        public void UserDefinitionReplacesTheBuiltInWithTheSameId()
        {
            var builtIn = new List<FormationDefinition> { Single(new SlotDef(1f, 1f, 0f)) };
            FormationDefinition user = Single(new SlotDef(-2f, 1f, 0f));
            List<FormationDefinition> merged = FormationCatalog.Merge(builtIn, new List<FormationDefinition> { user });
            Assert.Single(merged);
            Assert.Same(user, FormationCatalog.Find(merged, "TEST"));
        }

        [Fact]
        public void ElementsDefaultToPairsCountingTheLeader()
        {
            const string json = @"{ ""formations"": [ { ""id"": ""pairs"", ""slots"": [
                { ""right"": -1, ""aft"": 1 }, { ""right"": 1, ""aft"": 1 }, { ""right"": 2, ""aft"": 2 } ] } ] }";
            FormationDefinition d = FormationCatalog.Parse(json, new List<string>())[0];
            Assert.Equal(new[] { 0, 1, 1 }, d.Element);
            Assert.Equal(80f, d.SpacingDefault);
        }

        [Fact]
        public void ModifiersAreReadByNameAndUnknownOnesRejected()
        {
            const string json = @"{ ""formations"": [
                { ""id"": ""a"", ""modifiers"": [""turnCompress"", ""terrainFlatten""], ""slots"": [ { ""right"": 1, ""aft"": 1 } ] },
                { ""id"": ""b"", ""modifiers"": [""teleport""], ""slots"": [ { ""right"": 1, ""aft"": 1 } ] } ] }";
            var errors = new List<string>();
            List<FormationDefinition> all = FormationCatalog.Parse(json, errors);
            Assert.Equal(FormationModifiers.TurnCompress | FormationModifiers.TerrainFlatten, all[0].Modifiers);
            Assert.Single(all);
            Assert.Contains("teleport", errors[0]);
        }

        [Fact]
        public void SpacingIsClampedToTheDefinitionRange()
        {
            FormationDefinition d = Single(new SlotDef(1f, 1f, 0f));
            Assert.Equal(160f, d.ClampSpacing(1000f));
            Assert.Equal(40f, d.ClampSpacing(5f));
        }
    }
}
