using System;

namespace WingCommand
{
// Filled by the engine's wing service (M1c) and the FlightSim; the mod assembly only reads it until then.
#pragma warning disable CS0649
    /// <summary>What one member contributes to the wing computation this tick.</summary>
    internal struct WingMemberInput
    {
        public AircraftState State;
        public MemberCapability Capability;
        public float Radius;
        /// <summary>Terrain under and 2 s ahead of this member, for its GCAS.</summary>
        public float NearFloorY;
        public bool HasNearFloor;
        /// <summary>The member's role as of its last step (the wing computes trail references for Trail members).</summary>
        public Role Role;
    }
#pragma warning restore CS0649

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
        public readonly float[] NearFloorY = new float[FormationCatalog.MaxSlots];
        public readonly bool[] HasNearFloor = new bool[FormationCatalog.MaxSlots];
        /// <summary>For Trail members: their point on the anchor's route (see <see cref="FormationWing"/>).</summary>
        public readonly RefState[] TrailRef = new RefState[FormationCatalog.MaxSlots];
    }

    /// <summary>The once-per-wing half of the formation layer:
    /// <list type="number">
    /// <item>the leader estimate and the slots;</item>
    /// <item>which members are established (inside 0.3 spacing for 2 s);</item>
    /// <item>the stagger gate: a slot opens when the previous slot is established or sits on the other side;</item>
    /// <item>the pairwise collision bias, with the leader as rank 0;</item>
    /// <item>the trail element: each Trail member advances along the anchor's route (<see cref="AnchorTrail"/>) at its
    /// own speed, at least one spacing behind the Trail member ahead of it (by slot) and
    /// <see cref="TrailGapSpacings"/> behind the anchor, staggered left/right by slot, <see cref="TrailBelow"/> m
    /// under the anchor and above the floor. Each keeps its own place on the route, so nobody's reference jumps
    /// when a member ahead leaves the trail.</item>
    /// </list>
    /// Members read the resulting <see cref="WingFrame"/>, so their order does not matter.</summary>
    internal sealed class FormationWing
    {
        public static float EstablishedFraction = 0.3f, EstablishedSeconds = 2f;
        public static float TrailBelow = 150f, TrailStagger = 0.5f, TrailGapSpacings = 2f;
        private const int N = FormationCatalog.MaxSlots;

        public readonly LeaderEstimator Estimator = new LeaderEstimator();
        public readonly SlotSolver Solver = new SlotSolver();
        public readonly LeaderHistory History = new LeaderHistory();
        public readonly AnchorTrail Route = new AnchorTrail();
        private readonly float[] routeS = new float[N];
        public readonly CollisionBias Collision = new CollisionBias(N + 1);
        public readonly WingFrame Frame = new WingFrame();
        private readonly Persistence[] established = new Persistence[N];
        private readonly MemberCapability[] caps = new MemberCapability[N];
        private readonly CollisionBody[] bodies = new CollisionBody[N + 1];
        private bool leaderWasPresent;

        public FormationWing(FormationDefinition definition, float spacing)
        {
            SetFormation(definition, spacing);
            for (int i = 0; i < N; i++) routeS[i] = float.NaN;
        }

        public void SetFormation(FormationDefinition definition, float spacing)
        {
            Frame.Definition = definition;
            Frame.Spacing = definition.ClampSpacing(spacing);
        }

        public WingFrame Update(in AnchorSample leader, WingMemberInput[] members, int count, float floorY,
            float clearance, float leaderRadius, float dt)
        {
            count = Math.Min(count, N);
            Frame.Leader = Estimator.Update(leader, dt);
            if (leader.Present && !leaderWasPresent)
            {
                History.Clear();
                Route.Clear();
            }
            leaderWasPresent = leader.Present;
            History.Push(Frame.Leader, dt);
            if (leader.Present) Route.Push(leader.Pos);   // where it was, not the projected estimate
            Frame.LeaderLost = !leader.Present;
            Frame.Count = count;
            Frame.FloorY = floorY;
            Frame.Clearance = clearance;
            for (int i = 0; i < count; i++)
            {
                caps[i] = members[i].Capability;
                Frame.NearFloorY[i] = members[i].NearFloorY;
                Frame.HasNearFloor[i] = members[i].HasNearFloor;
            }
            Solver.Solve(Frame.Definition, Frame.Spacing, Frame.Leader, caps, count, floorY, clearance, dt, Frame.Slots, History);
            UpdateTrail(members, count, dt);

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

        private void UpdateTrail(WingMemberInput[] members, int count, float dt)
        {
            int ahead = -1;
            for (int i = 0; i < N; i++)
            {
                if (i >= count || members[i].Role != Role.Trail)
                {
                    routeS[i] = float.NaN;
                    continue;
                }
                if (float.IsNaN(routeS[i])) routeS[i] = Route.Nearest(members[i].State.Pos);
                float cap = ahead >= 0 ? routeS[ahead] - Frame.Spacing : Route.Head - TrailGapSpacings * Frame.Spacing;
                float previous = routeS[i];
                routeS[i] = Math.Min(previous + members[i].Capability.MaxSpeed * dt, Math.Max(previous, cap));
                float advance = dt > 0f ? (routeS[i] - previous) / dt : 0f;

                Vec3 pos = Route.PointAt(routeS[i], out Vec3 along);
                pos += Vec3.Cross(Vec3.Up, along) * ((i % 2 == 0 ? 1f : -1f) * TrailStagger * Frame.Spacing);
                float y = Frame.Leader.Pos.Y - TrailBelow;
                if (!float.IsNaN(Frame.FloorY)) y = Math.Max(y, Frame.FloorY + Frame.Clearance);
                Frame.TrailRef[i] = new RefState(new Vec3(pos.X, y, pos.Z), along * advance, Vec3.Zero);
                ahead = i;
            }
        }

        private static int Side(float lateral) => lateral > 1f ? 1 : lateral < -1f ? -1 : 0;
    }
}
