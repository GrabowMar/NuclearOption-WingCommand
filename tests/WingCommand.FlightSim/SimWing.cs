using System;
using System.Collections.Generic;
using System.IO;

namespace WingCommand.FlightSim
{
    /// <summary>A virtual leader plus plant-flown wingmen, running the production formation stack each tick:
    /// one FormationWing update, then every FormationPilot, then the plants. Terrain is optional, as
    /// (x, z) → ground height. The wing floor is the highest terrain under the leader and every member, and
    /// 2, 5 and 10 s ahead of each, as the engine's probes will see it.</summary>
    internal sealed class SimWing
    {
        public const float Dt = 1f / 60f;
        private static readonly float[] ProbeSeconds = { 0f, 2f, 5f, 10f };

        public readonly VirtualLeader Leader;
        public readonly FixedWingPlant[] Plants;
        public readonly FormationPilot[] Pilots;
        public readonly FormationWing Wing;
        public readonly WingEventRing Events = new WingEventRing();
        public readonly AirframeProfile Profile = SimProfiles.GenericFighter();
        public Func<float, float, float> Terrain;
        public float Clearance = 60f;
        private readonly TerrainFloor floor = new TerrainFloor();
        private readonly WingMemberInput[] inputs;
        private readonly AircraftState[] states;

        public SimWing(VirtualLeader leader, FormationDefinition definition, float spacing, Vec3[] starts,
            float startSpeed, float startHeadingDeg)
        {
            Leader = leader;
            Wing = new FormationWing(definition, spacing);
            int n = starts.Length;
            Plants = new FixedWingPlant[n];
            Pilots = new FormationPilot[n];
            inputs = new WingMemberInput[n];
            states = new AircraftState[n];
            for (int i = 0; i < n; i++)
            {
                Plants[i] = new FixedWingPlant(PlantParams.GenericFighter, starts[i], startSpeed, startHeadingDeg);
                Pilots[i] = new FormationPilot(i);
                Pilots[i].Track(SimSensor.Read(Plants[i], Dt), new ControlOutput { Throttle = Plants[i].ThrottleActual }, Profile);
            }
        }

        public float Time { get; private set; }

        /// <summary>No two aircraft may come closer: bounding spheres plus 10 m.</summary>
        public float SafeRadius => 2f * Profile.MaxRadius + 10f;

        /// <summary>A wing air-started in its slots at the leader's velocity (straight and level leader).</summary>
        public static SimWing InSlots(VirtualLeader leader, FormationDefinition definition, float spacing, int members)
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
            return new SimWing(leader, definition, spacing, starts, leader.Speed, leader.HeadingDeg);
        }

        public void Step()
        {
            Time += Dt;
            float floor = Floor();
            for (int i = 0; i < Plants.Length; i++)
            {
                states[i] = SimSensor.Read(Plants[i], Dt);
                inputs[i] = new WingMemberInput
                {
                    State = states[i],
                    Capability = new MemberCapability
                    {
                        MaxSpeed = Pilots[i].AfterburnerAllowed ? Profile.MaxSpeed : Profile.MilSpeed,
                        MinSpeed = Profile.MinimumSpeed(1f),
                    },
                    Radius = Profile.MaxRadius,
                    NearFloorY = Terrain == null ? 0f : NearFloor(Plants[i].Position, Plants[i].Velocity),
                    HasNearFloor = Terrain != null,
                    Role = Pilots[i].Roles.Current,
                };
            }
            WingFrame frame = Wing.Update(Leader.Sample(), inputs, Plants.Length, floor, Clearance, Profile.MaxRadius, Dt);
            for (int i = 0; i < Plants.Length; i++)
            {
                ControlOutput o = Pilots[i].Step(frame, states[i], Profile, Time, Dt, Events);
                Plants[i].Step(new PlantInput(o.Pitch, o.Roll, o.Throttle), Dt);
            }
        }

        public AircraftState State(int i) => states[i];

        /// <summary>The telemetry row the in-game recorder would write for member <paramref name="i"/>.</summary>
        public TelemetryRow Row(int i) => TelemetryRows.From(Time, i, states[i], Pilots[i], Wing.Frame.Slots[i].Ref.Pos);

        public float SlotError(int i) => (Wing.Frame.Slots[i].Ref.Pos - Plants[i].Position).Length;

        /// <summary>Smallest distance between any two aircraft, the leader included.</summary>
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

        /// <summary>Height of member <paramref name="i"/> above the terrain directly below it.</summary>
        public float ClearanceOf(int i) => Plants[i].Position.Y - Terrain(Plants[i].Position.X, Plants[i].Position.Z);

        private float Floor()
        {
            if (Terrain == null) return float.NaN;
            float raw = Probe(Leader.Position, Leader.Velocity);
            for (int i = 0; i < Plants.Length; i++) raw = Math.Max(raw, Probe(Plants[i].Position, Plants[i].Velocity));
            return floor.Update(raw, Dt);
        }

        private float NearFloor(Vec3 p, Vec3 v) =>
            Math.Max(Terrain(p.X, p.Z), Terrain(p.X + v.X * 2f, p.Z + v.Z * 2f));

        private float Probe(Vec3 p, Vec3 v)
        {
            float highest = float.MinValue;
            foreach (float t in ProbeSeconds) highest = Math.Max(highest, Terrain(p.X + v.X * t, p.Z + v.Z * t));
            return highest;
        }
    }

    internal static class SimFormations
    {
        /// <summary>A built-in formation from <c>Assets/Data/formations.json</c> (copied to the output directory).</summary>
        public static FormationDefinition Get(string id)
        {
            var errors = new List<string>();
            List<FormationDefinition> all = FormationCatalog.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "formations.json")), errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            return FormationCatalog.Find(all, id) ?? throw new ArgumentException($"no formation {id}");
        }
    }
}
