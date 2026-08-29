using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Fires a wingman's weapons at a chosen target without taking over its flying.
    ///
    /// This is what lets a Defensive wingman shoot while never leaving its slot: the
    /// formation controller keeps owning attitude and throttle, and this only touches the
    /// weapon manager. It reuses the exact sequence the stock AI uses in
    /// <c>AIPilotCombatModes</c> — select station, replace the target list, notify, fire —
    /// rather than inventing a parallel firing path.
    /// </summary>
    internal static class WingWeapons
    {
        /// <summary>What a wingman is currently allowed to shoot at.</summary>
        internal enum Allow
        {
            None,
            MissilesOnly,
            AirOnly,
            AirAndGround,
            GroundOnly,
        }

        /// <summary>
        /// Try to engage the highest-value permitted target. Returns true if it fired.
        /// </summary>
        public static bool Engage(Aircraft aircraft, Pilot pilot, Allow allow, float maxRange)
        {
            if (aircraft == null || pilot == null || allow == Allow.None) return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            // Never interrupt a salvo already in progress.
            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            if (allow == Allow.MissilesOnly)
                return InterceptMissiles(aircraft, pilot,
                    RoeRules.MissileDefenceProtectee(aircraft) ?? aircraft);

            Unit target = ChooseTarget(aircraft, allow, maxRange,
                                       out WeaponStation station, out int capacity);
            if (target == null || station == null) return false;
            if (!ShotIsValid(aircraft, station, target)) return false;
            if (!TacticalCoordinator.TryClaim(
                    target, aircraft, capacity,
                    Mathf.Max(Plugin.Config2.FireInterval.Value * 1.5f, 3f)))
                return false;

            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            wm.TargetListChanged();

            pilot.SetPrimaryTarget(target);
            pilot.Fire();
            return true;
        }

        /// <summary>
        /// Whether this shot is actually worth taking, using the weapon's own stated
        /// requirements.
        ///
        /// Without this a wingman fires on every engagement tick the moment anything
        /// hostile is loosely in range, and empties its entire loadout in a few seconds.
        /// The stock AI gates the same way — minimum range, alignment to the nose, and a
        /// cooldown between launches.
        /// </summary>
        private static bool ShotIsValid(Aircraft aircraft, WeaponStation station, Unit target)
        {
            WeaponInfo info = station.WeaponInfo;
            if (info == null) return false;

            TargetRequirements req = info.targetRequirements;

            float distance = FastMath.Distance(target.GlobalPosition(), aircraft.GlobalPosition());
            if (req.maxRange > 0f && distance > req.maxRange) return false;
            if (distance < req.minRange) return false;

            // Alignment: the target has to be somewhere near the nose. minAlignment is the
            // widest off-boresight angle the weapon accepts.
            if (req.minAlignment > 0f)
            {
                Vector3 toTarget = target.GlobalPosition() - aircraft.GlobalPosition();
                if (Vector3.Angle(aircraft.transform.forward, toTarget) > req.minAlignment)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Fire at one specific unit, chosen by the player rather than by the wingman.
        /// Returns true if it fired.
        /// </summary>
        public static bool EngageSpecific(Aircraft aircraft, Pilot pilot, Unit target, float maxRange)
        {
            if (aircraft == null || pilot == null || target == null || target.disabled) return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            if (FastMath.SquareDistance(target.GlobalPosition(), aircraft.GlobalPosition())
                > maxRange * maxRange)
                return false;

            bool isAir = target.definition.typeIdentity.air > 0.5f;
            WeaponStation station = BestStationFor(aircraft, isAir ? TargetClass.Air : TargetClass.Surface);
            if (station == null) return false;
            if (!ShotIsValid(aircraft, station, target)) return false;

            // Explicit attack orders may deliberately mass some fire, but still respect the
            // wing-wide cap. This keeps a four-ship from launching four missiles at a target
            // that only needed one or two.
            int capacity = RequiredAttackers(station, target);
            if (!TacticalCoordinator.TryClaim(
                    target, aircraft, capacity,
                    Mathf.Max(Plugin.Config2.FireInterval.Value * 1.5f, 3f)))
                return false;

            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            wm.TargetListChanged();

            pilot.SetPrimaryTarget(target);
            pilot.Fire();
            return true;
        }

        /// <summary>
        /// Shoot down inbound missiles using the game's own intercept target search.
        /// </summary>
        public static bool InterceptMissiles(Aircraft aircraft, Pilot pilot, Aircraft protectee)
        {
            if (protectee == null) protectee = aircraft;

            WeaponManager wm = aircraft.weaponManager;
            WeaponStation station = BestStationFor(aircraft, TargetClass.Missile);
            if (station == null) return false;

            // The intercept search anchors on a concrete inbound missile. Passing null used
            // to return zero targets immediately — CombatAI.LookForMissileTargets bails when
            // the anchor has no known position — so a wingman under a missile warning never
            // fired a single defensive shot. The anchor is the missile threatening whichever
            // aircraft the rules of engagement chose to defend (us or the leader).
            MissileWarning warning = protectee.GetMissileWarningSystem();
            if (warning == null || !warning.IsWarning())
                return false;

            Missile incoming = ChooseIncoming(warning, protectee, aircraft);
            if (incoming == null) return false;

            // One interceptor per inbound missile. If it cannot take the shot the claim
            // expires quickly and another wingman gets the next opportunity.
            if (!TacticalCoordinator.TryClaim(incoming, aircraft, 1, 3f)) return false;

            wm.currentWeaponStation = station;

            List<Unit> targets = wm.GetTargetList();
            targets.Clear();

            int found = CombatAI.LookForMissileTargets(aircraft, incoming, station, targets);
            wm.TargetListChanged();

            if (found <= 0) return false;

            pilot.Fire();
            return true;
        }

        // ------------------------------------------------------------------ selection

        private enum TargetClass { Air, Surface, Missile }

        private static Unit ChooseTarget(Aircraft aircraft, Allow allow, float maxRange,
                                         out WeaponStation station, out int capacity)
        {
            station = null;
            capacity = 1;

            bool wantAir = allow == Allow.AirOnly || allow == Allow.AirAndGround;
            bool wantGround = allow == Allow.GroundOnly || allow == Allow.AirAndGround;

            // Resolve the two candidate stations once. Doing this per unit meant walking
            // every weapon station for every unit on the map on every engagement tick.
            WeaponStation airStation = wantAir ? BestStationFor(aircraft, TargetClass.Air) : null;
            WeaponStation groundStation = wantGround ? BestStationFor(aircraft, TargetClass.Surface) : null;
            if (airStation == null && groundStation == null) return null;

            Unit best = null;
            float bestScore = 0f;
            WeaponStation bestStation = null;

            GlobalPosition from = aircraft.GlobalPosition();
            FactionHQ hq = aircraft.NetworkHQ;

            // Spatial query rather than a scan of every unit in the mission. This is the
            // same grid the game's own proximity checks use.
            scratch.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(from, maxRange, scratch);

            for (int i = 0; i < scratch.Count; i++)
            {
                Unit unit = scratch[i];
                if (unit == null || unit.disabled || unit == aircraft) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;   // friendly or neutral

                TypeIdentity id = unit.definition.typeIdentity;
                bool isAir = id.air > 0.5f;

                WeaponStation candidate = isAir ? airStation : groundStation;
                if (candidate == null) continue;

                // The game's own weapon/target matching, so a wingman does not try to take
                // a tank with an anti-air missile.
                float score = candidate.WeaponInfo.effectiveness.OpportunityAgainst(id);
                if (score <= 0f) continue;

                float distance = FastMath.Distance(unit.GlobalPosition(), from);
                float weaponRange = Mathf.Min(maxRange,
                    Mathf.Max(candidate.WeaponInfo.targetRequirements.maxRange, 1f));
                if (distance > weaponRange || distance < candidate.WeaponInfo.targetRequirements.minRange)
                    continue;

                int needed = RequiredAttackers(candidate, unit);
                int committed = TacticalCoordinator.CountClaims(unit, aircraft);
                if (committed >= needed) continue;

                // Effectiveness first, then range and reservation pressure. The old loop
                // used effectiveness alone, so equal contacts all resolved to whichever
                // BattlefieldGrid happened to enumerate first for every wingman.
                score *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(distance / weaponRange));
                score /= 1f + committed * Plugin.Config2.TargetSaturationPenalty.Value;
                if (score <= bestScore) continue;

                bestScore = score;
                best = unit;
                bestStation = candidate;
                capacity = needed;
            }

            scratch.Clear();
            station = bestStation;
            return best;
        }

        /// <summary>Closest-time unclaimed missile aimed at the protected aircraft.</summary>
        private static Missile ChooseIncoming(MissileWarning warning, Aircraft protectee,
                                              Aircraft interceptor)
        {
            Missile best = null;
            float bestTime = float.MaxValue;

            List<Missile> missiles = warning.knownMissiles;
            for (int i = 0; i < missiles.Count; i++)
            {
                Missile missile = missiles[i];
                if (missile == null || missile.disabled || missile.targetID != protectee.persistentID)
                    continue;
                if (TacticalCoordinator.CountClaims(missile, interceptor) > 0) continue;

                Vector3 toMissile = missile.GlobalPosition() - protectee.GlobalPosition();
                Vector3 relativeVelocity = missile.rb != null && protectee.rb != null
                    ? missile.rb.velocity - protectee.rb.velocity
                    : Vector3.zero;
                float closing = toMissile.sqrMagnitude > 1f
                    ? Mathf.Max(Vector3.Dot(-toMissile.normalized, relativeVelocity), 1f)
                    : 1f;
                float impactTime = toMissile.magnitude / closing;

                if (impactTime >= bestTime) continue;
                bestTime = impactTime;
                best = missile;
            }

            return best;
        }

        internal static int RequiredAttackers(WeaponStation station, Unit target)
        {
            if (station == null || station.WeaponInfo == null || target == null) return 1;
            if (target is Missile) return 1;

            int estimated = Mathf.CeilToInt(station.WeaponInfo.CalcAttacksNeeded(target));
            return Mathf.Clamp(estimated, 1, Plugin.Config2.MaxWingmenPerTarget.Value);
        }

        /// <summary>Estimated useful concurrent shooters from a particular aircraft.</summary>
        public static int RecommendedAttackers(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || target == null || target.definition == null) return 1;

            bool isAir = target.definition.typeIdentity.air > 0.5f;
            WeaponStation station = BestStationFor(
                aircraft, target is Missile ? TargetClass.Missile
                                            : (isAir ? TargetClass.Air : TargetClass.Surface));
            return RequiredAttackers(station, target);
        }

        /// <summary>Reused across calls so target search allocates nothing per tick.</summary>
        private static readonly List<Unit> scratch = new List<Unit>(64);

        /// <summary>Highest-effectiveness ready station for a target class.</summary>
        private static WeaponStation BestStationFor(Aircraft aircraft, TargetClass targetClass)
        {
            WeaponStation best = null;
            float bestValue = 0f;

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo) continue;
                if (station.WeaponInfo == null) continue;
                if (station.Ammo <= 0 || !station.Ready()) continue;

                RoleIdentity role = station.WeaponInfo.effectiveness;

                float value;
                switch (targetClass)
                {
                    case TargetClass.Air:     value = role.antiAir; break;
                    case TargetClass.Missile: value = role.antiMissile; break;
                    default:                  value = role.antiSurface; break;
                }

                if (value > bestValue)
                {
                    bestValue = value;
                    best = station;
                }
            }

            return bestValue > 0f ? best : null;
        }

        /// <summary>True when the aircraft carries anything able to engage missiles.</summary>
        public static bool HasMissileDefence(Aircraft aircraft)
        {
            return aircraft != null && BestStationFor(aircraft, TargetClass.Missile) != null;
        }

        /// <summary>
        /// Whether a hostile aircraft is close enough to be worth breaking formation for.
        /// Used only by the Aggressive posture.
        /// </summary>
        /// <summary>
        /// The enemy aircraft most threatening a given aircraft, or null.
        ///
        /// Used by the Cover Me order, which is the whole difference between it and plain
        /// formation flight: a covering wingman shoots at what is hunting the player, not
        /// at whatever happens to be nearest to itself.
        ///
        /// Aircraft behind the protectee score better than aircraft ahead of it at the same
        /// range, because that is where a gun or a heater comes from and it is the half of
        /// the sky the player can least see.
        /// </summary>
        public static Unit NearestThreatTo(Aircraft protectee, float range)
        {
            if (protectee == null) return null;

            float rangeSq = range * range;
            GlobalPosition from = protectee.GlobalPosition();
            Vector3 facing = protectee.transform.forward;
            FactionHQ hq = protectee.NetworkHQ;

            Unit best = null;
            float bestScore = float.MaxValue;

            List<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft other = all[i];
                if (other == null || other.disabled) continue;
                if (other.NetworkHQ == null || other.NetworkHQ == hq) continue;

                Vector3 toThreat = other.GlobalPosition() - from;
                float distanceSq = toThreat.sqrMagnitude;
                if (distanceSq > rangeSq) continue;

                // Halve the effective distance for anything in the rear hemisphere, so a
                // trailer is preferred over a head-on contact at the same range.
                float score = distanceSq;
                if (Vector3.Dot(toThreat, facing) < 0f) score *= 0.5f;

                if (score >= bestScore) continue;

                bestScore = score;
                best = other;
            }

            return best;
        }

    }
}
