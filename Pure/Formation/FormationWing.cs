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
        /// <summary>Stable for the member's life in the wing (slots renumber when a member ahead leaves); trail state is
        /// kept by it.</summary>
        public int Id;
        /// <summary>Collision rank (a higher rank yields); 0 means index + 1, the one-element rule. Elements pass seat + 1
        /// so members of different elements yield the same way (spec WMC program §3.3).</summary>
        public int Rank;
        /// <summary>Still on the ground (taxiing, lining up, rolling): never holds the stagger gate behind it and is not
        /// part of the collision check.</summary>
        public bool Grounded;
    }
#pragma warning restore CS0649

    /// <summary>Everything a member reads this tick, computed once per wing. Arrays are indexed by slot.</summary>
    internal sealed class WingFrame
    {
        public LeaderEstimate Leader;
        public bool LeaderLost;
        public FormationDefinition Definition;
        public float Spacing, FloorY = float.NaN, Clearance;
        /// <summary>The whole shape's current Go High / Go Low offset (the rejoin lanes follow it).</summary>
        public float Stack;
        public int Count;
        public readonly SlotTarget[] Slots = new SlotTarget[FormationCatalog.MaxSlots];
        public readonly Vec3[] Bias = new Vec3[FormationCatalog.MaxSlots];
        public readonly bool[] Emergency = new bool[FormationCatalog.MaxSlots];
        public readonly bool[] Established = new bool[FormationCatalog.MaxSlots];
        public readonly bool[] StaggerClear = new bool[FormationCatalog.MaxSlots];
        public readonly float[] NearFloorY = new float[FormationCatalog.MaxSlots];
        public readonly bool[] HasNearFloor = new bool[FormationCatalog.MaxSlots];
        /// <summary>For Trail members: their point on the anchor's route (see <see cref="FormationWing"/>), valid when
        /// <see cref="TrailValid"/> (the wing saw the member trailing this tick).</summary>
        public readonly RefState[] TrailRef = new RefState[FormationCatalog.MaxSlots];
        public readonly bool[] TrailValid = new bool[FormationCatalog.MaxSlots];
    }

    /// <summary>The once-per-wing half of the formation layer:
    /// <list type="number">
    /// <item>the leader estimate and the slots;</item>
    /// <item>which members are established (inside 0.3 spacing for 2 s);</item>
    /// <item>the stagger gate: a slot opens when the previous slot is established, sits on the other side, or is not
    /// flying its slot (trail, high cover);</item>
    /// <item>the pairwise collision bias, with the leader as rank 0;</item>
    /// <item>the trail element: each Trail member advances along the anchor's route (<see cref="AnchorTrail"/>) at its
    /// own speed, at least one spacing behind the Trail member ahead of it (by slot) and
    /// <see cref="TrailGapSpacings"/> behind the anchor, staggered left/right by member id, <see cref="TrailBelow"/> m
    /// under the anchor and above the floor. Each keeps its own place on the route, keyed by its id, so nobody's
    /// reference jumps when a member ahead leaves the trail or the wing.</item>
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
        private readonly int[] routeIds = new int[N];
        private readonly bool[] routeSeen = new bool[N];
        public readonly CollisionBias Collision = new CollisionBias(N + 1);
        public readonly WingFrame Frame = new WingFrame();
        private readonly Persistence[] established = new Persistence[N];
        private readonly MemberCapability[] caps = new MemberCapability[N];
        private readonly CollisionBody[] bodies = new CollisionBody[N + 1];
        private bool leaderWasPresent;

        public FormationWing(FormationDefinition definition, float spacing)
        {
            SetFormation(definition, spacing);
            ForgetRoutes();
        }

        /// <summary>The wing forms on another aircraft now (an anchor set, lost or replaced by the player). The leader stays
        /// present across the change, so the estimate, the path history and the route restart here.</summary>
        public void ResetLeader()
        {
            Estimator.Reset();
            History.Clear();
            Route.Clear();
            ForgetRoutes();
        }

        public void SetFormation(FormationDefinition definition, float spacing)
        {
            Frame.Definition = definition;
            Frame.Spacing = definition.ClampSpacing(spacing);
        }

        public WingFrame Update(in AnchorSample leader, WingMemberInput[] members, int count, float floorY,
            float clearance, float leaderRadius, float dt) =>
            Update(leader, false, default, members, count, null, 0, floorY, clearance, leaderRadius, dt);

        /// <summary>As <see cref="Update(in AnchorSample, WingMemberInput[], int, float, float, float, float)"/>, with
        /// <paramref name="body"/> as the wing's collision body 0 instead of the anchor (the player's aircraft while the
        /// wing forms on a task's virtual lead, review M4a C2; not present: no body 0).</summary>
        public WingFrame Update(in AnchorSample leader, in AnchorSample body, WingMemberInput[] members, int count, float floorY,
            float clearance, float leaderRadius, float dt) =>
            Update(leader, true, body, members, count, null, 0, floorY, clearance, leaderRadius, dt);

        /// <summary>The full update: <paramref name="separateBody"/> makes <paramref name="body"/> the collision body 0 instead
        /// of the anchor; <paramref name="others"/> are aircraft outside this wing's slots (other elements' members) that
        /// its members also keep clear of, each with its own rank (spec WMC program §3.3).</summary>
        public WingFrame Update(in AnchorSample leader, bool separateBody, in AnchorSample body, WingMemberInput[] members,
            int count, CollisionBody[] others, int otherCount, float floorY, float clearance, float leaderRadius, float dt)
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
            Frame.Stack = Solver.StackNow;
            UpdateTrail(members, count, dt);

            for (int i = 0; i < count; i++)
            {
                float error = (Frame.Slots[i].Ref.Pos - members[i].State.Pos).Length;
                Frame.Established[i] = established[i].Update(error < EstablishedFraction * Frame.Spacing, EstablishedSeconds, dt);
            }
            for (int i = 0; i < count; i++)
                Frame.StaggerClear[i] = i == 0 || Frame.Established[i - 1] || members[i - 1].Role != Role.Slot || members[i - 1].Grounded ||
                                        Side(Frame.Slots[i].Lateral) != Side(Frame.Slots[i - 1].Lateral);

            bodies[0] = !separateBody
                ? new CollisionBody
                {
                    Pos = leader.Present ? leader.Pos : Frame.Leader.Pos,
                    Vel = leader.Present ? leader.Vel : Frame.Leader.Vel,
                    Radius = leaderRadius,
                    Rank = 0,
                }
                : new CollisionBody { Pos = body.Pos, Vel = body.Vel, Radius = leaderRadius, Rank = 0, Ignored = !body.Present };
            for (int i = 0; i < count; i++)
                bodies[i + 1] = new CollisionBody
                {
                    Pos = members[i].State.Pos, Vel = members[i].State.Vel, Radius = members[i].Radius,
                    Rank = members[i].Rank > 0 ? members[i].Rank : i + 1, Ignored = members[i].Grounded,
                };
            int extra = others == null ? 0 : Math.Min(otherCount, bodies.Length - count - 1);
            for (int k = 0; k < extra; k++) bodies[count + 1 + k] = others[k];
            Collision.Update(bodies, count + 1 + extra, Frame.Spacing, dt);
            for (int i = 0; i < count; i++)
            {
                Frame.Bias[i] = Collision.Bias[i + 1];
                Frame.Emergency[i] = Collision.Emergency[i + 1];
            }
            return Frame;
        }

        private void UpdateTrail(WingMemberInput[] members, int count, float dt)
        {
            for (int k = 0; k < N; k++) routeSeen[k] = false;
            bool haveAhead = false;
            float aheadS = 0f;
            for (int i = 0; i < N; i++)
            {
                Frame.TrailValid[i] = false;
                if (i >= count || members[i].Role != Role.Trail) continue;
                int k = RouteOf(members[i].Id);
                if (k < 0) continue;
                routeSeen[k] = true;
                if (float.IsNaN(routeS[k])) routeS[k] = Route.Nearest(members[i].State.Pos);
                float cap = haveAhead ? aheadS - Frame.Spacing : Route.Head - TrailGapSpacings * Frame.Spacing;
                float previous = routeS[k];
                routeS[k] = Math.Min(previous + members[i].Capability.MaxSpeed * dt, Math.Max(previous, cap));
                float advance = dt > 0f ? (routeS[k] - previous) / dt : 0f;

                Vec3 pos = Route.PointAt(routeS[k], out Vec3 along);
                float side = Math.Abs(members[i].Id) % 2 == 0 ? 1f : -1f;
                pos += Vec3.Cross(Vec3.Up, along) * (side * TrailStagger * Frame.Spacing);
                float y = Frame.Leader.Pos.Y - TrailBelow;
                if (!float.IsNaN(Frame.FloorY)) y = Math.Max(y, Frame.FloorY + Frame.Clearance);
                Frame.TrailRef[i] = new RefState(new Vec3(pos.X, y, pos.Z), along * advance, Vec3.Zero);
                Frame.TrailValid[i] = true;
                aheadS = routeS[k];
                haveAhead = true;
            }
            for (int k = 0; k < N; k++)
                if (!routeSeen[k])
                {
                    routeIds[k] = -1;
                    routeS[k] = float.NaN;
                }
        }

        /// <summary>The route entry of member <paramref name="id"/>, taking a free one for a newcomer; −1 when full.</summary>
        private int RouteOf(int id)
        {
            int free = -1;
            for (int k = 0; k < N; k++)
            {
                if (routeIds[k] == id) return k;
                if (free < 0 && routeIds[k] == -1) free = k;
            }
            if (free < 0) return -1;
            routeIds[free] = id;
            routeS[free] = float.NaN;
            return free;
        }

        private void ForgetRoutes()
        {
            for (int k = 0; k < N; k++)
            {
                routeIds[k] = -1;
                routeS[k] = float.NaN;
            }
        }

        private static int Side(float lateral) => lateral > 1f ? 1 : lateral < -1f ? -1 : 0;
    }
}
