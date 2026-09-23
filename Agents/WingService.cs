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
    /// <item>The leader is the anchor aircraft while one is set and alive (automation now, escort in M2), else the
    /// local player. A dead or despawned anchor falls back to the player.</item>
    /// <item><see cref="Metrics"/> accumulates formation quality for the automated in-game scenarios.</item>
    /// </list></summary>
    internal sealed class WingService : IWingService
    {
        public const int ProbeTicks = 12;
        public static float Clearance = 60f, NoFbwReleaseSeconds = 1f;

        public static WingService Instance { get; private set; }

        public string Name => "Wing";
        public readonly List<WingMember> Members = new List<WingMember>(FormationCatalog.MaxSlots);
        public WingEventRing Events { get; private set; } = new WingEventRing();
        public FormationSelection Selection { get; private set; }
        public FormationWing Wing { get; private set; }
        public Aircraft Leader { get; private set; }
        public Aircraft Anchor { get; private set; }
        public WingMetrics Metrics { get; } = new WingMetrics();
        public float MissionTime => missionTime;
        public double LastFrameAiMs { get; private set; }
        public event Action RosterChanged;

        private readonly WingMemberInput[] inputs = new WingMemberInput[FormationCatalog.MaxSlots];
        private readonly AircraftSensor leaderSensor = new AircraftSensor();
        private TerrainFloor floor = new TerrainFloor();
        private float frameTime = float.NaN, missionTime;
        private int probeTick, frameIndex, nextMemberId;
        private long eventsLogged, aiTicks;

        public WingService() => Instance = this;

        public static int MaxMembers => Plugin.Settings.MaxWingmen.Value;

        public void Activate()
        {
            Members.Clear();
            Events = new WingEventRing();
            floor = new TerrainFloor();
            Leader = null;
            Anchor = null;
            frameTime = float.NaN;
            missionTime = 0f;
            eventsLogged = 0;
            Metrics.Reset(0f);
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
            Members.Clear();
            Leader = null;
            Anchor = null;
            RosterChanged?.Invoke();
        }

        public void Tick(float dt)
        {
            LastFrameAiMs = aiTicks * 1000.0 / Stopwatch.Frequency;
            aiTicks = 0;
            LogEvents();
        }

        public void FixedTick(float dt)
        {
            missionTime += dt;
            TrackLeader();
            Prune();
            if (++probeTick >= ProbeTicks)
            {
                probeTick = 0;
                Probe(ProbeTicks * dt);
            }
        }

        /// <summary>Take over an initialised aircraft as the next member (host only; fixed-wing only in M1).</summary>
        public bool Adopt(Aircraft a)
        {
            if (Wing == null || a == null || a.pilots == null || a.pilots.Length == 0 || Members.Count >= MaxMembers) return false;
            if (ProfileReader.IsVtol(a))
            {
                WingToast.Show("VTOL aircraft cannot fly formation (the game gives them no AI to take over)");
                return false;
            }
            var m = new WingMember(a, Members.Count, WingProfiles.For(a)) { Id = nextMemberId++ };
            m.State = new WingFlightState(m);
            Members.Add(m);
            m.Pilot.SwitchState(m.State);
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} {a.definition.unitName} joined");
            RosterChanged?.Invoke();
            return true;
        }

        public void StepMember(WingMember m)
        {
            if (Wing == null || m.Released) return;
            long start = Stopwatch.GetTimestamp();
            float dt = Time.fixedDeltaTime;
            try
            {
                WingFrame frame = FrameFor(Time.fixedTime, dt);
                m.NoFbwSeconds = m.Last.FbwActive ? 0f : m.NoFbwSeconds + dt;
                if (m.NoFbwSeconds >= NoFbwReleaseSeconds)
                {
                    Release(m, "no fly-by-wire (too slow or on the ground)");
                    return;
                }
                if (StepTest.Fly(m, dt)) return;
                ControlOutput o = StepTest.Adjust(m, m.Brain.Step(frame, m.Last, m.Profile, missionTime, dt, Events), dt);
                ControlWriter.Fly(m.Aircraft, o, m.Profile.Class);
                int slot = m.Brain.Slot;
                Metrics.Sample(m.Id, (frame.Slots[slot].Ref.Pos - m.Last.Pos).Length,
                    m.Brain.Mind.Current == BehaviourId.StationKeep, m.Last.Tas, missionTime, dt);
                if (Plugin.Settings.DevTools.Value && frameIndex % 3 == 0) TelemetryRecorder.Sample(m, frame, missionTime);
            }
            catch (Exception e)
            {
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

        /// <summary>Hand the member to native AI (its combat state exists: air-starts went through
        /// SetStartingAiState). It leaves the wing at the next prune.</summary>
        public void Release(WingMember m, string why)
        {
            if (m.Released) return;
            m.Released = true;
            if (why != null) Plugin.Logger.LogInfo($"[Wing] #{m.Number} released to the game's AI: {why}");
            Pilot p = m.Pilot;
            if (p != null && !p.dead && ReferenceEquals(p.currentState, m.State) && p.AICombatState != null)
                p.SwitchState(p.AICombatState);
        }

        public void FormUp()
        {
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
            Anchor = a;
            Plugin.Logger.LogInfo(a != null
                ? $"[Wing] forming on {a.definition.unitName} '{a.unitName}'"
                : "[Wing] forming on the player");
            TrackLeader();
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
            frameTime = time;
            frameIndex++;
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
                };
            }
            AnchorSample leader = SampleAnchor(dt);
            SampleSeparation(leader, n);
            Wing.Update(leader, inputs, n, floor.Value, Clearance, LeaderAlive ? Leader.maxRadius : 8f, dt);
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

        private bool LeaderAlive => Flying(Leader);

        private static bool Flying(Aircraft a) =>
            a != null && !a.disabled && a.pilots != null && a.pilots.Length > 0 && !a.pilots[0].dead && !a.pilots[0].ejected;

        /// <summary>Leader = the anchor while it flies, else the local player. A lost anchor is dropped once, with a log
        /// line, so the wing re-forms on the player.</summary>
        private void TrackLeader()
        {
            if ((object)Anchor != null && !Flying(Anchor))
            {
                Plugin.Logger.LogInfo("[Wing] the anchor is gone; forming on the player");
                Anchor = null;
            }
            Aircraft before = Leader;
            Leader = Anchor != null ? Anchor : GameManager.GetLocalAircraft(out Aircraft local) ? local : null;
            if (before != null && Leader != null && !ReferenceEquals(before, Leader)) Wing?.ResetLeader();
        }

        private AnchorSample SampleAnchor(float dt)
        {
            bool player = Anchor == null;
            if (!LeaderAlive) return new AnchorSample { Present = false, IsPlayer = player };
            AircraftState s = leaderSensor.Read(Leader, dt);
            return new AnchorSample
            {
                Pos = s.Pos, Vel = s.Vel, BankDeg = s.BankDeg, Present = true, Airborne = Leader.radarAlt > 1f, IsPlayer = player,
            };
        }

        private void Prune()
        {
            bool changed = false;
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                WingMember m = Members[i];
                if (!m.Released && m.Alive && ReferenceEquals(m.Pilot.currentState, m.State)) continue;
                StepTest.Forget(m);
                Metrics.Left(m.Id);
                Members.RemoveAt(i);
                changed = true;
                Plugin.Logger.LogInfo($"[Wing] #{m.Number} left the wing");
            }
            if (!changed) return;
            for (int i = 0; i < Members.Count; i++) Members[i].Brain.Slot = i;
            RosterChanged?.Invoke();
        }

        private void Probe(float dt)
        {
            bool any = false;
            float raw = 0f;
            if (LeaderAlive)
            {
                AircraftState l = leaderSensor.Read(Leader, 0f);
                raw = TerrainProbe.LookAhead(l.Pos, l.Vel);
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
                Plugin.Logger.LogInfo(string.Format(CultureInfo.InvariantCulture, "[Wing] t={0:0.0} #{1} {2} {3}->{4} ({5})",
                    e.Time, e.Member + 2, e.Kind, e.From, e.To, e.Reason));
                if (e.Kind == WingEventKind.FallingBehind) WingToast.Show($"#{e.Member + 2} falling behind");
                if (Plugin.Settings.DevTools.Value &&
                    (e.Kind == WingEventKind.GcasActivated || e.Kind == WingEventKind.CollisionEmergency))
                    TelemetryRecorder.AutoDump(e.Kind.ToString(), e.Time);
            }
            eventsLogged = Events.Total;
        }
    }
}
