using System;

namespace WingCommand.FlightSim
{
    /// <summary>A virtual leader plus helicopter wingmen on <see cref="RotaryPlant"/>s, running the production
    /// formation stack each tick as <see cref="SimWing"/> does for jets: one FormationWing update, then every
    /// FormationPilot (rotary pipelines from FlightStack), then the plants. Flat terrain, no floor.
    /// ponytail: a mixed wing (M2b) needs one plant interface; this class merges into SimWing then.</summary>
    internal sealed class RotarySimWing
    {
        public const float Dt = 1f / 60f;

        public readonly VirtualLeader Leader;
        public readonly RotaryPlant[] Plants;
        public readonly FormationPilot[] Pilots;
        public readonly FormationWing Wing;
        public readonly WingEventRing Events = new WingEventRing();
        public readonly AirframeProfile Profile = SimProfiles.Utility();
        private readonly WingMemberInput[] inputs;
        private readonly AircraftState[] states;

        public RotarySimWing(VirtualLeader leader, FormationDefinition definition, float spacing, Vec3[] starts, Vec3 startVelocity)
        {
            Leader = leader;
            Wing = new FormationWing(definition, spacing);
            int n = starts.Length;
            Plants = new RotaryPlant[n];
            Pilots = new FormationPilot[n];
            inputs = new WingMemberInput[n];
            states = new AircraftState[n];
            for (int i = 0; i < n; i++)
            {
                Plants[i] = new RotaryPlant(RotaryParams.Utility, starts[i], startVelocity, leader.HeadingDeg);
                Pilots[i] = new FormationPilot(i, AirframeClass.Rotary);
                Pilots[i].Track(Plants[i].Read(Dt), new ControlOutput { Throttle = Plants[i].Collective }, Profile);
            }
        }

        public float Time { get; private set; }

        public float SafeRadius => 2f * Profile.MaxRadius + 10f;

        /// <summary>A wing started in its slots at the leader's velocity (straight and level leader).</summary>
        public static RotarySimWing InSlots(VirtualLeader leader, FormationDefinition definition, float spacing, int members)
        {
            spacing = definition.ClampSpacing(spacing);
            var starts = new Vec3[members];
            for (int i = 0; i < members; i++)
            {
                SlotDef slot = SlotSolver.SlotFor(definition, i);
                starts[i] = leader.Position + TurnFrame.Offset(leader.Velocity, Vec3.FromHeading(leader.HeadingDeg), 0f,
                    slot.Right * spacing, slot.Aft * spacing, slot.Up * FormationCatalog.StackMetres, 0f);
            }
            return new RotarySimWing(leader, definition, spacing, starts, leader.Velocity);
        }

        public void Step()
        {
            Time += Dt;
            for (int i = 0; i < Plants.Length; i++)
            {
                states[i] = Plants[i].Read(Dt);
                inputs[i] = new WingMemberInput
                {
                    State = states[i],
                    Capability = new MemberCapability { MaxSpeed = Profile.CruiseSpeed, MinSpeed = Profile.MinimumSpeed(1f) },
                    Radius = Profile.MaxRadius,
                };
            }
            WingFrame frame = Wing.Update(Leader.Sample(), inputs, Plants.Length, float.NaN, 60f, Profile.MaxRadius, Dt);
            for (int i = 0; i < Plants.Length; i++)
                Plants[i].Step(Pilots[i].Step(frame, states[i], Profile, Time, Dt, Events), Dt);
        }

        public float SlotError(int i) => (Wing.Frame.Slots[i].Ref.Pos - Plants[i].Position).Length;

        /// <summary>Slot error along the leader's track (+ = the member is behind its slot).</summary>
        public float AlongError(int i) => Vec3.Dot(Wing.Frame.Slots[i].Ref.Pos - Plants[i].Position, Wing.Frame.Leader.Track);

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
    }
}
