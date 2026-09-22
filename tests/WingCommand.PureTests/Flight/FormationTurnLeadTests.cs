using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationTurnLeadTests
    {
        [Theory]
        [InlineData(80f, 20f)]
        [InlineData(250f, 45f)]
        [InlineData(350f, 60f)]
        [InlineData(80f, -20f)]
        [InlineData(250f, -45f)]
        [InlineData(350f, -60f)]
        public void SettledTurnRequestsTheBankNeededForTheLeadersCourseRate(float speed, float bank)
        {
            // Contract with the installed native AutoAim geometry, not a flight-physics simulation.
            float rate = 9.81f * (float)Math.Tan(bank * Math.PI / 180d) / speed;
            float lead = FormationGuidance.TurnLeadDegrees(rate, speed, 75f, 1f);
            Assert.InRange(NativeLevelBank(lead), bank - 0.04f, bank + 0.04f);
        }

        [Fact]
        public void FastJetTurnDoesNotDependOnFallingOutOfFormationToStartBanking()
        {
            const float speed = 250f;
            float rate = 9.81f / speed;
            float legacyLead = rate * 0.15f * 180f / (float)Math.PI;
            float lead = FormationGuidance.TurnLeadDegrees(rate, speed, 75f, 1f);
            Assert.InRange(NativeLevelBank(legacyLead), 1.6f, 1.8f);
            Assert.InRange(NativeLevelBank(lead), 44.96f, 45.04f);
            Assert.InRange(lead, 7.5f, 7.7f);
        }

        [Theory]
        [InlineData(8f)]
        [InlineData(25f)]
        [InlineData(60f)]
        public void FeedforwardRespectsTheAirframeAndTerrainBankCeiling(float bankLimit)
        {
            float lead = FormationGuidance.TurnLeadDegrees(0.2f, 350f, bankLimit, 1f);
            Assert.InRange(NativeLevelBank(lead), bankLimit - 0.04f, bankLimit + 0.04f);
            Assert.InRange(lead, 0f, 15f);
        }

        [Fact]
        public void AcquisitionAndRecoveryRetainTheShortExistingPreview()
        {
            const float rate = 0.04f;
            float legacy = rate * 0.15f * 180f / (float)Math.PI;
            float station = FormationGuidance.TurnLeadDegrees(rate, 250f, 75f, 1f);
            Assert.Equal(legacy, FormationGuidance.TurnLeadDegrees(rate, 250f, 75f, 0f), 5);
            float transition = FormationGuidance.TurnLeadDegrees(rate, 250f, 75f, 0.5f);
            Assert.InRange(transition, legacy, station);
        }

        [Fact]
        public void StraightFlightHasNoArtificialTurnAndExtremeRatesStayBounded()
        {
            Assert.Equal(0f, FormationGuidance.TurnLeadDegrees(0f, 250f, 75f, 1f));
            Assert.Equal(0f, FormationGuidance.TurnLeadDegrees(0.04f, 0f, 75f, 1f));
            float right = FormationGuidance.TurnLeadDegrees(10f, 350f, 88f, 1f);
            float left = FormationGuidance.TurnLeadDegrees(-10f, 350f, 88f, 1f);
            Assert.InRange(right, 14.99f, 15f);
            Assert.Equal(-right, left);
        }

        private static float NativeLevelBank(float headingDegrees)
        {
            double magnitude = Math.Abs(headingDegrees);
            // AutoAim's normalized level waypoint plus its upward bias, projected perpendicular
            // to level velocity by TargetCalc.GetAngleOnAxis (AutopilotPlane lines 67-72, 97).
            double right = Math.Sin(headingDegrees * Math.PI / 180d);
            double up = 1d / Math.Max(magnitude, 5d) +
                Math.Max(0d, Math.Min(1d, magnitude - 15d)) * 0.001d;
            return (float)(Math.Atan2(right, up) * 180d / Math.PI);
        }
    }
}
