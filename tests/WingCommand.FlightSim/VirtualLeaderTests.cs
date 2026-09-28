using Xunit;

namespace WingCommand.FlightSim
{
    public class VirtualLeaderTests
    {
        [Fact]
        public void SlotAccelerationMatchesTheVelocityDerivativeDuringARollIn()
        {
            const float dt = 1f / 60f;
            var leader = new VirtualLeader(new Vec3(0f, 2000f, 0f), 200f, 0f);
            leader.Step(0f, dt);
            // Mid roll-in the turn rate is changing, so the constant-turn formula alone is wrong.
            for (int i = 0; i < 20; i++) leader.Step(60f, dt);
            RefState before = leader.Slot(60f, 20f, 0f);
            leader.Step(60f, dt);
            RefState after = leader.Slot(60f, 20f, 0f);
            Vec3 derivative = (after.Vel - before.Vel) / dt;
            Assert.True((derivative - after.Acc).Length < 0.5f,
                $"finite difference {derivative} vs reported {after.Acc}");
        }
    }
}
