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

        public void SetDoctrine(WingDoctrine d)
        {
            Doctrine = d;
            if (Plugin.Settings != null) Plugin.Settings.Doctrine.Value = d.PatternName == "CUSTOM" ? Plugin.Settings.Doctrine.Value : d.PatternName;
            Plugin.Logger.LogInfo($"[Wing] doctrine {d.PatternName}");
        }

        private static bool HoldsFormation(BehaviourId b) =>
            b == BehaviourId.StationKeep || b == BehaviourId.Rejoin || b == BehaviourId.HoldOverhead || b == BehaviourId.Trail;

        /// <summary>After the member's flight step: a missile from the slot when the doctrine allows one (spec M5 §8.2).</summary>
        private void FireFromSlot(WingMember m, float dt)
        {
            if (!m.Cadence.Due(dt)) return;
            m.StandingTarget = null;
            if (m.Engaged || m.Recovery != null || m.OnGround || m.Released || !HoldsFormation(m.Brain.Mind.Current)) return;
            StandingMode mode = StandingFire.Decide(Doctrine.Targets, out DoctrineAllow allow);
            if (mode == StandingMode.None) return;
            Aircraft a = m.Aircraft;
            FactionHQ hq = a != null ? a.NetworkHQ : null;
            if (hq == null || hq.trackingDatabase == null || a.weaponStations == null || a.weaponManager == null) return;
            float range = WingDoctrineRules.EngageRange(Doctrine.Reach);
            AnchorSample anchor = Planner.Active ? Planner.Sample() : AnchorNow();
            Vec3 cover = anchor.Present ? anchor.Pos : a.GlobalPosition().ToVec3();
            int others = 0;
            foreach (WingMember o in Members)
                if (!ReferenceEquals(o, m) && o.StandingTarget != null && others < standingOthers.Length) standingOthers[others++] = o.StandingTarget;

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
                Vector3 to = t.GetPosition() - at;
                float distance = to.magnitude;
                if (distance > range || !hq.IsTargetPositionAccurate(u, TargetAccuracyMetres)) continue;
                float off = Vector3.Angle(nose, to);
                int committed = 0;
                for (int i = 0; i < others; i++)
                    if (ReferenceEquals(standingOthers[i], u)) committed++;
                foreach (WeaponStation w in a.weaponStations)
                {
                    if (!Usable(a, w) || !w.WeaponInfo.missile || w.WeaponInfo.gun || w.WeaponInfo.bomb) continue;
                    TargetRequirements req = w.WeaponInfo.targetRequirements;
                    if (!StandingFire.InEnvelope(distance, req.minRange, req.maxRange, u.radarAlt, req.minAltitude, req.maxAltitude, off, req.minAlignment)) continue;
                    OpportunityThreat ot = CombatAI.AnalyzeTarget(w, a, t, 0f, distance, 1f);
                    if (ot.opportunity <= 0f) continue;
                    int capacity = System.Math.Max(1, System.Math.Min(4, (int)System.Math.Ceiling(w.WeaponInfo.CalcAttacksNeeded(u))));
                    float score = mode == StandingMode.Cover
                        ? 1f / System.Math.Max(fromCover, 1f) / (1f + committed)
                        : TargetSpread.Score(ot.opportunity, ot.threat, distance, req.maxRange, committed, false, capacity);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = u;
                    bestStation = w;
                }
            }
            for (int i = 0; i < others; i++) standingOthers[i] = null;
            if (best == null || !FireAt(m, bestStation, best)) return;
            m.Cadence.Fired();
            StandingShots++;
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} fox on {best.unitName} ({Doctrine.PatternName})");
        }

        /// <summary>The game's stock sequence, as 0.9 fired from the slot: the station, the target list (each change sends
        /// the station's targets), the pilot's primary target, then the pilot's fire.</summary>
        private static bool FireAt(WingMember m, WeaponStation station, Unit target)
        {
            WeaponManager wm = m.Aircraft.weaponManager;
            if (m.Pilot == null || !station.Ready() || station.SalvoInProgress) return false;
            if (wm.currentWeaponStation != null && wm.currentWeaponStation.SalvoInProgress) return false;
            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            m.Pilot.SetPrimaryTarget(target);
            m.Pilot.Fire();
            m.StandingTarget = target;
            return true;
        }
    }
}
