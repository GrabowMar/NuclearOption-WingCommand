using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationSelectionTests
    {
        private static List<FormationDefinition> Catalog()
        {
            var errors = new List<string>();
            List<FormationDefinition> all = FormationCatalog.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "formations.json")), errors);
            Assert.Empty(errors);
            return all;
        }

        [Fact]
        public void DefaultIsTheConfiguredShapeOrTheFirst()
        {
            Assert.Equal("finger-four-right", new FormationSelection(Catalog(), "finger-four-right").Current.Id);
            Assert.Equal("echelon-right", new FormationSelection(Catalog(), "no-such-shape").Current.Id);
        }

        [Fact]
        public void SelectPicksAShapeByIdAndKeepsTheCurrentOneForAnUnknownId()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right");
            Assert.True(sel.Select("trail"));
            Assert.Equal("trail", sel.Current.Id);
            Assert.False(sel.Select("no-such-shape"));
            Assert.Equal("trail", sel.Current.Id);
        }

        [Fact]
        public void NextShapeCyclesWithinTheFamily()
        {
            var sel = new FormationSelection(Catalog(), "ladder");
            Assert.Equal("echelon-right", sel.NextShape().Id);
            Assert.Equal("echelon-left", sel.NextShape().Id);
        }

        [Fact]
        public void NextFamilyJumpsToTheOtherFamily()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right");
            Assert.Equal("combat-spread", sel.NextFamily().Id);
            Assert.Equal("echelon-right", sel.NextFamily().Id);
        }

        [Fact]
        public void CyclingStaysWithinTheShapesThatSuitTheWing()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right");
            sel.SetUse(FormationUse.Rotary, "staggered-trail");
            Assert.Equal("finger-four-right", sel.Current.Id);   // classic suits a helicopter wing too
            Assert.Equal("staggered-trail", sel.NextFamily().Id);
            Assert.Equal("echelon-staggered", sel.NextShape().Id);
            Assert.Equal("echelon-right", sel.NextFamily().Id);
        }

        [Fact]
        public void ChangingTheUseSwitchesToAShapeThatSuitsIt()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right");
            sel.SetUse(FormationUse.Escort, "high-cover");
            Assert.Equal("high-cover", sel.Current.Id);
            Assert.Equal("low-cover", sel.NextShape().Id);
            sel.SetUse(FormationUse.Jet, "finger-four-right");
            Assert.Equal("finger-four-right", sel.Current.Id);
        }

        [Fact]
        public void LeavingEscortRestoresTheShapeFlownBefore()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right");
            Assert.Equal("finger-four-left", sel.NextShape().Id);
            sel.SetUse(FormationUse.Escort, "high-cover");
            Assert.Equal("high-cover", sel.Current.Id);
            sel.SetUse(FormationUse.Jet, "finger-four-right");
            Assert.Equal("finger-four-left", sel.Current.Id);
        }

        [Fact]
        public void EscortDefaultsToCloseEscortWhenHelicoptersFlyAndHighCoverOtherwise()
        {
            Assert.Equal("close-escort", FormationSelection.EscortDefaultId(anyRotary: true));
            Assert.Equal("high-cover", FormationSelection.EscortDefaultId(anyRotary: false));
        }

        [Fact]
        public void SpacingPresetIsClampedToTheShape()
        {
            var sel = new FormationSelection(Catalog(), "finger-four-right") { Spacing = SpacingPreset.Spread };
            Assert.Equal(160f, sel.SpacingMetres);
            sel.Spacing = SpacingPreset.Standard;
            Assert.Equal(80f, sel.SpacingMetres);
            sel.NextFamily();
            sel.Spacing = SpacingPreset.Close;
            Assert.Equal(160f, sel.SpacingMetres);
        }
    }
}
