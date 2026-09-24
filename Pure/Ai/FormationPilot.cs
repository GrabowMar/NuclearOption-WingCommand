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
        public readonly MissileDefence Defence = new MissileDefence();
        /// <summary>The nearest missile guiding on this member, set by the caller before each <see cref="Step"/> (spec M5
        /// §7.1); default: none.</summary>
        public MissileThreat Threat;
        public DefenceCommand LastDefence;
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
        private int conversions;

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

            // Survive first (spec M5 §7.2): the missile defence enters and leaves Defend itself, past any dwell.
            LastDefence = Defence.Step(Threat, s, LastRejoin.Ref, Precision, dt);
            if (LastDefence.Active != (Mind.Current == BehaviourId.Defend))
            {
                BehaviourId was = Mind.Current;
                BehaviourId to = LastDefence.Active ? BehaviourId.Defend : BehaviourId.Rejoin;
                // Leaving: the loops pick up from the throttle the defence held (bumpless).
                if (!LastDefence.Active) Pipeline.Track(s, LastOutput, p);
                Mind.Force(to);
                events?.Push(new WingEvent
                {
                    Time = time, Member = Slot, Kind = WingEventKind.BehaviourChanged, From = was, To = to,
                    Reason = LastDefence.Active ? TransitionReason.MissileInbound : TransitionReason.MissileClear,
                });
            }

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
            else if (Mind.Current == BehaviourId.Defend) reference = LastDefence.Ref;
            // Evading at full effort, as the game's evasion does (aimEffort 1).
            float aggression = Mind.Current == BehaviourId.Defend ? MissileDefence.DefendAggression : Aggression;

            LastIntent = new FlightIntent
            {
                Ref = reference,
                Limits = new SpeedLimits(p.MinimumSpeed(1f), p.MaxSpeed, AfterburnerAllowed, true),
                Precision = Precision,
                Aggression = aggression,
                Spacing = spacing,
                TerrainClearance = Clearance,
                HasHeading = Mind.Current != BehaviourId.HoldOverhead && Mind.Current != BehaviourId.Defend,
                HeadingDeg = Vec3.HeadingDeg(leader.Track),
            };
            GuidanceCommand guidance = LastGuidance = Pipeline.Guide(LastIntent, s, p);
            var ctx = new LimitContext
            {
                FloorY = frame.FloorY, NearFloorY = frame.NearFloorY[Slot], HasNearFloor = frame.HasNearFloor[Slot],
                Clearance = Clearance, Aggression = aggression, CollisionBias = frame.Bias[Slot],
            };
            LastOutput = Pipeline.Step(guidance, s, ctx, p, dt);
            if (Mind.Current == BehaviourId.Defend && (LastDefence.Idle || LastDefence.Full))
            {
                LastOutput.Throttle = LastDefence.Idle ? 0f : 1f;
                LastOutput.Airbrake = false;
            }

            if (Pipeline is TiltwingPipeline tilt && tilt.Conversions != conversions)
            {
                conversions = tilt.Conversions;
                Log(events, time, WingEventKind.Converted);
            }
            bool gcas = Pipeline.GcasActive;
            if (gcas && !gcasWas) Log(events, time, WingEventKind.GcasActivated);
            gcasWas = gcas;
            bool emergency = frame.Emergency[Slot];
            if (emergency && !emergencyWas) Log(events, time, WingEventKind.CollisionEmergency);
            emergencyWas = emergency;
            return LastOutput;
        }

        /// <summary>Flies an intent of its own (the recovery approach) through this member's pipeline, with its own terrain
        /// floor and the wing's collision bias.</summary>
        public ControlOutput FlyIntent(in FlightIntent intent, WingFrame frame, in AircraftState s, AirframeProfile p, float dt)
        {
            LastIntent = intent;
            GuidanceCommand guidance = LastGuidance = Pipeline.Guide(intent, s, p);
            bool near = frame.HasNearFloor[Slot];
            var ctx = new LimitContext
            {
                FloorY = near ? frame.NearFloorY[Slot] : float.NaN, NearFloorY = frame.NearFloorY[Slot], HasNearFloor = near,
                Clearance = Clearance, Aggression = intent.Aggression, CollisionBias = frame.Bias[Slot],
            };
            return LastOutput = Pipeline.Step(guidance, s, ctx, p, dt);
        }

        /// <summary>"Form up": every member rejoins now, logged as a commanded transition.</summary>
        public void FormUp(float time, WingEventRing events)
        {
            BehaviourId from = Mind.Current;
            // A member defending against a missile finishes first (spec M5 §7.2).
            if (from == BehaviourId.Defend || !Mind.Force(BehaviourId.Rejoin)) return;
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
