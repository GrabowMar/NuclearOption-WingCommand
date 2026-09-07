using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class LoadoutSnapshotTests
    {
        [Fact]
        public void OldInitializedAirframeConfigStillDecodes()
        {
            // DEFAULT templates are no longer auto-seeded, but a leftover config value
            // from an earlier version must not throw if something still reads the codec.
            var initialized = new HashSet<string> { "jet;with|delimiters,%", "helo" };
            string saved = LoadoutTemplateCodec.EncodeInitializedAirframes(initialized);
            Assert.Empty(LoadoutTemplateCodec.Decode(""));
            var reloaded = LoadoutTemplateCodec.DecodeInitializedAirframes(saved);
            Assert.True(initialized.SetEquals(reloaded));
        }

        [Fact]
        public void RecoveredFitSurvivesTemplateEditsAndDeletion()
        {
            var template = new LoadoutTemplateRecord("t1", "jet", "CAP", new[] { "missile", null });
            WingLoadoutChoice fitted = new WingLoadoutChoice(template.Id).Snapshot(template.MountKeys);

            template.SetKeyAt(0, "bomb");
            template.MountKeys.Clear();
            Assert.Equal(new[] { "missile", null }, fitted.FittedKeys);
            Assert.True(fitted.HasSnapshot);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)fitted.FittedKeys)[0] = "changed");
        }

        [Fact]
        public void NativeStandardAndDeliberatelyEmptyFitsCanBePreserved()
        {
            WingLoadoutChoice standard = WingLoadoutChoice.Standard.Snapshot(new[] { "native-store" });
            Assert.False(standard.IsTemplate);
            Assert.True(standard.HasSnapshot);
            Assert.Equal("native-store", standard.FittedKeys[0]);

            WingLoadoutChoice empty = WingLoadoutChoice.Standard.Snapshot(Array.Empty<string>());
            Assert.True(empty.HasSnapshot);
            Assert.Empty(empty.FittedKeys);
            Assert.False(WingLoadoutChoice.Standard.HasSnapshot);
        }

        [Fact]
        public void SelectingAnotherTemplateCreatesANewPlanWithoutOldStores()
        {
            WingLoadoutChoice recovered = new WingLoadoutChoice("old").Snapshot(new[] { "missile" });
            WingLoadoutChoice planned = recovered.WithTemplate("new");
            Assert.Equal("new", planned.TemplateId);
            Assert.False(planned.HasSnapshot);
            Assert.Equal("missile", recovered.FittedKeys[0]);
        }
    }
}
