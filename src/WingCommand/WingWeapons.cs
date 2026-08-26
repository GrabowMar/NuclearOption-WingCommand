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
                return InterceptMissiles(aircraft, pilot);

            Unit target = ChooseTarget(aircraft, allow, maxRange, out WeaponStation station);
            if (target == null || station == null) return false;

            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            wm.TargetListChanged();

            pilot.SetPrimaryTarget(target);
            pilot.Fire();
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
        public static bool InterceptMissiles(Aircraft aircraft, Pilot pilot)
        {
            WeaponManager wm = aircraft.weaponManager;
            WeaponStation station = BestStationFor(aircraft, TargetClass.Missile);
            if (station == null) return false;

            wm.currentWeaponStation = station;

            List<Unit> targets = wm.GetTargetList();
            targets.Clear();

            int found = CombatAI.LookForMissileTargets(aircraft, null, station, targets);
            wm.TargetListChanged();

            if (found <= 0) return false;

            pilot.Fire();
            return true;
        }

        // ------------------------------------------------------------------ selection

        private enum TargetClass { Air, Surface, Missile }

        private static Unit ChooseTarget(Aircraft aircraft, Allow allow, float maxRange,
                                         out WeaponStation station)
        {
            station = null;

            bool wantAir = allow == Allow.AirOnly || allow == Allow.AirAndGround;
            bool wantGround = allow == Allow.GroundOnly || allow == Allow.AirAndGround;

            Unit best = null;
            float bestScore = 0f;
            WeaponStation bestStation = null;

            float rangeSq = maxRange * maxRange;
            GlobalPosition from = aircraft.GlobalPosition();
            FactionHQ hq = aircraft.NetworkHQ;

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled) continue;
                if (unit == aircraft) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;   // friendly or neutral
                if (FastMath.SquareDistance(unit.GlobalPosition(), from) > rangeSq) continue;

                TypeIdentity id = unit.definition.typeIdentity;
                bool isAir = id.air > 0.5f;

                if (isAir && !wantAir) continue;
                if (!isAir && !wantGround) continue;

                WeaponStation candidate = BestStationFor(
                    aircraft, isAir ? TargetClass.Air : TargetClass.Surface);
                if (candidate == null) continue;

                // The game's own weapon/target matching, so a wingman does not try to take
                // a tank with an anti-air missile.
                float score = candidate.WeaponInfo.effectiveness.OpportunityAgainst(id);
                if (score <= bestScore) continue;

                bestScore = score;
                best = unit;
                bestStation = candidate;
            }

            station = bestStation;
            return best;
        }

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
        public static bool HasAirThreatWithin(Aircraft aircraft, float range)
        {
            if (aircraft == null) return false;

            float rangeSq = range * range;
            GlobalPosition from = aircraft.GlobalPosition();
            FactionHQ hq = aircraft.NetworkHQ;

            foreach (Aircraft other in UnitRegistry.allAircraft)
            {
                if (other == null || other.disabled || other == aircraft) continue;
                if (other.NetworkHQ == null || other.NetworkHQ == hq) continue;
                if (FastMath.SquareDistance(other.GlobalPosition(), from) <= rangeSq)
                    return true;
            }

            return false;
        }
    }
}
