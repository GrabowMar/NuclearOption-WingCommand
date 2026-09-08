using System;
using System.Numerics;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationProportionalTests
    {
        [Theory]
        [InlineData(-20f)]
        [InlineData(-1f)]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(20f)]
        public void StationCorrectionsScaleWithMovementAndDampClosure(float gap)
        {
            var velocity = new Vector2(0f, 200f);
            var command = FormationGuidance.Horizontal(new Vector2(gap, 0f), velocity,
                velocity, Vector2.UnitY, new Vector2(gap, 400f), velocity,
                Math.Abs(gap), 700f, 200f, 0f, 1f, 1f);
            Assert.Equal(1.35f * gap, command.Aim.X, 4);
            Assert.Equal(700f, command.Aim.Y);
            Assert.Equal(gap, FormationControlRules.KinematicVerticalCorrection(
                gap, 0f, 200f, 1f, 4f, 1f, 1f));

            var closing = FormationGuidance.Horizontal(new Vector2(gap, 0f),
                velocity + new Vector2(gap, 0f), velocity, Vector2.UnitY,
                new Vector2(gap, 400f), velocity, Math.Abs(gap), 700f, 200f, 0f, 1f, 1f);
            Assert.True(gap == 0f || closing.Correction.X * gap < 0f);
            float vertical = FormationControlRules.KinematicVerticalCorrection(
                gap, gap, 200f, 1f, 4f, 1f, 1f);
            Assert.True(gap == 0f || vertical * gap < 0f);
        }

        [Fact]
        public void LargeMovementsRemainBounded()
        {
            var velocity = new Vector2(0f, 200f);
            var command = FormationGuidance.Horizontal(new Vector2(10000f, 0f), velocity,
                velocity, Vector2.UnitY, new Vector2(10000f, 400f), velocity,
                10000f, 700f, 200f, 0f, 1f, 1f);
            Assert.Equal(command.MaxCorrection, command.Correction.Length(), 3);
            Assert.Equal(200f, FormationControlRules.KinematicVerticalCorrection(
                10000f, 0f, 200f, 1f, 4f, 1f, 1f));
        }
    }
}
