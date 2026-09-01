using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.Tests
{
    public class WingWeaponPreferenceTests
    {
        [Fact]
        public void AllCoversEveryEnumValueExactlyOnce()
        {
            var values = (WingWeaponPreference[])Enum.GetValues(typeof(WingWeaponPreference));
            Assert.Equal(values.Length, WingWeaponPreferences.All.Length);
            foreach (WingWeaponPreference v in values)
                Assert.Contains(v, WingWeaponPreferences.All);
        }

        /// <summary>Auto is the stock behaviour, so it is what the selector opens on.</summary>
        [Fact]
        public void AutoLeadsTheSelector()
        {
            Assert.Equal(WingWeaponPreference.Auto, WingWeaponPreferences.All[0]);
        }

        // Every one of these three is a table cell or a fixed-width button. A label that
        // outgrows its column is the failure mode, and the widths below are what the
        // Tactical tab and the docked HUD strip actually reserve.
        [Fact]
        public void EveryPreferenceHasLabelsThatFitTheirColumns()
        {
            foreach (WingWeaponPreference preference in WingWeaponPreferences.All)
            {
                string label = WingWeaponPreferences.Label(preference);
                string shortLabel = WingWeaponPreferences.ShortLabel(preference);
                string hint = WingWeaponPreferences.Hint(preference);

                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.False(string.IsNullOrWhiteSpace(shortLabel));
                Assert.False(string.IsNullOrWhiteSpace(hint));

                Assert.True(label.Length <= 4, $"{preference} label '{label}' is too wide");
                Assert.True(shortLabel.Length <= 3,
                            $"{preference} short label '{shortLabel}' is too wide");
                Assert.True(hint.Length <= 64, $"{preference} hint is too long for one line");
            }
        }

        [Fact]
        public void LabelsAndHintsAreDistinctPerPreference()
        {
            var labels = new HashSet<string>();
            var shortLabels = new HashSet<string>();
            var hints = new HashSet<string>();

            foreach (WingWeaponPreference preference in WingWeaponPreferences.All)
            {
                Assert.True(labels.Add(WingWeaponPreferences.Label(preference)));
                Assert.True(shortLabels.Add(WingWeaponPreferences.ShortLabel(preference)));
                Assert.True(hints.Add(WingWeaponPreferences.Hint(preference)));
            }
        }
    }
}
