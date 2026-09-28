using System;
using Xunit;

namespace WingCommand.FlightSim
{
    public class DefenceScenarioTests
    {
        private const float Dt = SimWing.Dt;

        [Fact]
        public void AWingmanNotchesARadarMissileAndRejoins()
        {
            // Spec M5 §7.3: a head-on radar missile on member 0 of a settled 2-ship; the notch is abeam (90° off).
            var leader = new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f);
            SimWing wing = SimWing.InSlots(leader, SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 2);
            for (int i = 0; i < 15 * 60; i++)
            {
                leader.Step(0f, Dt);
                wing.Step();
            }
            Assert.Equal(BehaviourId.StationKeep, wing.Pilots[0].Mind.Current);

            Vec3 missile = wing.Plants[0].Position + new Vec3(0f, 0f, 8000f);
            float start = wing.Time, end = float.NaN, defendAt = float.NaN, backAt = float.NaN, maxTurn = 0f;
            float minSeparation = float.MaxValue;
            bool otherDefended = false;
            for (int i = 0; i < 120 * 60; i++)
            {
                bool flying = float.IsNaN(end);
                if (flying)
                {
                    Vec3 to = wing.Plants[0].Position - missile;
                    if (to.Length < 150f || wing.Time - start > 20f) end = wing.Time;
                    else
                    {
                        Vec3 vel = to.Normalized * 700f;
                        missile += vel * Dt;
                        wing.Pilots[0].Threat = new MissileThreat { Present = true, Pos = missile, Vel = vel, Seeker = MissileSeeker.Radar };
                    }
                }
                if (!float.IsNaN(end)) wing.Pilots[0].Threat = default;
                leader.Step(0f, Dt);
                wing.Step();
                minSeparation = Math.Min(minSeparation, wing.MinSeparation());
                BehaviourId mind = wing.Pilots[0].Mind.Current;
                if (float.IsNaN(defendAt) && mind == BehaviourId.Defend) defendAt = wing.Time - start;
                if (float.IsNaN(end) || wing.Time - end < 1f)
                    maxTurn = Math.Max(maxTurn, Math.Abs(Scalar.Wrap180(Vec3.HeadingDeg(wing.Plants[0].Velocity))));
                if (!float.IsNaN(end) && float.IsNaN(backAt) && mind == BehaviourId.StationKeep) backAt = wing.Time - end;
                otherDefended |= wing.Pilots[1].Mind.Current == BehaviourId.Defend;
            }
            Assert.True(defendAt <= 1.5f, $"defending {defendAt:0.00} s after the launch");
            Assert.True(maxTurn >= 45f, $"turned only {maxTurn:0} deg toward the notch");
            Assert.True(backAt <= 90f, $"back in the slot {backAt:0} s after the missile");
            Assert.False(otherDefended, "the other member was never threatened");
            Assert.True(minSeparation >= wing.SafeRadius, $"separation {minSeparation:0.0} m");
        }
    }
}
