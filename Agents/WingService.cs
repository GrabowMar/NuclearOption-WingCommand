using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace WingCommand
{
    /// <summary>The local player's wing (M1: one wing per host, up to <see cref="WingConfig.MaxWingmen"/>
    /// members).
    /// <list type="bullet">
    /// <item>Each physics tick, whichever member steps first computes the <see cref="WingFrame"/> (sensors,
    /// leader, slots, collision pairs); every member then flies from it (spec §2.2).</item>
    /// <item>Runtime FixedTick tracks the leader, prunes lost members, compacts slots, and probes terrain at
    /// 5 Hz.</item>
    /// <item>Members that lose their fly-by-wire for 1 s, or fault three times in 10 s, go to native AI once.</item>
    /// <item>The wing forms on its anchor while one is set and alive (an aircraft from automation, or any unit it
    /// escorts), else on the local player. A lost anchor falls back to the player; a lost escortee also says so.</item>
    /// <item>The shape use follows the anchor: rotary shapes behind a helicopter, escort shapes while escorting.</item>
    /// <item><see cref="Metrics"/> accumulates formation quality for the automated in-game scenarios.</item>
    /// </list></summary>
    internal sealed partial class WingService : IWingService
    {
        public const int ProbeTicks = 12;
        public static float Clearance = 60f, NoFbwReleaseSeconds = 1f;

        public static WingService Instance { get; private set; }

        public string Name => "Wing";
        public readonly List<WingMember> Members = new List<WingMember>(FormationCatalog.MaxSlots);
        public WingEventRing Events { get; private set; } = new WingEventRing();
        public FormationSelection Selection { get; private set; }
        public FormationWing Wing { get; private set; }
        /// <summary>What the wing forms on: the anchor while one is set and alive, else the player's aircraft.</summary>
        public Unit LeaderUnit { get; private set; }
        /// <summary>The leader when it is an aircraft (null while escorting a vehicle or a ship).</summary>
        public Aircraft Leader => LeaderUnit as Aircraft;
        /// <summary>The local player's aircraft.</summary>
        public Aircraft Player { get; private set; }
        public Unit Anchor { get; private set; }
        public bool Escorting { get; private set; }
        public static string RotaryDefaultShape = "staggered-trail";
        public WingMetrics Metrics { get; } = new WingMetrics();
        public float MissionTime => missionTime;
        public double LastFrameAiMs { get; private set; }
        /// <summary>This mission's AI time per frame (plan M8 budget: 0.3 ms for a 4-ship), over frames with members.</summary>
        public double AiMsMean => aiFrames > 0 ? aiMsSum / aiFrames : 0.0;
        public double AiMsMax { get; private set; }
        private double aiMsSum;
        private long aiFrames;
        public event Action RosterChanged;

        private readonly WingMemberInput[] inputs = new WingMemberInput[FormationCatalog.MaxSlots];
        private readonly AircraftSensor leaderSensor = new AircraftSensor();
        private TerrainFloor floor = new TerrainFloor();
        private float frameTime = float.NaN, missionTime;
        private int probeTick, frameIndex, nextMemberId;
        private AirframeClass leaderClass;
        private long eventsLogged, aiTicks;

        public WingService() => Instance = this;

        public static int MaxMembers => Plugin.Settings.MaxWingmen.Value;

        public void Activate()
        {
            Members.Clear();
            FieldRegistry.Clear();
            WingPilotRoster.Reset();
            WingKillCredit.Reset();
            WingLedger.Reset();
            Planner.Reset();
            WingTakeover.Reset();
            WingRecruitment.Reset();
            ResetCombat();
            LoadDoctrine();
            StandingShots = 0;
            flyingPlayer = null;
            Events = new WingEventRing();
            floor = new TerrainFloor();
            LeaderUnit = null;
            Player = null;
            Anchor = null;
            Escorting = false;
            leaderClass = AirframeClass.FixedWing;
            frameTime = float.NaN;
            missionTime = 0f;
            eventsLogged = 0;
            Metrics.Reset(0f);
            aiMsSum = 0.0;
            aiFrames = 0;
            AiMsMax = 0.0;
            if (WingData.Formations.Count == 0)
            {
                Plugin.Logger.LogError("[Wing] no valid formations loaded; the wing is disabled this mission");
                Selection = null;
                Wing = null;
                return;
            }
            Selection = new FormationSelection(WingData.Formations, Plugin.Settings.DefaultFormation.Value)
            {
                Spacing = Plugin.Settings.DefaultSpacing.Value,
            };
            Wing = new FormationWing(Selection.Current, Selection.SpacingMetres);
            RosterChanged?.Invoke();
        }

        public void Deactivate()
        {
            WingTakeover.Reset();
            Interop.WingSquad.Reset();
            flyingPlayer = null;
            Members.Clear();
            FieldRegistry.Clear();
            LeaderUnit = null;
            Player = null;
            Anchor = null;
            Escorting = false;
            RosterChanged?.Invoke();
        }

        public void Tick(float dt)
        {
            LastFrameAiMs = aiTicks * 1000.0 / Stopwatch.Frequency;
            aiTicks = 0;
            if (Members.Count > 0)
            {
                aiMsSum += LastFrameAiMs;
                aiFrames++;
                if (LastFrameAiMs > AiMsMax) AiMsMax = LastFrameAiMs;
            }
            LogEvents();
            WingTakeover.Tick();
            WingSearchAndRescue.Tick();
            WingKillCredit.Tick();
        }

        public void FixedTick(float dt)
        {
            missionTime += dt;
            TrackLeader();
            StepPlanner(dt);
            SuperviseLandings();
            SuperviseCombat(dt);
            Prune();
            if (Plugin.Settings.DevTools.Value && (traceClock += dt) >= GroundTraceSeconds)
            {
                traceClock = 0f;
                TraceGround();
            }
            if (++probeTick >= ProbeTicks)
            {
                probeTick = 0;
                Probe(ProbeTicks * dt);
            }
        }

        /// <summary>Take over an initialised aircraft as the next member (host only; fixed-wing only in M1).</summary>
        public bool Adopt(Aircraft a) => AdoptMember(a, null) != null;

        /// <summary>The new member, its ground pilot (if any) attached before its state is entered: entering reads
        /// <see cref="WingMember.OnGround"/> to keep the gear down.</summary>
        private WingMember AdoptMember(Aircraft a, Func<WingMember, GroundPilot> ground)
        {
            if (Wing == null || a == null || a.pilots == null || a.pilots.Length == 0 || Members.Count >= MaxMembers) return null;
            if (ProfileReader.IsVtol(a))
            {
                WingToast.Show("VTOL aircraft cannot fly formation (the game gives them no AI to take over)");
                return null;
            }
            var m = new WingMember(a, Members.Count, WingProfiles.For(a)) { Id = nextMemberId++ };
            FlyAs(m, WingPilotRoster.Assign(a));
            m.State = new WingFlightState(m);
            if (ground != null) m.Ground = ground(m);
            Members.Add(m);
            m.Pilot.SwitchState(m.State);
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} {a.definition.unitName} joined");
            RosterChanged?.Invoke();
            return m;
        }

        /// <summary>The squadron pilot in the seat sets how precisely and how hard the member flies (rank and perks,
        /// scaled by Squadron/RankEffect; progression off flies every pilot at the base skill).</summary>
        private static void FlyAs(WingMember m, WingPilot pilot)
        {
            if (pilot == null) return;
            float effect = Plugin.Settings.PilotProgression.Value ? Plugin.Settings.RankEffect.Value : 0f;
            PilotSkill.For(pilot.Rank, pilot.Perks, effect, out float precision, out float aggression);
            m.Brain.Precision = precision;
            m.Brain.Aggression = aggression;
            Plugin.Logger.LogInfo($"[Pilot] {pilot.Callsign} ({WingPilotRoster.RankName(pilot.Rank)}) flies #{m.Number}: " +
                                  $"precision {precision:0.00}, aggression {aggression:0.00}");
        }

        /// <summary>The seat is empty: back in the pool when the aircraft came home or was handed over alive; lost when
        /// the pilot died; an ejection is settled by search and rescue.</summary>
        private static void RetirePilot(WingMember m)
        {
            // (object): an aircraft destroyed outside the game's own flow is Unity-null but still has its id.
            if ((object)m.Aircraft == null) return;
            bool home = m.Aircraft != null && m.Aircraft.unitState == Unit.UnitState.Returned;
            PilotFate fate = PilotFates.Of(home, m.Aircraft == null || m.Aircraft.disabled, m.Pilot == null,
                m.Pilot != null && m.Pilot.dead, m.Pilot != null && m.Pilot.ejected);
            if (home) WingPilotRoster.NoteSortie(m.Aircraft);
            if (home) WingLedger.Returned(m.Aircraft);
            else WingLedger.Forget(m.Aircraft);
            WingPilotRoster.Retire(m.Aircraft.persistentID, fate != PilotFate.Down);
        }

        /// <summary>Gear comes up this high on the climb-out.</summary>
        public static float GearUpHeight = 20f;

        /// <summary>A frame this long after the last one restarts the sensors (their derived acceleration would be stale).</summary>
        public static float SensorGapSeconds = 0.1f;

        public void StepMember(WingMember m)
        {
            if (Wing == null || m.Released) return;
            long start = Stopwatch.GetTimestamp();
            float dt = Time.fixedDeltaTime;
            try
            {
                WingFrame frame = FrameFor(Time.fixedTime, dt);
                if ((m.Recovery != null || m.ReserveNow) && StepRecovery(m, frame, dt)) return;
                if (m.OnGround)
                {
                    StepGround(m, dt);
                    return;
                }
                if (StepSettle(m, frame, dt)) return;
                m.NoFbwSeconds = m.Last.FbwActive ? 0f : m.NoFbwSeconds + dt;
                if (m.NoFbwSeconds >= NoFbwReleaseSeconds)
                {
                    Release(m, "no fly-by-wire (too slow or on the ground)");
                    return;
                }
                if (m.Recovery == null && !m.HasPendingRecovery) CheckBingo(m, dt);
                if (StepTest.Fly(m, dt)) return;
                m.Brain.Threat = ReadThreat(m);
                ControlOutput o = StepTest.Adjust(m, m.Brain.Step(frame, m.Last, m.Profile, missionTime, dt, Events), dt);
                ControlWriter.Fly(m.Aircraft, o, m.Profile.Class);
                Trigger(m.Aircraft, m.Brain.Mind.Current == BehaviourId.Defend && m.Brain.LastDefence.Countermeasures);
                FireFromSlot(m, dt);
                int slot = m.Brain.Slot;
                Metrics.Sample(m.Id, (frame.Slots[slot].Ref.Pos - m.Last.Pos).Length,
                    m.Brain.Mind.Current == BehaviourId.StationKeep, m.Last.Tas, missionTime, dt);
                if (Plugin.Settings.DevTools.Value && frameIndex % 3 == 0) TelemetryRecorder.Sample(m, frame, missionTime);
            }
            catch (Exception e)
            {
                if (m.OnGround)
                {
                    // Native taxi ejects stuck pilots: a faulting member on the ground holds its brakes instead.
                    ControlWriter.Fly(m.Aircraft, new ControlOutput { Brake = 1f }, m.Profile.Class);
                    if (!m.Faults.Record(missionTime))
                    {
                        Plugin.LogVerbose($"[Wing] #{m.Number} ground step failed: {e.Message}");
                        return;
                    }
                    Plugin.Logger.LogError($"[Wing] #{m.Number} failed three times in 10 s on the ground; releasing it: {e}");
                    Release(m, null);
                    return;
                }
                if (m.Faults.Record(missionTime))
                {
                    Plugin.Logger.LogError($"[Wing] #{m.Number} failed three times in 10 s; handing it to the game's AI: {e}");
                    Release(m, null);
                }
                else Plugin.LogVerbose($"[Wing] #{m.Number} step failed: {e.Message}");
            }
            finally
            {
                aiTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        /// <summary>A launched member on the ground: its ground pilot flies it (taxi, lineup, roll, climb-out), a pending
        /// relocation moves it, the gear comes up on the climb-out, and when it is done the formation brain takes over
        /// bumplessly and rejoins.</summary>
        private void StepGround(WingMember m, float dt)
        {
            m.NoFbwSeconds = 0f;
            ControlOutput o = m.Ground.Step(m.Last, m.Profile, m.Brain.Pipeline, missionTime, dt, Events, m.Brain.Slot);
            m.GroundOutput = o;
            ControlWriter.Fly(m.Aircraft, o, m.Profile.Class);
            LogLongStop(m);
            if (m.Ground.TakeRelocation(out Pose to)) SafeRelocate.Move(m.Aircraft, to);
            if (m.Ground.Phase == GroundPhase.Aborted)
            {
                Release(m, "could not take off");
                return;
            }
            if (m.Recovery != null)
            {
                StepRecoveryGround(m);
                if (m.Released) return;
            }
            bool climbing = m.Ground.Phase == GroundPhase.ClimbOut || m.Ground.Done;
            if (climbing && m.Last.RadarAlt > GearUpHeight && m.Aircraft.gearState != LandingGear.GearState.LockedRetracted)
                m.Aircraft.SetGear(false);
            if (!m.Ground.Done) return;
            m.Brain.Track(m.Last, o, m.Profile);
            m.Brain.FormUp(missionTime, Events);
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} airborne from the field; rejoining");
            AirborneAfterGround(m);
        }

        /// <summary>Adopt an aircraft launched on a field: it starts on the ground under a <see cref="GroundPilot"/>.</summary>
        public bool AdoptGround(Aircraft a, FieldTraffic field, Pose spawn, int hangarIndex, int startNode)
        {
            WingMember m = AdoptMember(a, n => new GroundPilot(n.Id, field, n.Profile.Class, spawn, hangarIndex, startNode));
            if (m == null) return false;
            if (m.Profile.Class == AirframeClass.FixedWing)
            {
                field.Departures.Expect(m.Id, LineupPlanner.Abreast(field.Runway.Width, m.Profile.SpanM));
            }
            else m.Ground.RoofOverhead = Physics.Raycast(a.transform.position + Vector3.up * 3f, Vector3.up, 30f);
            Events.Push(new WingEvent { Time = missionTime, Member = m.Brain.Slot, Kind = WingEventKind.GroundSpawned });
            return true;
        }

        /// <summary>A member under ground supervision, or in the game's landing for us, whose aircraft is intact is never
        /// ejected by native checks.</summary>
        /// <summary>The eject guard skipped an ejection of <paramref name="a"/>: a member in the game's landing is taken back
        /// at the next tick (review M3b I5: the game's helicopter landing with no field only ejects, every tick).</summary>
        public void EjectionBlocked(Aircraft a)
        {
            foreach (WingMember m in Members)
                if (ReferenceEquals(m.Aircraft, a) && m.Recovery != null && m.Recovery.Phase == RecoveryPhase.Landing) m.EjectBlocked = true;
        }

        /// <summary>The player took this member's seat (<see cref="WingTakeover"/>): it leaves the wing without a state
        /// switch (the server destroys the AI aircraft next), its pilot goes back to the pool, and its call cost is spent.</summary>
        public bool TakenOver(WingMember m)
        {
            if (m == null || m.Released || !Members.Contains(m)) return false;
            // The player flies again: the wing forms on the new aircraft, as a Form Up would (review M4a C2).
            if (Planner.Active) Order(WingTask.Form());
            m.Released = true;
            m.Ground?.Leave();
            m.Recovery?.Leave();
            if (m.Aircraft != null)
            {
                WingPilotRoster.Retire(m.Aircraft.persistentID, true);
                WingLedger.Forget(m.Aircraft);
            }
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} taken over by the player");
            return true;
        }

        /// <summary>Whether a faction aircraft already flying can join the wing (<see cref="WingRecruitment"/>), with the
        /// reason when it cannot.</summary>
        public bool CanRecruit(Aircraft a, out string reason)
        {
            reason = null;
            if (Wing == null) reason = "Wing Command is not ready";
            else if (!Flying(Player)) reason = "Not flying";
            else if (Members.Count + (SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0) >= MaxMembers)
                reason = "the wing is full";
            else if (a == null || a.disabled) reason = "it is no longer available";
            else if (ReferenceEquals(a, Player) || a.Player != null) reason = "a player flies it";
            else if (Player.NetworkHQ == null || a.NetworkHQ != Player.NetworkHQ) reason = "it is not in your faction";
            else if (IsMember(a)) reason = "it is already in the wing";
            else if (!Flying(a)) reason = "it has no pilot to fly it";
            else if (a.radarAlt < RecruitMinHeight) reason = "it must be airborne";
            else if (!a.LocalSim) reason = "this host does not fly it";
            return reason == null;
        }

        public static float RecruitMinHeight = 10f;

        /// <summary>A faction aircraft already flying joins the wing (checked by <see cref="CanRecruit"/>). One the game
        /// was landing leaves the runway's landing list and the pad's queue first (review M3c I3: either would close the
        /// field to takeoffs for as long as it lived).</summary>
        public WingMember Recruit(Aircraft a)
        {
            Pilot p = a != null && a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
            if (NativeLandingBridge.Landing(p))
            {
                NativeLandingBridge.Deregister(NativeLandingBridge.Field(p), a);
                NativeLandingBridge.LeavePad(p, a);
            }
            return AdoptMember(a, null);
        }

        public bool ProtectsFromEjection(Aircraft a)
        {
            foreach (WingMember m in Members)
                if (ReferenceEquals(m.Aircraft, a))
                    return !a.disabled && !m.Released && (m.OnGround || (m.Recovery != null && m.Recovery.Phase == RecoveryPhase.Landing));
            return false;
        }

        /// <summary>Hand the member to native AI (its combat state exists: air-starts went through
        /// SetStartingAiState); a member still on the surface of a field goes back to the reserve instead, since the
        /// native AI ejects a pilot sitting on the ground. It leaves the wing at the next prune.</summary>
        public void Release(WingMember m, string why)
        {
            if (m.Released) return;
            m.Released = true;
            // A helicopter down in the field goes back to the reserve (review M4c I3): the game's combat AI ejects a pilot
            // sitting still on the ground.
            bool settledDown = m.Settle != null && m.Last.RadarAlt < 5f;
            m.Settle = null;
            EndDefence(m);
            ReleasePad(m);
            bool despawn = settledDown || (m.Ground != null && m.Ground.DespawnOnRelease(m.Last));
            m.Ground?.Leave();
            if (despawn && m.Aircraft != null && !m.Aircraft.disabled)
            {
                ControlWriter.Fly(m.Aircraft, new ControlOutput { Brake = 1f }, m.Profile.Class);
                m.Aircraft.ReturnToInventory();
                Plugin.Logger.LogInfo($"[Wing] #{m.Number} returned to the reserve from the field{(why != null ? ": " + why : "")}");
                return;
            }
            if (why != null) Plugin.Logger.LogInfo($"[Wing] #{m.Number} released to the game's AI: {why}");
            Pilot p = m.Pilot;
            PilotBaseState combat = p != null ? NativeCombatState(p) : null;
            if (combat != null && !p.dead && ReferenceEquals(p.currentState, m.State)) p.SwitchState(combat);
        }

        /// <summary>The native state an airborne AI of this pilot type flies: planes get AICombatState, helicopters
        /// and tiltwings AIHeloCombatState (the game creates only the one matching the type).</summary>
        public static PilotBaseState NativeCombatState(Pilot p) =>
            p.pilotType == Pilot.PilotType.Plane ? p.AICombatState
            : p.pilotType == Pilot.PilotType.Helo || p.pilotType == Pilot.PilotType.Tiltwing ? p.AIHeloCombatState
            : null;

        public void FormUp()
        {
            if (Planner.Active) Order(WingTask.Form());
            Disengage();
            TakeOff();
            for (int i = 0; i < Members.Count; i++) Members[i].Brain.FormUp(missionTime, Events);
        }

        public void NextShape()
        {
            Selection.NextShape();
            ApplySelection();
        }

        public void NextFamily()
        {
            Selection.NextFamily();
            ApplySelection();
        }

        public void SetSpacing(SpacingPreset preset)
        {
            Selection.Spacing = preset;
            ApplySelection();
        }

        /// <summary>Picks a shape by id; an unknown id changes nothing and returns false.</summary>
        public bool SetShape(string id)
        {
            if (Selection == null || !Selection.Select(id)) return false;
            ApplySelection();
            return true;
        }

        /// <summary>Forms the wing on <paramref name="a"/> instead of the player; null forms on the player again.</summary>
        public void SetAnchor(Aircraft a)
        {
            if (Planner.Active) Order(WingTask.Form());
            Anchor = a;
            Escorting = false;
            Plugin.Logger.LogInfo(a != null
                ? $"[Wing] forming on {a.definition.unitName} '{a.unitName}'"
                : "[Wing] forming on the player");
            TrackLeader();
        }

        /// <summary>Escorts <paramref name="u"/> (an aircraft, a vehicle or a ship) in the escort shapes; null ends the
        /// escort and the wing forms on the player again in the shape it flew before.</summary>
        public void SetEscort(Unit u)
        {
            if (Planner.Active) Order(WingTask.Form());
            if (u is Aircraft a && IsMember(a)) u = null;
            Anchor = u;
            Escorting = u != null;
            Plugin.Logger.LogInfo(u != null ? $"[Wing] escorting {u.unitName}" : "[Wing] escort ended; forming on the player");
            TrackLeader();
        }

        /// <summary>The field the next call launches from (the player's pick; null: the nearest friendly one).</summary>
        public Airbase LaunchField { get; private set; }

        /// <summary>Friendly or unowned fields with a runway usable for takeoff, nearest to <paramref name="from"/> first.</summary>
        public static List<Airbase> FriendlyFields(Aircraft from)
        {
            var fields = new List<Airbase>();
            if (from == null) return fields;
            foreach (Airbase a in UnityEngine.Object.FindObjectsOfType<Airbase>())
            {
                if (a == null || a.disabled || a.runways == null) continue;
                if (a.CurrentHQ != null && a.CurrentHQ != from.NetworkHQ) continue;
                bool takeoff = false;
                foreach (Airbase.Runway r in a.runways)
                    if (r != null && r.Takeoff) takeoff = true;
                if (takeoff) fields.Add(a);
            }
            Vector3 at = from.transform.position;
            fields.Sort((x, y) => (x.transform.position - at).sqrMagnitude.CompareTo((y.transform.position - at).sqrMagnitude));
            return fields;
        }

        /// <summary>Fields <paramref name="type"/> can launch from: a jet not from a carrier (spike S6: assault carriers have
        /// no taxi network and narrow deck runways; deck operations are M3d's), a helicopter from any.</summary>
        private static List<Airbase> LaunchFields(Aircraft caller, AircraftDefinition type)
        {
            List<Airbase> fields = FriendlyFields(caller);
            if (ProfileReader.ClassOf(type ?? caller.definition) == AirframeClass.FixedWing) fields.RemoveAll(b => b.AttachedAirbase);
            return fields;
        }

        /// <summary>The picked field while it is still usable, else the nearest friendly field <paramref name="type"/> can
        /// launch from.</summary>
        public Airbase FieldFor(Aircraft caller, AircraftDefinition type = null)
        {
            List<Airbase> fields = LaunchFields(caller, type);
            if (LaunchField != null && fields.Contains(LaunchField)) return LaunchField;
            return fields.Count > 0 ? fields[0] : null;
        }

        /// <summary>Picks the next friendly field (nearest first, wrapping).</summary>
        public Airbase NextField(Aircraft caller, AircraftDefinition type = null)
        {
            List<Airbase> fields = LaunchFields(caller, type);
            if (fields.Count == 0) return LaunchField = null;
            int i = LaunchField != null ? fields.IndexOf(LaunchField) : -1;
            return LaunchField = fields[(i + 1) % fields.Count];
        }

        /// <summary>A ground anchor's collision radius is capped: a ship's bounding radius (well over 100 m) would push
        /// close-escort slots out of reach, and the terrain floor already keeps the wing off the surface.</summary>
        public static float GroundAnchorRadiusMax = 20f;

        private float AnchorRadius(AnchorKind kind)
        {
            float r = Alive(LeaderUnit) ? LeaderUnit.maxRadius : 8f;
            return kind == AnchorKind.Ground ? System.Math.Min(r, GroundAnchorRadiusMax) : r;
        }

        /// <summary>A wing member cannot be an anchor: it would chase a slot that moves with it.</summary>
        /// <summary>An aircraft of ours still on the ground at <paramref name="field"/>, null when there is none.</summary>
        public Aircraft GroundAircraftOn(FieldTraffic field)
        {
            foreach (WingMember m in Members)
                if (!m.Released && m.OnGround && m.Ground.Field == field && m.Aircraft != null && !m.Aircraft.disabled) return m.Aircraft;
            return null;
        }

        public bool IsMember(Aircraft a)
        {
            for (int i = 0; i < Members.Count; i++)
                if (ReferenceEquals(Members[i].Aircraft, a)) return true;
            return false;
        }

        public void Dismiss()
        {
            for (int i = 0; i < Members.Count; i++) Release(Members[i], "dismissed");
        }

        private void ApplySelection()
        {
            Wing.SetFormation(Selection.Current, Selection.SpacingMetres);
            WingToast.Show($"Formation: {Selection.Current.Name} · {Selection.Spacing} ({Selection.SpacingMetres:0} m)");
        }

        private WingFrame FrameFor(float time, float dt)
        {
            if (time == frameTime) return Wing.Frame;
            // After a gap (every member in a native state), every sensor starts afresh (review M5a C1).
            if (!float.IsNaN(frameTime) && time - frameTime > SensorGapSeconds)
            {
                leaderSensor.Restart();
                foreach (WingMember x in Members) x.Sensor.Restart();
            }
            frameTime = time;
            frameIndex++;
            FieldRegistry.Step(dt, this);
            int n = Members.Count;
            for (int i = 0; i < n; i++)
            {
                WingMember m = Members[i];
                m.Last = m.Sensor.Read(m.Aircraft, dt);
                inputs[i] = new WingMemberInput
                {
                    State = m.Last,
                    Capability = new MemberCapability
                    {
                        MaxSpeed = m.Profile.Class == AirframeClass.Rotary ? m.Profile.CruiseSpeed
                            : m.Brain.AfterburnerAllowed && m.Profile.HasAfterburner ? m.Profile.MaxSpeed : m.Profile.MilSpeed,
                        MinSpeed = m.Profile.MinimumSpeed(1f),
                    },
                    Radius = m.Profile.MaxRadius,
                    NearFloorY = m.NearFloorY,
                    HasNearFloor = !float.IsNaN(m.NearFloorY),
                    Role = m.Brain.Roles.Current,
                    Id = m.Id,
                    Grounded = m.OnGround,
                };
            }
            AnchorSample leader = SampleAnchor(dt);
            if (Planner.Active)
            {
                AnchorSample player = PlayerBody();
                SampleSeparation(player, n);
                Wing.Update(leader, player, inputs, n, floor.Value, Clearance, AnchorRadius(leader.Kind), dt);
                return Wing.Frame;
            }
            SampleSeparation(leader, n);
            Wing.Update(leader, inputs, n, floor.Value, Clearance, AnchorRadius(leader.Kind), dt);
            return Wing.Frame;
        }

        /// <summary>The closest pair this tick, the leader included, for <see cref="Metrics"/>.</summary>
        private void SampleSeparation(AnchorSample leader, int n)
        {
            float min = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vec3 p = inputs[i].State.Pos;
                if (leader.Present) min = Math.Min(min, (leader.Pos - p).Length);
                for (int j = i + 1; j < n; j++) min = Math.Min(min, (inputs[j].State.Pos - p).Length);
            }
            if (min < float.MaxValue) Metrics.Separation(min);
        }

        private static bool Flying(Aircraft a) =>
            a != null && !a.disabled && a.pilots != null && a.pilots.Length > 0 && !a.pilots[0].dead && !a.pilots[0].ejected;

        /// <summary>An aircraft that still flies, or any other unit that is not destroyed.</summary>
        private static bool Alive(Unit u) => u is Aircraft a ? Flying(a) : u != null && !u.disabled;

        /// <summary>Leader = the anchor while it lives, else the local player. A lost anchor is dropped once, with a log
        /// line (and for an escortee a toast and a WingEvent), so the wing re-forms on the player. A new leader restarts
        /// the estimate and sets the shape use for its class.</summary>
        private void TrackLeader()
        {
            Player = GameManager.GetLocalAircraft(out Aircraft local) ? local : null;
            WatchPlayerLoss();
            if ((object)Anchor != null && !Alive(Anchor))
            {
                Plugin.Logger.LogInfo(Escorting ? "[Wing] the escortee is gone; forming on the player" : "[Wing] the anchor is gone; forming on the player");
                if (Escorting)
                {
                    WingToast.Show("Escort lost; forming on you");
                    Events.Push(new WingEvent { Time = missionTime, Member = -1, Kind = WingEventKind.AnchorLost });
                }
                Anchor = null;
                Escorting = false;
            }
            Unit before = LeaderUnit;
            LeaderUnit = Anchor != null ? Anchor : Player;
            if (!ReferenceEquals(before, LeaderUnit))
            {
                // (object) casts: a destroyed anchor is Unity-null but still a different leader to reset from.
                // A task's lead does not change with the anchor unit (review M4a M1).
                if ((object)before != null && (object)LeaderUnit != null && !Planner.Active) Wing?.ResetLeader();
                leaderClass = LeaderUnit is Aircraft a ? ProfileReader.ClassOf(a) : AirframeClass.FixedWing;
            }
            UpdateUse();
        }

        private Aircraft flyingPlayer;
        private GlobalPosition flyingPlayerAt;

        /// <summary>The player's aircraft is lost — the pilot dead or ejected, or the aircraft destroyed, but not home at a
        /// field — so the wing's aircraft are offered to fly on in (<see cref="WingTakeover"/>).</summary>
        private void WatchPlayerLoss()
        {
            if (Flying(Player))
            {
                flyingPlayer = Player;
                flyingPlayerAt = Player.GlobalPosition();
                return;
            }
            if ((object)flyingPlayer == null) return;
            Aircraft lost = flyingPlayer;
            flyingPlayer = null;
            if (lost != null && lost.unitState == Unit.UnitState.Returned) return;
            if (WingTakeover.Active) return;
            if (WingTakeover.Begin(this, lost, flyingPlayerAt)) Plugin.Logger.LogInfo("[Wing] player aircraft lost; offering the wing's aircraft");
            else WingTakeover.FinishSuppressedDefeat();
        }

        /// <summary>Behind a helicopter the wing flies rotary shapes, escorting it flies escort shapes, otherwise jet
        /// shapes; each use gets back the shape it last flew.</summary>
        private void UpdateUse()
        {
            if (Selection == null) return;
            FormationUse wanted = Escorting ? FormationUse.Escort
                : leaderClass == AirframeClass.Rotary ? FormationUse.Rotary : FormationUse.Jet;
            if (wanted == Selection.Use) return;
            string fallback = wanted == FormationUse.Escort ? FormationSelection.EscortDefaultId(AnyRotaryMember())
                : wanted == FormationUse.Rotary ? RotaryDefaultShape : Plugin.Settings.DefaultFormation.Value;
            Selection.SetUse(wanted, fallback);
            ApplySelection();
        }

        private bool AnyRotaryMember()
        {
            foreach (WingMember m in Members)
                if (m.Profile.Class == AirframeClass.Rotary) return true;
            return false;
        }

        /// <summary>Where the anchor is now, without reading its sensor (that would disturb the estimate).</summary>
        private AnchorSample AnchorNow()
        {
            Unit u = LeaderUnit;
            return Alive(u) ? new AnchorSample { Pos = u.GlobalPosition().ToVec3(), Present = true } : default;
        }

        private AnchorSample SampleAnchor(float dt)
        {
            if (Planner.Active) return Planner.Sample();
            bool player = Anchor == null;
            Unit u = LeaderUnit;
            if (!Alive(u)) return new AnchorSample { Present = false, IsPlayer = player };
            if (u is Aircraft a)
            {
                AircraftState s = leaderSensor.Read(a, dt);
                return new AnchorSample
                {
                    Pos = s.Pos, Vel = s.Vel, BankDeg = s.BankDeg, Present = true, Airborne = a.radarAlt > 1f, IsPlayer = player,
                    Kind = player ? AnchorKind.Player : AnchorKind.Aircraft, CanHover = leaderClass != AirframeClass.FixedWing,
                    Fwd = s.Fwd,
                };
            }
            Rigidbody rb = u.rb;
            return new AnchorSample
            {
                Pos = u.GlobalPosition().ToVec3(), Vel = rb != null ? rb.velocity.ToVec3() : Vec3.Zero, Present = true,
                Kind = AnchorKind.Ground, Fwd = u.transform.forward.ToVec3(),
            };
        }

        private void Prune()
        {
            bool changed = false;
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                WingMember m = Members[i];
                bool ours = ReferenceEquals(m.Pilot.currentState, m.State) ||
                            (m.Recovery != null && m.Recovery.Phase == RecoveryPhase.Landing && NativeLandingBridge.Landing(m.Pilot)) ||
                            (m.Engaged && InNativeCombat(m));
                if (!m.Released && m.Alive && ours) continue;
                StepTest.Forget(m);
                m.Ground?.Leave();
                m.Recovery?.Leave();
                Unlist(m);
                ReleasePad(m);
                RetirePilot(m);
                Metrics.Left(m.Id);
                Members.RemoveAt(i);
                changed = true;
                Plugin.Logger.LogInfo($"[Wing] #{m.Number} left the wing: {LeaveReason(m, ours)}");
            }
            if (!changed) return;
            for (int i = 0; i < Members.Count; i++) Members[i].Brain.Slot = i;
            RosterChanged?.Invoke();
        }

        /// <summary>Why a member left (diagnostics).</summary>
        private static string LeaveReason(WingMember m, bool ours)
        {
            if (m.Released) return "released";
            if (m.Aircraft == null) return "aircraft gone";
            if (m.Aircraft.disabled) return $"aircraft disabled ({m.Aircraft.unitState})";
            if (m.Pilot == null) return "no pilot";
            if (m.Pilot.dead) return "pilot dead";
            if (m.Pilot.ejected) return "pilot ejected";
            return ours ? "unknown" : $"pilot state now {(m.Pilot.currentState != null ? m.Pilot.currentState.GetType().Name : "none")}";
        }

        public static float LongStopSeconds = 15f;

        /// <summary>Diagnostics: a member taxiing that has stood still for <see cref="LongStopSeconds"/> is logged once
        /// per stop with its phase, position and what holds it.</summary>
        private void LogLongStop(WingMember m)
        {
            GroundPhase phase = m.Ground.Phase;
            bool taxiing = phase == GroundPhase.TaxiOut || phase == GroundPhase.HoldShort || phase == GroundPhase.TaxiIn;
            if (!taxiing || m.Last.Speed > 0.5f)
            {
                m.StoppedSince = float.NaN;
                m.StopLogged = false;
                return;
            }
            if (float.IsNaN(m.StoppedSince)) m.StoppedSince = missionTime;
            if (m.StopLogged || missionTime - m.StoppedSince < LongStopSeconds) return;
            m.StopLogged = true;
            Plugin.Logger.LogInfo($"[Ground] #{m.Number} stopped {LongStopSeconds:0} s in {phase} at ({m.Last.Pos.X:0}, {m.Last.Pos.Z:0}): " +
                                  $"{m.Ground.Stop}{(m.Ground.StopWho >= 0 ? " (member " + NumberOf(m.Ground.StopWho) + ")" : "")}; " +
                                  $"command {m.Ground.LastCommand.Speed:0.0} m/s{(m.Ground.LastCommand.Stop ? " stop" : "")} curvature {m.Ground.LastCommand.Curvature:0.000}/m, " +
                                  $"yaw {m.Aircraft.GetInputs().yaw:0.00}, " +
                                  $"{m.Ground.StopDistance:0} m to the stop, throttle {m.Last.Throttle:0.00}, brake {m.Aircraft.GetInputs().brake:0.00}, " +
                                  $"radar {m.Last.RadarAlt:0.0}, pitch {m.Last.PitchDeg:0.0}, bank {m.Last.BankDeg:0.0}, gear {m.Aircraft.gearState}");
        }

        public static float GroundTraceSeconds = 10f;
        private float traceClock;
        private readonly HashSet<FieldTraffic> traced = new HashSet<FieldTraffic>();

        /// <summary>Dev tools: every <see cref="GroundTraceSeconds"/>, each member on a field (phase, why it stands, where,
        /// how fast) and each such field's traffic (<see cref="FieldTraffic.Describe"/>).</summary>
        private void TraceGround()
        {
            traced.Clear();
            foreach (WingMember m in Members)
            {
                if (m.Released) continue;
                if (!m.OnGround)
                {
                    RecoveryPilot r = m.Recovery;
                    if (r != null && (r.Phase == RecoveryPhase.Approach || r.Phase == RecoveryPhase.Landing))
                    {
                        // Live values: the sensor is not read while the game's landing flies the aircraft.
                        Vec3 pos = m.Aircraft.GlobalPosition().ToVec3();
                        Vec3 point = r.ApproachPoint(m.Last);
                        Plugin.Logger.LogInfo($"[Recovery] trace t={missionTime:0} #{m.Number} {r.Phase} to {r.Field.Field.Name}: " +
                                              $"{(point - pos).Horizontal.Length:0} m from the approach point, alt {pos.Y:0} " +
                                              $"(point {point.Y:0}), radar {m.Aircraft.radarAlt:0}, v {m.Aircraft.speed:0}, holding {r.Holding}, " +
                                              $"tries {r.FailedLandings}, state {m.Pilot?.currentState?.GetType().Name ?? "none"} " +
                                              $"{NativeLandingBridge.Mode(m.Pilot)}");
                    }
                    continue;
                }
                GroundPilot g = m.Ground;
                Plugin.Logger.LogInfo($"[Ground] trace t={missionTime:0} #{m.Number} id {m.Id} {g.Phase} stop={g.Stop}" +
                                      $"{(g.StopWho >= 0 ? " (" + NumberOf(g.StopWho) + ")" : "")} at ({m.Last.Pos.X:0}, {m.Last.Pos.Z:0}) " +
                                      $"v {m.Last.Speed:0.0} alt {m.Last.RadarAlt:0.0} throttle {m.GroundOutput.Throttle:0.00} brake {m.GroundOutput.Brake:0.0}" +
                                      (m.Profile.Class != AirframeClass.FixedWing
                                          ? $" rpm {m.Last.RotorRpm:0.00} roof {g.RoofOverhead} exited {g.ExitedHangar}" : ""));
                if (traced.Add(g.Field)) Plugin.Logger.LogInfo($"[Ground] trace {g.Field.Field.Name}: {g.Field.Describe()}");
            }
        }

        /// <summary>The wing number (#2…) of the member with <paramref name="id"/>, or its id when it has left.</summary>
        private string NumberOf(int id)
        {
            foreach (WingMember x in Members)
                if (x.Id == id) return "#" + x.Number;
            return "id " + id;
        }

        private void Probe(float dt)
        {
            bool any = false;
            float raw = 0f;
            if (Planner.Active || Alive(LeaderUnit))
            {
                AnchorSample l = SampleAnchor(0f);
                raw = TerrainProbe.LookAhead(l.Pos, l.Vel);
                // A task's lead is probed 15–30 s along its path too: it climbs at 5° and must see a ridge coming (review
                // M4a I4).
                if (Planner.Active) raw = Math.Max(raw, TerrainProbe.LookAheadFar(l.Pos, l.Vel));
                any = true;
            }
            for (int i = 0; i < Members.Count; i++)
            {
                WingMember m = Members[i];
                if (!m.Alive) continue;
                m.NearFloorY = TerrainProbe.Near(m.Last.Pos, m.Last.Vel);
                raw = Math.Max(raw, TerrainProbe.LookAhead(m.Last.Pos, m.Last.Vel));
                any = true;
            }
            if (any) floor.Update(raw, dt);
        }

        private void LogEvents()
        {
            long fresh = Math.Min(Events.Total - eventsLogged, Events.Count);
            for (int i = Events.Count - (int)fresh; i < Events.Count; i++)
            {
                WingEvent e = Events[i];
                Metrics.Event(e.Kind);
                if (e.Kind >= WingEventKind.TaskStarted && e.Kind <= WingEventKind.WaypointReached)
                {
                    Plugin.Logger.LogInfo(string.Format(CultureInfo.InvariantCulture, "[Wing] t={0:0.0} task {1} {2} ({3})",
                        e.Time, e.Task, e.Kind, e.Reason));
                    continue;
                }
                Plugin.Logger.LogInfo(string.Format(CultureInfo.InvariantCulture, "[Wing] t={0:0.0} #{1} {2} {3}->{4} ({5})",
                    e.Time, e.Member + 2, e.Kind, e.From, e.To, e.Reason));
                if (Plugin.Settings.DevTools.Value &&
                    (e.Kind == WingEventKind.GcasActivated || e.Kind == WingEventKind.CollisionEmergency))
                    TelemetryRecorder.AutoDump(e.Kind.ToString(), e.Time);
            }
            eventsLogged = Events.Total;
        }
    }
}
