using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>The wing's doctrine and standing fire from the slot (spec M5 §8): under Escort members cover the leader,
    /// under Sweep they take targets of opportunity, under Reserve they hold fire; only guided missiles whose launch
    /// envelope is met from where the member flies.</summary>
    internal sealed partial class WingService
    {
        public WingDoctrine Doctrine { get; private set; } = WingDoctrine.Reserve;
        /// <summary>Shots fired from the slot this session (automation reads it).</summary>
        public int StandingShots { get; private set; }

        private readonly Unit[] standingOthers = new Unit[FormationCatalog.MaxSlots];

        /// <summary>The game fires a missile only at a position this accurate (review M5d-2 I2).</summary>
        public static float FireAccuracyMetres = 100f;

        private void LoadDoctrine()
        {
            string text = Plugin.Settings != null ? Plugin.Settings.Doctrine.Value : "";
            if (!WingDoctrine.TryParse(text, out WingDoctrine d))
            {
                if (!string.IsNullOrWhiteSpace(text)) Plugin.Logger.LogWarning($"[Wing] doctrine '{text}' not understood; using RESERVE");
                d = WingDoctrine.Reserve;
            }
            Doctrine = d;
        }

        /// <summary>Reserve → Escort → Sweep → Reserve (a custom doctrine goes to Reserve), saved to the config.</summary>
        public WingDoctrine NextDoctrine()
        {
            SetDoctrine(Doctrine.NextPattern());
            return Doctrine;
        }

        /// <summary>Element <paramref name="element"/>'s own doctrine (A and below: the wing's).</summary>
        public void SetDoctrine(WingDoctrine d, int element)
        {
            if (element <= 0)
            {
                SetDoctrine(d);
                return;
            }
            Settings.SetDoctrine(element, d);
            Plugin.Logger.LogInfo($"[Wing] element {ElementRoster.Letter(element)} doctrine {d.PatternName}");
        }

        /// <summary>The doctrine element <paramref name="e"/> flies (spec WMC program §4).</summary>
        public WingDoctrine DoctrineOf(int e) => Settings.DoctrineOf(e, Doctrine);

        /// <summary>The doctrine member <paramref name="m"/> flies: its element's, then its own (spec WMC rebuild R3).</summary>
        public WingDoctrine DoctrineFor(WingMember m) =>
            Settings.Members.Resolve((object)m.Aircraft != null ? m.Aircraft.persistentID.Id : 0u, DoctrineOf(ElementOf(m)));

        /// <summary>Whether <paramref name="m"/> keeps its own value of <paramref name="axis"/> when its element's changes.</summary>
        public bool KeepsOwn(WingMember m, DoctrineAxis axis) =>
            (object)m.Aircraft != null && Settings.Members.Has(m.Aircraft.persistentID.Id, axis);

        /// <summary>One setting for the whole wing (<paramref name="element"/> &lt; 0: every element, the detached ones with
        /// their own doctrine too) or one element (A: the wing's doctrine, with B-D pinned first so they keep flying theirs).</summary>
        public void SetAxis(int element, DoctrineAxis axis, byte value)
        {
            if (element > 0)
            {
                Settings.SetDoctrine(element, DoctrineOf(element).With(axis, value));
                Plugin.Logger.LogInfo($"[Wing] element {ElementRoster.Letter(element)} {axis} {value}");
                return;
            }
            for (int e = 1; e < ElementRoster.MaxElements; e++)
            {
                if (!Roster.InUse(e)) continue;
                if (Settings.HasDoctrine(e)) { if (element < 0) Settings.SetDoctrine(e, DoctrineOf(e).With(axis, value)); }
                else if (element == 0) Settings.SetDoctrine(e, Doctrine);
            }
            SetDoctrine(Doctrine.With(axis, value));
        }

        /// <summary>A setting of member <paramref name="m"/>'s own (targets, reach, weapons, radar); false when it cannot.</summary>
        public bool SetMemberAxis(WingMember m, DoctrineAxis axis, byte value) =>
            (object)m.Aircraft != null && Settings.Members.SetOverride(m.Aircraft.persistentID.Id, axis, value);

        /// <summary>A profile of member <paramref name="m"/>'s own; its single settings go.</summary>
        public bool SetMemberDoctrine(WingMember m, WingDoctrine d) =>
            (object)m.Aircraft != null && Settings.Members.SetBase(m.Aircraft.persistentID.Id, d);

        public void SetDoctrine(WingDoctrine d)
        {
            Doctrine = d;
            // Every axis survives a restart (a preset saves its name, anything else its eight values).
            if (Plugin.Settings != null) Plugin.Settings.Doctrine.Value = d.ToString();
            Plugin.Logger.LogInfo($"[Wing] doctrine {d.PatternName} ({d})");
        }

        private static bool HoldsFormation(BehaviourId b) =>
            b == BehaviourId.StationKeep || b == BehaviourId.Rejoin || b == BehaviourId.HoldOverhead || b == BehaviourId.Trail;

        /// <summary>After the member's flight step: a missile from the slot when the doctrine allows one (spec M5 §8.2).</summary>
        private void FireFromSlot(WingMember m, float dt)
        {
            if (!m.Cadence.Due(dt, m.Perks.IntervalScale)) return;
            m.StandingTarget = null;
            if (m.Engaged || m.Recovery != null || m.OnGround || m.Released || !HoldsFormation(m.Brain.Mind.Current)) return;
            WingDoctrine doctrine = DoctrineFor(m);
            StandingMode mode = StandingFire.Decide(doctrine.Targets, out DoctrineAllow allow);
            if (mode == StandingMode.None) return;
            Aircraft a = m.Aircraft;
            FactionHQ hq = a != null ? a.NetworkHQ : null;
            if (hq == null || hq.trackingDatabase == null || a.weaponStations == null || a.weaponManager == null) return;
            float range = WingDoctrineRules.EngageRange(doctrine.Reach) * m.Perks.ReachScale;
            AnchorSample anchor = Planner.Active ? Planner.Sample() : AnchorNow();
            Vec3 cover = anchor.Present ? anchor.Pos : a.GlobalPosition().ToVec3();
            int others = 0;
            // Committed: the others' standing targets this check and the engaged members' own targets (review M5d-2 I1).
            foreach (WingMember o in Members)
            {
                if (ReferenceEquals(o, m) || others >= standingOthers.Length) continue;
                Unit taken = o.Engaged ? NativeTarget(o) : o.StandingTarget;
                if (taken != null) standingOthers[others++] = taken;
            }

            GlobalPosition at = a.GlobalPosition();
            Vector3 nose = a.transform.forward;
            Unit best = null;
            WeaponStation bestStation = null;
            float bestScore = 0f;
            foreach (KeyValuePair<PersistentID, TrackingInfo> pair in hq.trackingDatabase)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !t.TryGetUnit(out Unit u) || u == null || u.disabled || u is Missile || u.NetworkHQ == null || u.NetworkHQ == hq) continue;
                bool air = u.definition != null && u.definition.typeIdentity.air > 0.5f;
                float fromCover = (t.GetPosition().ToVec3() - cover).Length;
                if (mode == StandingMode.Cover ? !(u is Aircraft) || fromCover > range : !StandingFire.Allows(allow, air)) continue;
                if (!WeaponsFilter.AllowsTarget(doctrine.Weapons, air)) continue;
                Vector3 to = t.GetPosition() - at;
                float distance = to.magnitude;
                if (distance > range || !hq.IsTargetPositionAccurate(u, FireAccuracyMetres)) continue;
                float off = Vector3.Angle(nose, to);
                int committed = 0;
                for (int i = 0; i < others; i++)
                    if (ReferenceEquals(standingOthers[i], u)) committed++;
                foreach (WeaponStation w in a.weaponStations)
                {
                    if (!UsableBy(m, doctrine, w) || !w.WeaponInfo.missile || w.WeaponInfo.gun || w.WeaponInfo.bomb) continue;
                    TargetRequirements req = w.WeaponInfo.targetRequirements;
                    float maxRange = PerkRange(m, w, a, u, to);
                    if (!StandingFire.InEnvelope(distance, req.minRange, maxRange, u.radarAlt, req.minAltitude, req.maxAltitude, off, m.Perks.Boresight(req.minAlignment), a.speed, req.minOwnerSpeed)) continue;
                    if (StandingFire.Saturated(committed, t.missileAttacks, w.WeaponInfo.CalcAttacksNeeded(u))) continue;
                    OpportunityThreat ot = CombatAI.AnalyzeTarget(w, a, t, 0f, distance, maxRange / Mathf.Max(req.maxRange, 1f));
                    if (ot.opportunity <= 0f) continue;
                    int capacity = System.Math.Max(1, System.Math.Min(4, (int)System.Math.Ceiling(w.WeaponInfo.CalcAttacksNeeded(u))));
                    float score = mode == StandingMode.Cover
                        ? 1f / System.Math.Max(fromCover, 1f) / (1f + committed)
                        : TargetSpread.Score(ot.opportunity, ot.threat, distance, req.maxRange, committed, false, capacity);
                    if (score <= bestScore) continue;
                    // Line of sight last (a raycast), as the game's gate: a missile that cannot see its target is wasted.
                    if (!w.WeaponInfo.overHorizon && !u.LineOfSight(a.transform.position - Vector3.up * a.definition.spawnOffset.y, 1000f)) continue;
                    bestScore = score;
                    best = u;
                    bestStation = w;
                }
            }
            for (int i = 0; i < others; i++) standingOthers[i] = null;
            if (best == null) return;
            bool launched = FireAt(m, bestStation, best, out bool attempted);
            // Any attempt waits the full interval (a launch the ammo count has not shown yet must not be doubled).
            if (attempted) m.Cadence.Fired();
            if (!launched) return;
            StandingShots++;
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} fox on {best.unitName} ({doctrine.PatternName})");
            CallFox(m, bestStation, best);
        }

        /// <summary>Spec M5 §9.2 Splash: one guided missile at <paramref name="target"/> from every member flying with the wing
        /// that has it in a missile's envelope from its slot (engaged members are the combat AI's). Returns how many fired;
        /// <paramref name="capable"/> is how many had it in envelope.</summary>
        public int Splash(Unit target, out int capable, Func<WingMember, bool> who = null)
        {
            capable = 0;
            if (target == null || target.disabled) return 0;
            int fired = 0;
            foreach (WingMember m in Members)
            {
                if (m.Released || !m.Alive || m.Engaged || m.OnGround || m.Recovery != null || m.Settle != null) continue;
                if (who != null && !who(m)) continue;
                Aircraft a = m.Aircraft;
                FactionHQ hq = a.NetworkHQ;
                if (a.weaponStations == null || a.weaponManager == null || hq == null || hq.trackingDatabase == null) continue;
                // Spec WMC rebuild R3: a member whose WEAPONS forbid this target or every missile aboard sits it out.
                WingDoctrine doctrine = DoctrineFor(m);
                if (!WeaponsFilter.AllowsTarget(doctrine.Weapons, IsAir(target))) continue;
                // As standing fire and the game's own release gate (review M5e I2): a track accurate to 100 m, measured from
                // the tracked position, and a weapon the game rates as able to hurt this target.
                if (!hq.trackingDatabase.TryGetValue(target.persistentID, out TrackingInfo t) || t == null ||
                    !hq.IsTargetPositionAccurate(target, FireAccuracyMetres)) continue;
                Vector3 to = t.GetPosition() - a.GlobalPosition();
                float distance = to.magnitude, off = Vector3.Angle(a.transform.forward, to);
                bool inEnvelope = false;
                int shots = 0;
                foreach (WeaponStation w in a.weaponStations)
                {
                    if (!UsableBy(m, doctrine, w) || !w.WeaponInfo.missile || w.WeaponInfo.gun || w.WeaponInfo.bomb) continue;
                    TargetRequirements req = w.WeaponInfo.targetRequirements;
                    float maxRange = PerkRange(m, w, a, target, to);
                    if (!StandingFire.InEnvelope(distance, req.minRange, maxRange, target.radarAlt, req.minAltitude, req.maxAltitude, off, m.Perks.Boresight(req.minAlignment), a.speed, req.minOwnerSpeed)) continue;
                    if (CombatAI.AnalyzeTarget(w, a, t, 0f, distance, maxRange / Mathf.Max(req.maxRange, 1f)).opportunity <= 0f) continue;
                    if (!w.WeaponInfo.overHorizon && !target.LineOfSight(a.transform.position - Vector3.up * a.definition.spawnOffset.y, 1000f)) continue;
                    inEnvelope = true;
                    bool launched = FireAt(m, w, target, out bool attempted);
                    // A shot (or one the ammo count has not shown yet) restarts standing fire's interval; a station not
                    // ready lets the next one try.
                    if (attempted) m.Cadence.Fired();
                    if (launched)
                    {
                        fired++;
                        shots++;
                        Plugin.Logger.LogInfo($"[Wing] #{m.Number} splash on {target.unitName}");
                        CallFox(m, w, target);
                    }
                    // SalvoSpecialist: a second missile from another station (spec M5 §11).
                    if (attempted && (!launched || shots >= m.Perks.SplashShots)) break;
                }
                if (inEnvelope) capable++;
            }
            SplashShots += fired;
            return fired;
        }

        /// <summary>A missile's max range with the member's range perks (HeadOnJoust, ApexHunter: spec M5 §11).</summary>
        private static float PerkRange(WingMember m, WeaponStation w, Aircraft a, Unit target, Vector3 to)
        {
            TargetRequirements req = w.WeaponInfo.targetRequirements;
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 rel = targetVel - (a.rb != null ? a.rb.velocity : Vector3.zero);
            float d = Mathf.Max(to.magnitude, 1f);
            float closing = -Vector3.Dot(rel, to / d);
            // Aspect: the target's heading against the line back to us (0 = straight at us).
            float aspect = targetVel.sqrMagnitude > 1f ? Vector3.Angle(targetVel, -to) : 180f;
            // An air-to-air radar missile (not infrared); never a ground shot (laser, TV, anti-radiation): review M5g C2.
            bool radar = target is Aircraft && req.minIR <= 0f;
            return m.Perks.MaxRange(req.maxRange, radar, a.GlobalPosition().y, closing, aspect);
        }

        /// <summary>Spec M7 §3: the launch on the radio (one Fox call per speaker per repeat window).</summary>
        private static void CallFox(WingMember m, WeaponStation w, Unit target)
        {
            RadioDirector radio = RadioDirector.Instance;
            if (radio == null) return;
            TargetRequirements req = w.WeaponInfo.targetRequirements;
            string type = target.definition != null ? target.definition.unitName : target.unitName;
            radio.Say(m, RadioClass.Tactical, RadioCalls.ShotLine(target is Aircraft, req.minIR > 0f, req.minRadar > 0f), type, false);
        }

        /// <summary>Missiles fired by Splash this mission (automation reads it).</summary>
        public int SplashShots { get; private set; }

        /// <summary>The game's stock sequence, as 0.9 fired from the slot: the station, the target list (each change sends
        /// the station's targets), the pilot's primary target, then the pilot's fire.</summary>
        private static bool FireAt(WingMember m, WeaponStation station, Unit target, out bool attempted)
        {
            attempted = false;
            WeaponManager wm = m.Aircraft.weaponManager;
            // The game's fire returns silently with the safety on (gear not up on a gear-safety station): no shot then.
            if (m.Pilot == null || !station.Ready() || station.SalvoInProgress || station.SafetyIsOn(m.Aircraft)) return false;
            if (wm.currentWeaponStation != null && wm.currentWeaponStation.SalvoInProgress) return false;
            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            m.Pilot.SetPrimaryTarget(target);
            int before = station.Ammo;
            attempted = true;
            m.Pilot.Fire();
            // Counted only when a missile left the rail (review M5d-2 I3).
            if (station.Ammo >= before) return false;
            m.StandingTarget = target;
            return true;
        }
    }
}
