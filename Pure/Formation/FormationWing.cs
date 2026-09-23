using System;

namespace WingCommand
{
    /// <summary>What one member contributes to the wing computation this tick.</summary>
    internal struct WingMemberInput
    {
        public AircraftState State;
        public MemberCapability Capability;
        public float Radius;
    }

    /// <summary>Everything a member reads this tick, computed once per wing. Arrays are indexed by slot.</summary>
    internal sealed class WingFrame
    {
        public LeaderEstimate Leader;
        public bool LeaderLost;
        public FormationDefinition Definition;
        public float Spacing, FloorY = float.NaN, Clearance;
        public int Count;
        public readonly SlotTarget[] Slots = new SlotTarget[FormationCatalog.MaxSlots];
        public readonly Vec3[] Bias = new Vec3[FormationCatalog.MaxSlots];
        public readonly bool[] Emergency = new bool[FormationCatalog.MaxSlots];
        public readonly bool[] Established = new bool[FormationCatalog.MaxSlots];
        public readonly bool[] StaggerClear = new bool[FormationCatalog.MaxSlots];
    }

    /// <summary>The once-per-wing half of the formation layer:
    /// <list type="number">
    /// <item>the leader estimate and the slots;</item>
    /// <item>which members are established (inside 0.3 spacing for 2 s);</item>
    /// <item>the stagger gate: a slot opens when the previous slot is established or sits on the other side;</item>
    /// <item>the pairwise collision bias, with the leader as rank 0.</item>
    /// </list>
    /// Members read the resulting <see cref="WingFrame"/>, so their order does not matter.</summary>
    internal sealed class FormationWing
    {
        public const float EstablishedFraction = 0.3f, EstablishedSeconds = 2f;
        private const int N = FormationCatalog.MaxSlots;

        public readonly LeaderEstimator Estimator = new LeaderEstimator();
        public readonly SlotSolver Solver = new SlotSolver();
        public readonly CollisionBias Collision = new CollisionBias(N + 1);
        public readonly WingFrame Frame = new WingFrame();
        private readonly Persistence[] established = new Persistence[N];
        private readonly MemberCapability[] caps = new MemberCapability[N];
        private readonly CollisionBody[] bodies = new CollisionBody[N + 1];

        public FormationWing(FormationDefinition definition, float spacing) => SetFormation(definition, spacing);

        public void SetFormation(FormationDefinition definition, float spacing)
        {
            Frame.Definition = definition;
            Frame.Spacing = definition.ClampSpacing(spacing);
        }

        public WingFrame Update(in LeaderSample leader, WingMemberInput[] members, int count, float floorY,
            float clearance, float leaderRadius, float dt)
        {
            count = Math.Min(count, N);
            Frame.Leader = Estimator.Update(leader, dt);
            Frame.LeaderLost = !leader.Present;
            Frame.Count = count;
            Frame.FloorY = floorY;
            Frame.Clearance = clearance;
            for (int i = 0; i < count; i++) caps[i] = members[i].Capability;
            Solver.Solve(Frame.Definition, Frame.Spacing, Frame.Leader, caps, count, floorY, clearance, dt, Frame.Slots);

            for (int i = 0; i < count; i++)
            {
                float error = (Frame.Slots[i].Ref.Pos - members[i].State.Pos).Length;
                Frame.Established[i] = established[i].Update(error < EstablishedFraction * Frame.Spacing, EstablishedSeconds, dt);
            }
            for (int i = 0; i < count; i++)
                Frame.StaggerClear[i] = i == 0 || Frame.Established[i - 1] ||
                                        Side(Frame.Slots[i].Lateral) != Side(Frame.Slots[i - 1].Lateral);

            bodies[0] = new CollisionBody
            {
                Pos = leader.Present ? leader.Pos : Frame.Leader.Pos,
                Vel = leader.Present ? leader.Vel : Frame.Leader.Vel,
                Radius = leaderRadius,
                Rank = 0,
            };
            for (int i = 0; i < count; i++)
                bodies[i + 1] = new CollisionBody
                {
                    Pos = members[i].State.Pos, Vel = members[i].State.Vel, Radius = members[i].Radius, Rank = i + 1,
                };
            Collision.Update(bodies, count + 1, Frame.Spacing, dt);
            for (int i = 0; i < count; i++)
            {
                Frame.Bias[i] = Collision.Bias[i + 1];
                Frame.Emergency[i] = Collision.Emergency[i + 1];
            }
            return Frame;
        }

        private static int Side(float lateral) => lateral > 1f ? 1 : lateral < -1f ? -1 : 0;
    }
}
