using System;
using System.Collections.Generic;

namespace WingCommand.FlightSim
{
    /// <summary>Spec WMC program §3.3: a wing split into elements, running the production formation stack per element. A
    /// straight-and-level virtual leader; element A forms on it; every other element forms on its own planner's lead,
    /// with the leader as its collision body 0; every element's members keep clear of the other elements' members
    /// (ranks by seat). Member ids are plant indices; seats are plant indices (no one leaves).</summary>
    internal sealed class ElementSimWing
    {
        public const float Dt = SimWing.Dt;

        public readonly VirtualLeader Leader;
        public readonly FixedWingPlant[] Plants;
        public readonly FormationPilot[] Pilots;
        public readonly ElementRoster Roster = new ElementRoster();
        public readonly WingEventRing Events = new WingEventRing();
        public readonly AirframeProfile Profile = SimProfiles.GenericFighter();
        public float Clearance = 60f;
        private readonly FormationDefinition definition;
        private readonly float spacing;
        private readonly FormationWing[] wings = new FormationWing[ElementRoster.MaxElements];
        private readonly WingPlanner[] planners = new WingPlanner[ElementRoster.MaxElements];
        private readonly WingFrame[] frames = new WingFrame[ElementRoster.MaxElements];
        private readonly AircraftState[] states;
        private readonly WingMemberInput[] inputs = new WingMemberInput[FormationCatalog.MaxSlots];
        private readonly CollisionBody[] others = new CollisionBody[FormationCatalog.MaxSlots];
        private readonly List<uint> ids = new List<uint>();
        private readonly int[] elementOf;

        private ElementSimWing(VirtualLeader leader, FormationDefinition definition, float spacing, Vec3[] starts)
        {
            Leader = leader;
            this.definition = definition;
            this.spacing = spacing;
            int n = starts.Length;
            Plants = new FixedWingPlant[n];
            Pilots = new FormationPilot[n];
            states = new AircraftState[n];
            elementOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                Plants[i] = new FixedWingPlant(PlantParams.GenericFighter, starts[i], leader.Speed, leader.HeadingDeg);
                Pilots[i] = new FormationPilot(i, AirframeClass.FixedWing);
                Pilots[i].Track(SimSensor.Read(Plants[i], Dt), new ControlOutput { Throttle = Plants[i].ThrottleActual }, Profile);
                Roster.Add((uint)i);
            }
        }

        public float Time { get; private set; }

        public float SafeRadius => 2f * Profile.MaxRadius + 10f;

        public static ElementSimWing InSlots(VirtualLeader leader, FormationDefinition definition, float spacing, int members)
        {
            spacing = definition.ClampSpacing(spacing);
            var starts = new Vec3[members];
            for (int i = 0; i < members; i++)
            {
                SlotDef slot = SlotSolver.SlotFor(definition, i);
                float right = slot.Right * spacing;
                starts[i] = leader.Position + TurnFrame.Offset(leader.Velocity, Vec3.Forward, 0f, right,
                    slot.Aft * spacing, slot.Up * FormationCatalog.StackMetres,
                    TurnFrame.RollFollowWeight(TurnFrame.Reach(right, slot.Aft * spacing, slot.Up * FormationCatalog.StackMetres), slot.RollFollow));
            }
            return new ElementSimWing(leader, definition, spacing, starts);
        }

        /// <summary>Detaches <paramref name="members"/> with <paramref name="task"/>; returns the element (or -1 refused).</summary>
        public int Detach(WingTask task, params uint[] members)
        {
            ScopeTarget t = Roster.Resolve(WingScope.OfMembers(members));
            if (t.Element <= 0) return t.Element;
            WingPlanner planner = planners[t.Element] ?? (planners[t.Element] = new WingPlanner(t.Element));
            if (!planner.Apply(task, Snapshot(t.Element), Time, Events).Accepted)
            {
                Roster.Merge(t.Element);
                return -1;
            }
            WingOf(t.Element).ResetLeader();
            return t.Element;
        }

        public void Merge(int e)
        {
            Roster.Merge(e);
            planners[e]?.Reset();
        }

        public void Step()
        {
            Time += Dt;
            Leader.Step(0f, Dt);
            for (int i = 0; i < Plants.Length; i++) states[i] = SimSensor.Read(Plants[i], Dt);
            for (int e = 1; e < ElementRoster.MaxElements; e++)
            {
                if (!Roster.InUse(e)) continue;
                if (planners[e] != null && planners[e].Active) planners[e].Step(Snapshot(e), Time, Dt, Events);
                if (planners[e] == null || !planners[e].Active) Merge(e);
            }
            for (int e = 0; e < ElementRoster.MaxElements; e++)
            {
                if (!Roster.InUse(e)) continue;
                Roster.Members(e, ids);
                int k = 0;
                foreach (uint id in ids)
                {
                    int i = (int)id;
                    elementOf[i] = e;
                    Pilots[i].Slot = k;
                    Pilots[i].Seat = i;
                    inputs[k++] = Input(i);
                }
                int o = 0;
                for (int j = 0; j < Plants.Length; j++)
                    if (Roster.ElementOf((uint)j) != e)
                        others[o++] = new CollisionBody { Pos = states[j].Pos, Vel = states[j].Vel, Radius = Profile.MaxRadius, Rank = j + 1 };
                frames[e] = e == 0
                    ? WingOf(0).Update(Leader.Sample(), false, default, inputs, k, others, o, float.NaN, Clearance, Profile.MaxRadius, Dt)
                    : WingOf(e).Update(planners[e].Sample(), true, Leader.Sample(), inputs, k, others, o, float.NaN, Clearance,
                        Profile.MaxRadius, Dt);
            }
            for (int i = 0; i < Plants.Length; i++)
            {
                ControlOutput c = Pilots[i].Step(frames[elementOf[i]], states[i], Profile, Time, Dt, Events);
                Plants[i].Step(new PlantInput(c.Pitch, c.Roll, c.Throttle), Dt);
            }
        }

        public WingPlanner PlannerOf(int e) => planners[e];

        public AircraftState State(int i) => states[i];

        public float SlotError(int i) => (frames[elementOf[i]].Slots[Pilots[i].Slot].Ref.Pos - Plants[i].Position).Length;

        public float MinSeparation()
        {
            float min = float.MaxValue;
            for (int i = 0; i < Plants.Length; i++)
            {
                min = Math.Min(min, (Plants[i].Position - Leader.Position).Length);
                for (int j = i + 1; j < Plants.Length; j++) min = Math.Min(min, (Plants[i].Position - Plants[j].Position).Length);
            }
            return min;
        }

        private WingMemberInput Input(int i) => new WingMemberInput
        {
            State = states[i],
            Capability = new MemberCapability
            {
                MaxSpeed = Pilots[i].AfterburnerAllowed ? Profile.MaxSpeed : Profile.MilSpeed,
                MinSpeed = Profile.MinimumSpeed(1f),
            },
            Radius = Profile.MaxRadius,
            Role = Pilots[i].Roles.Current,
            Id = i,
            Rank = i + 1,
        };

        private FormationWing WingOf(int e) => wings[e] ?? (wings[e] = new FormationWing(definition, spacing));

        /// <summary>What an element's planner sees: its members only; no anchor, so a new lead starts at them.</summary>
        private WingSnapshot Snapshot(int e)
        {
            Vec3 sum = Vec3.Zero, vel = Vec3.Zero;
            int n = 0;
            for (int i = 0; i < Plants.Length; i++)
            {
                if (Roster.ElementOf((uint)i) != e) continue;
                sum += Plants[i].Position;
                vel += Plants[i].Velocity;
                n++;
            }
            return new WingSnapshot
            {
                Members = n, Centroid = n > 0 ? sum * (1f / n) : Vec3.Zero, MeanVel = n > 0 ? vel * (1f / n) : Vec3.Zero,
                CruiseSpeed = Profile.CruiseSpeed, MinSpeed = Profile.MinimumSpeed(1f), FloorY = float.NaN,
                WingAirborne = true, MapHalfX = 200000f, MapHalfZ = 200000f,
            };
        }
    }
}
