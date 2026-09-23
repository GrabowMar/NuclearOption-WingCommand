using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class CollisionBiasTests
    {
        private const float Dt = 1f / 60f;
        private static readonly Vec3 North = new Vec3(0f, 0f, 200f);

        private static CollisionBody Body(int rank, Vec3 pos, Vec3 vel) =>
            new CollisionBody { Pos = pos, Vel = vel, Radius = 8f, Rank = rank };

        private static CollisionBias Run(float seconds, float spacing, params CollisionBody[] bodies)
        {
            var bias = new CollisionBias(bodies.Length);
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) bias.Update(bodies, bodies.Length, spacing, Dt);
            return bias;
        }

        [Fact]
        public void DivergingPairGetsNoBias()
        {
            CollisionBias b = Run(1f, 80f, Body(1, new Vec3(0f, 2000f, 0f), North),
                Body(2, new Vec3(40f, 2000f, 0f), North + new Vec3(5f, 0f, 0f)));
            Assert.Equal(Vec3.Zero, b.Bias[0]);
            Assert.Equal(Vec3.Zero, b.Bias[1]);
        }

        [Fact]
        public void ConvergingPairBiasesOnlyTheHigherRankAwayFromTheOther()
        {
            // Closest approach 24 m in 8 s, inside R = max(2·8 + 15, 0.35·80) = 31 m.
            CollisionBias b = Run(2f, 80f, Body(1, new Vec3(0f, 2000f, 0f), North),
                Body(2, new Vec3(40f, 2000f, 0f), North + new Vec3(-2f, 0f, 0f)));
            Assert.Equal(Vec3.Zero, b.Bias[0]);
            Assert.True(b.Bias[1].X > 0.05f * Scalar.G, $"{b.Bias[1]}");
            Assert.True(Math.Abs(b.Bias[1].Y) < 1e-3f && Math.Abs(b.Bias[1].Z) < 1e-3f);
        }

        [Fact]
        public void BiasIsCappedAtHalfAGOutsideAnEmergency()
        {
            // Rank 3 converges on both others with closest approach in 5 s (not imminent): 0.45 g + 0.40 g upward.
            CollisionBias b = Run(3f, 80f,
                Body(1, new Vec3(0f, 2000f, 0f), North),
                Body(2, new Vec3(-50f, 1997f, 0f), North + new Vec3(10f, 0f, 0f)),
                Body(3, new Vec3(50f, 2003f, 0f), North + new Vec3(-10f, 0f, 0f)));
            Assert.False(b.Emergency[2]);
            Assert.InRange(b.Bias[2].Length, 0.45f * Scalar.G, CollisionBias.BiasG * Scalar.G + 1e-3f);
        }

        [Fact]
        public void ImminentCollisionAllowsUpToTwoG()
        {
            CollisionBias b = Run(2f, 80f, Body(1, new Vec3(0f, 2000f, 0f), North), Body(2, new Vec3(5f, 2000f, 0f), North));
            Assert.True(b.Emergency[1]);
            Assert.InRange(b.Bias[1].Length, CollisionBias.BiasG * Scalar.G, CollisionBias.EmergencyG * Scalar.G + 1e-3f);
            Assert.True(b.Bias[1].X > 0f);
        }

        [Fact]
        public void BiasDecaysWithTheFilterOnceTheThreatPasses()
        {
            var bodies = new[] { Body(1, new Vec3(0f, 2000f, 0f), North), Body(2, new Vec3(5f, 2000f, 0f), North) };
            var bias = new CollisionBias(2);
            for (int i = 0; i < 120; i++) bias.Update(bodies, 2, 80f, Dt);
            float start = bias.Bias[1].Length;
            bodies[1].Pos = new Vec3(500f, 2000f, 0f);
            for (int i = 0; i < 24; i++) bias.Update(bodies, 2, 80f, Dt);   // 0.4 s: one time constant
            Assert.InRange(bias.Bias[1].Length, start * 0.3679f - 0.05f, start * 0.3679f + 0.05f);
        }

        [Fact]
        public void CoincidentPairGetsAFiniteBias()
        {
            CollisionBias b = Run(1f, 80f, Body(1, new Vec3(0f, 2000f, 0f), North), Body(2, new Vec3(0f, 2000f, 0f), North));
            Assert.True(Scalar.IsFinite(b.Bias[1].X) && Scalar.IsFinite(b.Bias[1].Y) && b.Bias[1].Length > 0f);
        }
    }
}
