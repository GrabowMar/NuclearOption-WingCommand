using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationWingTests
    {
        private const float Dt = 1f / 60f;
        private static readonly Vec3 Far = new Vec3(0f, 2000f, -5000f);

        private static FormationDefinition FingerFour() => new FormationDefinition
        {
            Id = "ff", Slots = new[] { new SlotDef(-1f, 1f, 0f), new SlotDef(1f, 1f, 0f), new SlotDef(2f, 2f, 0f) },
            Element = new[] { 0, 1, 1 }, SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
        };

        private static AnchorSample Leader(bool present = true, float speed = 200f, bool airborne = true) => new AnchorSample
        {
            Pos = new Vec3(0f, 2000f, 0f), Vel = new Vec3(0f, 0f, speed), Present = present, Airborne = airborne,
        };

        private static WingMemberInput[] Members(params Vec3[] positions)
        {
            var members = new WingMemberInput[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                members[i] = new WingMemberInput
                {
                    State = TestStates.Flying(positions[i], new Vec3(0f, 0f, 200f)),
                    Capability = new MemberCapability { MaxSpeed = 255f, MinSpeed = 80f },
                    Radius = 8f,
                };
            return members;
        }

        private static WingFrame Run(FormationWing wing, AnchorSample leader, WingMemberInput[] members, float seconds)
        {
            WingFrame frame = null;
            for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds / Dt)); i++)
                frame = wing.Update(leader, members, members.Length, float.NaN, 60f, 8f, Dt);
            return frame;
        }

        /// <summary>A leader flying north at <paramref name="speed"/> from the origin for <paramref name="seconds"/>, then
        /// holding position; two helicopters trailing from where it started. Returns the wing.</summary>
        private static FormationWing TrailRun(float speed, float seconds, float holdSeconds, WingMemberInput[] members,
            Func<int, float, Role> role = null)
        {
            var wing = new FormationWing(FingerFour(), 80f);
            Vec3 pos = new Vec3(0f, 2000f, 0f);
            int ticks = (int)Math.Round((seconds + holdSeconds) / Dt);
            for (int i = 0; i < ticks; i++)
            {
                float t = i * Dt;
                float v = t < seconds ? speed : 0f;
                pos += new Vec3(0f, 0f, v * Dt);
                for (int k = 0; k < members.Length; k++) members[k].Role = role?.Invoke(k, t) ?? Role.Trail;
                wing.Update(new AnchorSample { Pos = pos, Vel = new Vec3(0f, 0f, v), Present = true, Airborne = true },
                    members, members.Length, float.NaN, 60f, 8f, Dt);
            }
            return wing;
        }

        private static WingMemberInput[] Helos(int n)
        {
            WingMemberInput[] m = Members(new Vec3[n]);
            for (int i = 0; i < n; i++)
            {
                m[i].State = TestStates.Flying(new Vec3(0f, 1850f, -100f * i), new Vec3(0f, 0f, 60f));
                m[i].Capability = new MemberCapability { MaxSpeed = 60f, MinSpeed = 0f };
            }
            return m;
        }

        [Fact]
        public void TrailingMembersFlyTheAnchorsRouteOneSpacingApartBelowIt()
        {
            FormationWing wing = TrailRun(200f, 20f, 0f, Helos(2));
            RefState lead = wing.Frame.TrailRef[0], next = wing.Frame.TrailRef[1];
            Assert.InRange(lead.Pos.Z, 1150f, 1250f);                            // 20 s at 60 m/s along the route
            Assert.Equal(80f, lead.Pos.Z - next.Pos.Z, 0);                         // one spacing behind on the route
            Assert.Equal(2000f - FormationWing.TrailBelow, lead.Pos.Y, 1);
            Assert.Equal(60f, lead.Vel.Z, 1);
            Assert.True(Math.Abs(lead.Pos.X) > 1f && Math.Sign(lead.Pos.X) != Math.Sign(next.Pos.X), "staggered");
        }

        [Fact]
        public void TrailStopsShortOfAnAnchorThatStopped()
        {
            FormationWing wing = TrailRun(200f, 5f, 60f, Helos(1));   // the anchor stops 1000 m north
            Assert.Equal(1000f - FormationWing.TrailGapSpacings * 80f, wing.Frame.TrailRef[0].Pos.Z, 0);
            Assert.Equal(0f, wing.Frame.TrailRef[0].Vel.Length, 2);
        }

        [Fact]
        public void TheNextMemberTakesTheLeadWithoutAJump()
        {
            // #1 leaves the trail at 20 s; #2's reference must keep moving smoothly, not leap one spacing forward.
            WingMemberInput[] helos = Helos(2);
            FormationWing before = TrailRun(200f, 20f, 0f, Helos(2));
            FormationWing after = TrailRun(200f, 20f + Dt, 0f, helos, (k, t) => k == 0 && t >= 20f ? Role.Slot : Role.Trail);
            float step = after.Frame.TrailRef[1].Pos.Z - before.Frame.TrailRef[1].Pos.Z;
            Assert.InRange(step, 0f, 60f * Dt + 0.01f);
        }

        [Fact]
        public void WideAftSlotHangsOffTheLeadersRealPastPath()
        {
            // A slot 700 m in trail (3.5 s at 200 m/s). The leader turns right for 10 s, then reverses left: 2 s
            // into the reversal the slot must sit where the leader really was 3.5 s ago (still in the right
            // turn), not on the new turn's arc extrapolated backwards.
            var def = new FormationDefinition
            {
                Id = "trail", Slots = new[] { new SlotDef(0f, 2f, 0f) }, Element = new[] { 0 },
                SpacingMin = 160f, SpacingDefault = 350f, SpacingMax = 600f,
            };
            var wing = new FormationWing(def, 350f);
            WingMemberInput[] members = Members(Far);
            var path = new Vec3[12 * 60 + 1];
            Vec3 pos = new Vec3(0f, 2000f, 0f);
            float heading = 0f;
            for (int i = 0; i <= 12 * 60; i++)
            {
                float rate = i < 10 * 60 ? 0.1f : -0.1f;
                heading += rate * Dt;
                Vec3 vel = Vec3.FromHeading(heading * Scalar.Rad2Deg, 200f);
                pos += vel * Dt;
                path[i] = pos;
                wing.Update(new AnchorSample { Pos = pos, Vel = vel, Present = true, Airborne = true }, members, 1,
                    float.NaN, 60f, 8f, Dt);
            }
            Vec3 past = path[12 * 60 - (int)(3.5f * 60)];
            float off = (wing.Frame.Slots[0].Ref.Pos - past).Length;
            Assert.True(off < 10f, $"slot {off:0} m off the leader's real path");
        }

        [Fact]
        public void EstablishedNeedsTwoSecondsInsideThirtyPercentOfSpacing()
        {
            var wing = new FormationWing(FingerFour(), 80f);
            WingMemberInput[] members = Members(new Vec3(-80f, 2000f, -80f), Far, Far + new Vec3(0f, 0f, -500f));
            Assert.False(Run(wing, Leader(), members, 1.9f).Established[0]);
            WingFrame frame = Run(wing, Leader(), members, 0.2f);
            Assert.True(frame.Established[0]);
            Assert.False(frame.Established[1]);
        }

        [Fact]
        public void StaggerGateOpensForTheFirstSlotAndForOppositeSides()
        {
            var wing = new FormationWing(FingerFour(), 80f);
            WingFrame frame = Run(wing, Leader(), Members(Far, Far + new Vec3(300f, 0f, 0f), Far + new Vec3(600f, 0f, 0f)), Dt);
            Assert.True(frame.StaggerClear[0]);    // first wingman
            Assert.True(frame.StaggerClear[1]);    // #3 right of the leader, #2 left: different sides
            Assert.False(frame.StaggerClear[2]);   // #4 is on #3's side and #3 is not established
        }

        [Fact]
        public void CollisionBiasGoesToTheMemberNeverTheLeader()
        {
            var wing = new FormationWing(FingerFour(), 80f);
            WingFrame frame = Run(wing, Leader(), Members(new Vec3(5f, 2000f, 0f), Far, Far + new Vec3(300f, 0f, 0f)), 1f);
            Assert.True(frame.Bias[0].Length > 0.5f * Scalar.G, $"{frame.Bias[0]}");
            Assert.True(frame.Emergency[0]);
            Assert.Equal(Vec3.Zero, frame.Bias[1]);
        }

        [Fact]
        public void HistoryRestartsWhenTheLeaderReappears()
        {
            var def = new FormationDefinition
            {
                Id = "trail", Slots = new[] { new SlotDef(0f, 2f, 0f) }, Element = new[] { 0 },
                SpacingMin = 160f, SpacingDefault = 350f, SpacingMax = 600f,
            };
            var wing = new FormationWing(def, 350f);
            WingMemberInput[] members = Members(Far);
            for (int i = 0; i < 600; i++)
                wing.Update(new AnchorSample { Pos = new Vec3(0f, 2000f, i * 200f * Dt), Vel = new Vec3(0f, 0f, 200f), Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
            for (int i = 0; i < 60; i++) wing.Update(new AnchorSample { Present = false }, members, 1, float.NaN, 60f, 8f, Dt);
            var back = new Vec3(20000f, 2000f, 0f);
            wing.Update(new AnchorSample { Pos = back, Vel = new Vec3(0f, 0f, 200f), Present = true, Airborne = true },
                members, 1, float.NaN, 60f, 8f, Dt);
            float off = (wing.Frame.Slots[0].Ref.Pos - back).Length;
            Assert.True(off < 1000f, $"slot {off:0} m from the reappeared leader");
        }

        [Fact]
        public void LostLeaderKeepsTheLastEstimateAndFlagsIt()
        {
            var wing = new FormationWing(FingerFour(), 80f);
            WingMemberInput[] members = Members(Far, Far + new Vec3(300f, 0f, 0f), Far + new Vec3(600f, 0f, 0f));
            Vec3 last = Run(wing, Leader(), members, 1f).Leader.Pos;
            WingFrame frame = Run(wing, Leader(present: false), members, 1f);
            Assert.True(frame.LeaderLost);
            Assert.False(frame.Leader.Flying);
            Assert.Equal(last, frame.Leader.Pos);
            Assert.True(Scalar.IsFinite(frame.Slots[2].Ref.Pos.X));
        }

        [Fact]
        public void NotFlyingLeaderIsFlaggedAndSlotsStayFinite()
        {
            var wing = new FormationWing(FingerFour(), 80f);
            WingFrame frame = Run(wing, Leader(speed: 0f, airborne: false),
                Members(Far, Far + new Vec3(300f, 0f, 0f), Far + new Vec3(600f, 0f, 0f)), 1f);
            Assert.False(frame.Leader.Flying);
            for (int i = 0; i < 3; i++)
                Assert.True(Scalar.IsFinite(frame.Slots[i].Ref.Pos.X) && Scalar.IsFinite(frame.Slots[i].Ref.Vel.Z) &&
                            Scalar.IsFinite(frame.Slots[i].Ref.Acc.Y));
        }
    }
}
