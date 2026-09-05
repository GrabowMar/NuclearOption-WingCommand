using Xunit;

namespace WingCommand.Tests
{
    public class FormationCollisionTests
    {
        [Fact]
        public void CrossingTrafficTriggersBeforeEnteringCurrentRadius()
        {
            float score = FormationCollision.Threat(200f, 0f, 0f, -40f, 0f, 0f, 70f,
                out float time, out float miss);
            Assert.True(score > 0f);
            Assert.Equal(5f, time);
            Assert.Equal(0f, miss);
        }

        [Fact]
        public void LogClosePassRemainsProtectedEvenAfterVelocityChanges()
        {
            Assert.True(FormationCollision.Threat(13f, 0f, 0f, 30f, 0f, 0f, 70f,
                out _, out _) > 0f);
        }

        [Theory]
        [InlineData(85f, 0f)]
        [InlineData(100f, 30f)]
        [InlineData(400f, -20f)]
        public void SafeParallelDivergingAndDistantTrafficDoNotOverrideSlot(float range, float closing)
        {
            Assert.Equal(0f, FormationCollision.Threat(range, 0f, 0f, closing, 0f, 0f,
                70f, out _, out _));
        }

        [Fact]
        public void HoldTighteningFadesOutDuringRejoinAndOtherRoe()
        {
            Assert.Equal(1f, FormationCollision.HoldBlend(true, 0f, 80f));
            Assert.Equal(0.5f, FormationCollision.HoldBlend(true, 120f, 80f));
            Assert.Equal(0f, FormationCollision.HoldBlend(true, 1000f, 80f));
            Assert.Equal(0f, FormationCollision.HoldBlend(false, 0f, 80f));
        }

        [Fact]
        public void HighCoordinatedDescentRetainsRollTrackingButUnsafeSinkDoesNot()
        {
            Assert.True(FormationSafety.AllowsBankMatch(3000f, -40f, -40f));
            Assert.False(FormationSafety.AllowsBankMatch(200f, -40f, -40f));
            Assert.False(FormationSafety.AllowsBankMatch(3000f, -40f, 0f));
        }

        [Fact]
        public void LeaderRollAcrossInvertedDoesNotFlipSlotFrame()
        {
            Assert.InRange(System.Math.Abs(FormationCollision.SlotBank(179f) -
                FormationCollision.SlotBank(-179f)), 0f, 2.01f);
            Assert.Equal(45f, FormationCollision.SlotBank(45f), 3);
            Assert.Equal(80f, FormationCollision.SlotBank(90f));
        }
    }
}
