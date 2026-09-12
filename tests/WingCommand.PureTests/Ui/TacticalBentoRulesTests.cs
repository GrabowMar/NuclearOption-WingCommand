using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class TacticalBentoRulesTests
    {
        [Theory]
        [InlineData(0f, "0 m")]
        [InlineData(-50f, "0 m")]
        [InlineData(float.NaN, "0 m")]
        [InlineData(450f, "450 m")]
        [InlineData(999f, "999 m")]
        [InlineData(1000f, "1.0 km")]
        [InlineData(4820f, "4.8 km")]
        [InlineData(12500f, "12.5 km")]
        public void FormatDistance_FormatsMetersAndKilometersAccurately(float input, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatDistance(input));
        }

        [Theory]
        [InlineData(0f, "0 m/s")]
        [InlineData(0.2f, "0 m/s")]
        [InlineData(-0.4f, "0 m/s")]
        [InlineData(140.2f, "+140 m/s")]
        [InlineData(-65.4f, "-65 m/s")]
        [InlineData(float.NaN, "0 m/s")]
        public void FormatClosingSpeed_DistinguishesClosingAndSeparating(float input, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatClosingSpeed(input));
        }

        [Theory]
        [InlineData(0f, "0 m")]
        [InlineData(-100f, "0 m")]
        [InlineData(850f, "850 m")]
        [InlineData(2400f, "2,400 m")]
        [InlineData(12500f, "12,500 m")]
        public void FormatAltitude_FormatsWithDigitGrouping(float input, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatAltitude(input));
        }

        [Theory]
        [InlineData(0f, "0 kt")]
        [InlineData(-10f, "0 kt")]
        [InlineData(380.4f, "380 kt")]
        [InlineData(450.9f, "451 kt")]
        public void FormatSpeed_FormatsKnots(float input, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatSpeed(input));
        }

        [Fact]
        public void FormatSlotDeviation_IdentifiesLeadAndFormationStatus()
        {
            Assert.Equal("LEAD", TacticalBentoRules.FormatSlotDeviation(100f, isFlightLead: true, inFormation: true));
            Assert.Equal("INDEP", TacticalBentoRules.FormatSlotDeviation(15f, isFlightLead: false, inFormation: false));
            Assert.Equal("12m (TIGHT)", TacticalBentoRules.FormatSlotDeviation(12f, isFlightLead: false, inFormation: true));
            Assert.Equal("45m (FORM)", TacticalBentoRules.FormatSlotDeviation(45f, isFlightLead: false, inFormation: true));
            Assert.Equal("85m (WIDE)", TacticalBentoRules.FormatSlotDeviation(85f, isFlightLead: false, inFormation: true));
        }

        [Fact]
        public void ResolveThreat_PrioritizesMissilesAndDamageProperly()
        {
            var mws = TacticalBentoRules.ResolveThreat(missileWarned: true, missileSeeker: "IR", integrityFraction: 1f, isDefending: false);
            Assert.Equal(BentoThreatLevel.Danger, mws.Level);
            Assert.Equal("EVADING MISSILE [IR]", mws.StatusText);

            var def = TacticalBentoRules.ResolveThreat(missileWarned: false, missileSeeker: null, integrityFraction: 1f, isDefending: true);
            Assert.Equal(BentoThreatLevel.Danger, def.Level);
            Assert.Equal("DEFENSIVE JINKING", def.StatusText);

            var crit = TacticalBentoRules.ResolveThreat(missileWarned: false, missileSeeker: null, integrityFraction: 0.35f, isDefending: false);
            Assert.Equal(BentoThreatLevel.Danger, crit.Level);
            Assert.Equal("HULL CRITICAL 35%", crit.StatusText);

            var dmg = TacticalBentoRules.ResolveThreat(missileWarned: false, missileSeeker: null, integrityFraction: 0.72f, isDefending: false);
            Assert.Equal(BentoThreatLevel.Caution, dmg.Level);
            Assert.Equal("HULL DAMAGED 72%", dmg.StatusText);

            var clr = TacticalBentoRules.ResolveThreat(missileWarned: false, missileSeeker: null, integrityFraction: 1f, isDefending: false);
            Assert.Equal(BentoThreatLevel.Clear, clr.Level);
            Assert.Equal("THREAT: CLEAR", clr.StatusText);
        }

        [Fact]
        public void FormatSingleMemberStores_GroupsMultiPylonAndDetectsWinchester()
        {
            var stores = new List<BentoRawStore>
            {
                new BentoRawStore("IRM-S", 1, 1, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("IRM-S", 1, 1, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("AGM-48", 4, 4, isMissile: false, isGun: false, isJammer: false, isBomb: true),
                new BentoRawStore("20mm_Cannon", 480, 600, isMissile: false, isGun: true, isJammer: false, isBomb: false),
            };

            TacticalBentoRules.FormatSingleMemberStores(stores, out string l1, out string l2, out string l3, out bool winchester);

            Assert.False(winchester);
            Assert.Equal("2x IRM-S [2]", l1);
            Assert.Equal("AGM-48 [4]", l2);
            Assert.Equal("20MM CANNON [480]", l3);
        }

        [Fact]
        public void FormatSingleMemberStores_EmptyOrDepleted_FlagsWinchester()
        {
            var empty = new List<BentoRawStore>();
            TacticalBentoRules.FormatSingleMemberStores(empty, out string l1, out _, out _, out bool winchester);
            Assert.True(winchester);
            Assert.Equal("NO STORES MOUNTED", l1);

            var depleted = new List<BentoRawStore>
            {
                new BentoRawStore("IRM-S", 0, 1, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("20mm", 0, 600, isMissile: false, isGun: true, isJammer: false, isBomb: false),
            };
            TacticalBentoRules.FormatSingleMemberStores(depleted, out _, out _, out _, out bool winchesterDepleted);
            Assert.True(winchesterDepleted);
        }

        [Fact]
        public void FormatFlightStores_AggregatesPoolAccurately()
        {
            var pool = new List<BentoRawStore>
            {
                // Wingman 1
                new BentoRawStore("Fox2", 2, 2, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("Gun", 500, 500, isMissile: false, isGun: true, isJammer: false, isBomb: false),
                // Wingman 2
                new BentoRawStore("Fox2", 2, 2, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("AGM", 4, 4, isMissile: false, isGun: false, isJammer: false, isBomb: true),
                new BentoRawStore("Gun", 450, 500, isMissile: false, isGun: true, isJammer: false, isBomb: false),
                new BentoRawStore("ECM_Pod", 1, 1, isMissile: false, isGun: false, isJammer: true, isBomb: false),
            };

            TacticalBentoRules.FormatFlightStores(pool, out string l1, out string l2, out string l3, out bool winchester);

            Assert.False(winchester);
            Assert.Equal("MISSILES : 4 READY", l1);
            Assert.Equal("STRIKE   : 4 BOMBS / AGMs", l2);
            Assert.Equal("CANNON   : 950 RDS · 1x ECM", l3);
        }

        [Fact]
        public void FormatFlightPosture_GeneratesCleanMilitaryString()
        {
            string posture = TacticalBentoRules.FormatFlightPosture(engaging: 2, inFormation: 1, defending: 0, other: 0);
            Assert.Equal("POSTURE: 2 ATK · 1 FORM · 0 DEF", posture);

            string withRtb = TacticalBentoRules.FormatFlightPosture(engaging: 1, inFormation: 1, defending: 1, other: 1);
            Assert.Equal("POSTURE: 1 ATK · 1 FORM · 1 DEF · 1 RTB/REFIT", withRtb);

            string standby = TacticalBentoRules.FormatFlightPosture(0, 0, 0, 0);
            Assert.Equal("POSTURE: STANDBY", standby);
        }

        [Theory]
        [InlineData(1.0f, "FUEL: 100%")]
        [InlineData(0.854f, "FUEL: 85%")]
        [InlineData(0.146f, "FUEL: 15%")]
        [InlineData(0.0f, "FUEL: 0%")]
        [InlineData(-0.5f, "FUEL: 0%")]
        [InlineData(float.NaN, "FUEL: 0%")]
        public void FormatSingleFuel_FormatsPercentagesAccurately(float input, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatSingleFuel(input));
        }

        [Theory]
        [InlineData(0.45f, 0.72f, "FUEL: 45% MIN")]
        [InlineData(0.0f, 0.50f, "FUEL: 0% MIN")]
        [InlineData(-0.1f, 0.50f, "FUEL: 0% MIN")]
        [InlineData(float.NaN, 0.50f, "FUEL: 0% MIN")]
        public void FormatFlightFuel_FormatsMinimumAccurately(float min, float avg, string expected)
        {
            Assert.Equal(expected, TacticalBentoRules.FormatFlightFuel(min, avg));
        }

        [Fact]
        public void FormatSingleMemberStores_CalculatesTotalAmmoAccurately()
        {
            var stores = new List<BentoRawStore>
            {
                new BentoRawStore("IRM-S", 2, 2, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("AGM-48", 4, 4, isMissile: false, isGun: false, isJammer: false, isBomb: true),
                new BentoRawStore("20mm", 450, 500, isMissile: false, isGun: true, isJammer: false, isBomb: false),
                new BentoRawStore("ECM", 1, 1, isMissile: false, isGun: false, isJammer: true, isBomb: false),
            };

            TacticalBentoRules.FormatSingleMemberStores(stores, out _, out _, out _, out bool winchester, out int totalAmmo);
            Assert.False(winchester);
            Assert.Equal(456, totalAmmo);
        }

        [Fact]
        public void FormatFlightStores_CalculatesTotalAmmoAccurately()
        {
            var stores = new List<BentoRawStore>
            {
                new BentoRawStore("Fox2", 4, 4, isMissile: true, isGun: false, isJammer: false, isBomb: false),
                new BentoRawStore("Bomb", 2, 2, isMissile: false, isGun: false, isJammer: false, isBomb: true),
                new BentoRawStore("Cannon", 600, 600, isMissile: false, isGun: true, isJammer: false, isBomb: false),
            };

            TacticalBentoRules.FormatFlightStores(stores, out _, out _, out _, out bool winchester, out int totalAmmo);
            Assert.False(winchester);
            Assert.Equal(606, totalAmmo);
        }
    }
}

