using System;

namespace WingCommand
{
    /// <summary>One wingman's brain and flight stack.
    /// <list type="bullet">
    /// <item>PilotMind picks the behaviour.</item>
    /// <item>The behaviour fills the FlightIntent: rejoin references, the slot, or a hold-orbit rabbit.</item>
    /// <item>The shared pipeline flies it with the wing's collision bias and floor.</item>
    /// </list>
    /// It logs transitions (with reasons), falling behind, GCAS activations and collision emergencies. The
    /// engine's PilotAgent (M1c) and the FlightSim both drive this class. It lives as long as the aircraft,
    /// and nothing resets on a behaviour change.</summary>
    internal sealed class FormationPilot
    {
        public readonly IFlightPipeline Pipeline;
        public readonly RejoinPlanner Rejoin = new RejoinPlanner();
        public readonly PilotMind Mind = new PilotMind();
        public readonly RolePolicy Roles = new RolePolicy();
        /// <summary>Slot index; the engine reassigns it when a member ahead of it is lost.</summary>
        public int Slot;
        public float Precision = 1f, Aggression = 0.5f, Clearance = 60f;
        public bool AfterburnerAllowed = true;
        public FlightIntent LastIntent;
        public GuidanceCommand LastGuidance;
        public RejoinOutput LastRejoin;
        public ControlOutput LastOutput;
        private HoldOrbit orbit;
        private bool gcasWas, emergencyWas;

        public FormationPilot(int slot, AirframeClass cls)
        {
            Slot = slot;
            Pipeline = FlightStack.NewPipeline(cls);
        }

        /// <summary>Seed every loop from the aircraft (spawn, handover from native flight).</summary>
        public void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p) => Pipeline.Track(s, applied, p);

        public ControlOutput Step(WingFrame frame, in AircraftState s, AirframeProfile p, float time, float dt,
            WingEventRing events)
        {
            SlotTarget slot = frame.Slots[Slot];
            LeaderEstimate leader = frame.Leader;
            float usable = AfterburnerAllowed && p.HasAfterburner ? p.MaxSpeed : p.MilSpeed;
            Rejoin.LaneStepM = Math.Max(RejoinPlanner.LaneStep,
                CollisionBias.RadiusFor(p.MaxRadius, frame.Spacing) + RejoinPlanner.LaneClearance);
            LastRejoin = Rejoin.Step(s, slot, leader, Slot + 1, frame.Spacing, usable, frame.StaggerClear[Slot], dt);
            if (LastRejoin.FallingBehindStarted) Log(events, time, WingEventKind.FallingBehind);
            if (LastRejoin.FallingBehindCleared) Log(events, time, WingEventKind.FallingBehindCleared);

            Roles.Tick(new RoleInput
            {
                AnchorSpeed = leader.Speed,
                MinSpeed = p.MinimumSpeed(1f),
                // Only helicopters trail; a jet that cannot keep up flies cutoff and calls "falling behind" (A1).
                TopSpeed = p.Class == AirframeClass.Rotary ? p.CruiseSpeed : 0f,
            }, dt, out _);
            var mind = new MindInput
            {
                Sigma = LastRejoin.Sigma,
                SlotError = (slot.Ref.Pos - s.Pos).Length,
                Spacing = frame.Spacing,
                Role = Roles.Current,
                LeaderFlying = leader.Flying,
                LeaderLost = frame.LeaderLost,
            };
            if (Mind.Tick(mind, dt, out BehaviourId from, out TransitionReason reason))
            {
                events?.Push(new WingEvent
                {
                    Time = time, Member = Slot, Kind = WingEventKind.BehaviourChanged,
                    From = from, To = Mind.Current, Reason = reason,
                });
                if (Mind.Current == BehaviourId.HoldOverhead) orbit.Begin(HoldCenter(leader), OrbitSpeed(p), s.Pos);
            }

            RefState reference = LastRejoin.Ref;
            float spacing = frame.Spacing;
            if (Mind.Current == BehaviourId.StationKeep) reference = slot.Ref;
            // The frame computes trail references from last tick's roles: the tick the mind enters Trail it may have none.
            else if (Mind.Current == BehaviourId.Trail) reference = frame.TrailValid[Slot] ? frame.TrailRef[Slot] : LastRejoin.Ref;
            else if (Mind.Current == BehaviourId.HoldOverhead)
            {
                bool anchored = !frame.LeaderLost && leader.Flying;
                reference = orbit.Step(HoldCenter(leader), anchored ? leader.Vel : Vec3.Zero, dt);
                spacing = 0f;
            }

            LastIntent = new FlightIntent
            {
                Ref = reference,
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, AfterburnerAllowed, true),
                Precision = Precision,
                Aggression = Aggression,
                Spacing = spacing,
                TerrainClearance = Clearance,
                HasHeading = Mind.Current != BehaviourId.HoldOverhead,
                HeadingDeg = Vec3.HeadingDeg(leader.Track),
            };
            GuidanceCommand guidance = LastGuidance = Pipeline.Guide(LastIntent, s, p);
            var ctx = new LimitContext
            {
                FloorY = frame.FloorY, NearFloorY = frame.NearFloorY[Slot], HasNearFloor = frame.HasNearFloor[Slot],
                Clearance = Clearance, Aggression = Aggression, CollisionBias = frame.Bias[Slot],
            };
            LastOutput = Pipeline.Step(guidance, s, ctx, p, dt);

            bool gcas = Pipeline.GcasActive;
            if (gcas && !gcasWas) Log(events, time, WingEventKind.GcasActivated);
            gcasWas = gcas;
            bool emergency = frame.Emergency[Slot];
            if (emergency && !emergencyWas) Log(events, time, WingEventKind.CollisionEmergency);
            emergencyWas = emergency;
            return LastOutput;
        }

        /// <summary>"Form up": every member rejoins now, logged as a commanded transition.</summary>
        public void FormUp(float time, WingEventRing events)
        {
            BehaviourId from = Mind.Current;
            if (!Mind.Force(BehaviourId.Rejoin)) return;
            events?.Push(new WingEvent
            {
                Time = time, Member = Slot, Kind = WingEventKind.BehaviourChanged,
                From = from, To = BehaviourId.Rejoin, Reason = TransitionReason.Commanded,
            });
        }

        private Vec3 HoldCenter(in LeaderEstimate leader) =>
            new Vec3(leader.Pos.X, leader.Pos.Y + HoldOrbit.BaseHeight + HoldOrbit.SlotHeight * (Slot + 1), leader.Pos.Z);

        /// <summary>Hold orbit speed: three quarters of cruise, never under 1.5 × the loaded minimum.</summary>
        internal static float OrbitSpeed(AirframeProfile p) => Math.Max(1.5f * p.MinimumSpeed(1f), 0.75f * p.CruiseSpeed);

        private void Log(WingEventRing events, float time, WingEventKind kind) =>
            events?.Push(new WingEvent { Time = time, Member = Slot, Kind = kind });
    }
}
