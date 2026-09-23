using System;
using System.Collections.Generic;

namespace WingCommand.FlightSim
{
    /// <summary>A virtual leader plus wingmen of any class, each on its own plant with its own profile, running the
    /// production formation stack each tick: one FormationWing update, then every FormationPilot (pipelines from
    /// FlightStack), then the plants. Optional flat floor (<see cref="FloorY"/>). <see cref="SimWing"/> stays the jets-only harness of the
    /// M1 scenarios (its tests read fixed-wing plant internals).</summary>
    internal sealed class MixedSimWing
    {
        public const float Dt = 1f / 60f;

        public readonly VirtualLeader Leader;
        public readonly FormationWing Wing;
        public readonly WingEventRing Events = new WingEventRing();
        public readonly List<ISimPlant> Plants = new List<ISimPlant>();
        public readonly List<FormationPilot> Pilots = new List<FormationPilot>();
        public readonly List<AirframeProfile> Profiles = new List<AirframeProfile>();
        /// <summary>Flat terrain height, or NaN for none; members keep <see cref="Clearance"/> above it.</summary>
        public float FloorY = float.NaN, Clearance = 60f;
        private readonly WingMemberInput[] inputs = new WingMemberInput[FormationCatalog.MaxSlots];
        private readonly AircraftState[] states = new AircraftState[FormationCatalog.MaxSlots];

        public MixedSimWing(VirtualLeader leader, FormationDefinition definition, float spacing)
        {
            Leader = leader;
            Wing = new FormationWing(definition, spacing);
        }

        public float Time { get; private set; }

        public int Count => Plants.Count;

        /// <summary>Adds a member flying <paramref name="plant"/> with <paramref name="profile"/>; returns its slot.</summary>
        public int Add(ISimPlant plant, AirframeProfile profile, float throttle)
        {
            var pilot = new FormationPilot(Plants.Count, profile.Class);
            pilot.Track(plant.Read(Dt), new ControlOutput { Throttle = throttle }, profile);
            Plants.Add(plant);
            Pilots.Add(pilot);
            Profiles.Add(profile);
            return Plants.Count - 1;
        }

        /// <summary>Helicopters started in their slots at the leader's velocity (straight and level leader).</summary>
        public static MixedSimWing HelosInSlots(VirtualLeader leader, FormationDefinition definition, float spacing, int members)
        {
            var wing = new MixedSimWing(leader, definition, definition.ClampSpacing(spacing));
            for (int i = 0; i < members; i++)
            {
                SlotDef slot = SlotSolver.SlotFor(definition, i);
                Vec3 start = leader.Position + TurnFrame.Offset(leader.Velocity, Vec3.FromHeading(leader.HeadingDeg), 0f,
                    slot.Right * wing.Wing.Frame.Spacing, slot.Aft * wing.Wing.Frame.Spacing, slot.Up * FormationCatalog.StackMetres, 0f);
                var plant = new RotaryPlant(RotaryParams.Utility, start, leader.Velocity, leader.HeadingDeg);
                wing.Add(plant, SimProfiles.Utility(), plant.Collective);
            }
            return wing;
        }

        public void Step()
        {
            Time += Dt;
            int n = Count;
            for (int i = 0; i < n; i++)
            {
                AirframeProfile p = Profiles[i];
                states[i] = Plants[i].Read(Dt);
                bool rotary = p.Class == AirframeClass.Rotary;
                inputs[i] = new WingMemberInput
                {
                    State = states[i],
                    Capability = new MemberCapability
                    {
                        MaxSpeed = rotary ? p.CruiseSpeed : Pilots[i].AfterburnerAllowed && p.HasAfterburner ? p.MaxSpeed : p.MilSpeed,
                        MinSpeed = p.MinimumSpeed(1f),
                    },
                    Radius = p.MaxRadius,
                    Role = Pilots[i].Roles.Current,
                    Id = i,
                };
            }
            WingFrame frame = Wing.Update(Leader.Sample(), inputs, n, FloorY, Clearance, 9f, Dt);
            for (int i = 0; i < n; i++)
                Plants[i].Step(Pilots[i].Step(frame, states[i], Profiles[i], Time, Dt, Events), Dt);
        }

        public float SlotError(int i) => (Wing.Frame.Slots[i].Ref.Pos - Plants[i].Position).Length;

        /// <summary>Slot error along the leader's track (+ = the member is behind its slot).</summary>
        public float AlongError(int i) => Vec3.Dot(Wing.Frame.Slots[i].Ref.Pos - Plants[i].Position, Wing.Frame.Leader.Track);

        /// <summary>No two aircraft may come closer than their bounding spheres plus 10 m.</summary>
        public float SafeRadius
        {
            get
            {
                float r = 0f;
                foreach (AirframeProfile p in Profiles) r = Math.Max(r, p.MaxRadius);
                return 2f * r + 10f;
            }
        }

        public float MinSeparation()
        {
            float min = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                min = Math.Min(min, (Plants[i].Position - Leader.Position).Length);
                for (int j = i + 1; j < Count; j++) min = Math.Min(min, (Plants[i].Position - Plants[j].Position).Length);
            }
            return min;
        }
    }
}
