using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
 /// <summary>Fires through the stock select-target-notify-fire sequence while leaving attitude and
 /// throttle to the flight controller.</summary>
    internal static class WingWeapons
    {
     /// <summary>Shared shot interval for formation, orbit, and attack runs, shortened by pilot
     /// experience.</summary>
        public static float FireInterval(Aircraft aircraft) =>
            WingTuning.FireInterval * WingPilotRoster.ReactionScale(aircraft);

     /// <summary>This member's weapon preference, or Auto if the aircraft is not commandable.</summary>
        private static WingWeaponPreference PreferenceOf(Aircraft aircraft)
        {
            WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
            return member != null ? member.WeaponPreference : WingWeaponPreference.Auto;
        }

     /// <summary>Permitted target classes.</summary>
        internal enum Allow
        {
            None,
            MissilesOnly,
            AirOnly,
            AirAndGround,
            GroundOnly,
        }

     /// <summary>Engage the highest-value allowed target; return true if fired.</summary>
        public static bool Engage(Aircraft aircraft, Pilot pilot, Allow allow, float maxRange)
        {
            if (aircraft == null || !aircraft.LocalSim || pilot == null || allow == Allow.None) return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            // Let an active salvo finish.
            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            if (allow == Allow.MissilesOnly)
                return InterceptMissiles(aircraft, pilot,
                    RoeRules.MissileDefenceProtectee(aircraft) ?? aircraft);

            Unit target = ChooseTarget(aircraft, allow, maxRange,
                                       out WeaponStation station, out int capacity);
            if (target == null || station == null) return false;
            if (!TacticalCoordinator.TryClaim(
                    target, aircraft, capacity,
                    Mathf.Max(FireInterval(aircraft) * 1.5f, 3f)))
                return false;

            FireStation(aircraft, pilot, target, wm, station);
            return true;
        }

     /// <summary>Checks the weapon's shot envelope. Callers enforce cadence separately to prevent
     /// firing on every engagement tick.</summary>
        private static bool ShotIsValid(Aircraft aircraft, WeaponStation station, Unit target)
        {
            WeaponInfo info = station.WeaponInfo;
            if (info == null) return false;

            TargetRequirements req = info.targetRequirements;

            // Experience may widen range and off-boresight tolerance; the scale never reduces the base
            // envelope.
            float envelope = WingPilotRoster.EnvelopeScale(aircraft);

            float distance = FastMath.Distance(target.GlobalPosition(), aircraft.GlobalPosition());
            if (req.maxRange > 0f && distance > req.maxRange * envelope) return false;
            if (distance < req.minRange) return false;

            if (req.minAltitude > 0f && aircraft.radarAlt < req.minAltitude) return false;
            if (req.maxAltitude > 0f && aircraft.radarAlt > req.maxAltitude * envelope) return false;

            // Bomb targets lie below the run-in boresight, so skip the missile seeker-cone check.
            if (info.bomb || info.glideBomb) return true;

            // minAlignment is the maximum permitted off-boresight angle.
            if (req.minAlignment > 0f)
            {
                Vector3 toTarget = target.GlobalPosition() - aircraft.GlobalPosition();
                if (Vector3.Angle(aircraft.transform.forward, toTarget) > req.minAlignment * envelope)
                    return false;
            }

            return true;
        }

     /// <summary>Engage the player's designated unit; return true if fired.</summary>
        public static bool EngageSpecific(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
            EngageDesignated(aircraft, pilot, target, maxRange, massed: false);

     /// <summary>Splash 'Em bypasses TacticalCoordinator's firing cap for massed fire. Stations must
     /// still match the target and pass their shot envelopes.</summary>
        public static bool EngageMassed(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
            EngageDesignated(aircraft, pilot, target, maxRange, massed: true);

        private static bool EngageDesignated(Aircraft aircraft, Pilot pilot, Unit target,
                                             float maxRange, bool massed)
        {
            if (aircraft == null || !aircraft.LocalSim || pilot == null || target == null || target.disabled)
                return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            if (FastMath.SquareDistance(target.GlobalPosition(), aircraft.GlobalPosition())
                > maxRange * maxRange)
                return false;

            // Splash interleaves effective stores; measured Attack retains the wing-wide firing cap.
            if (massed)
            {
                WeaponStation station = MassedStationFor(aircraft, target, current);
                if (station == null) return false;
                FireStation(aircraft, pilot, target, wm, station);
                return true;
            }

            WeaponStation chosen = DesignatedStationFor(aircraft, target);
            if (chosen == null) return false;

            int capacity = RequiredAttackers(chosen, target);
            if (!TacticalCoordinator.TryClaim(
                    target, aircraft, capacity,
                    Mathf.Max(FireInterval(aircraft) * 1.5f, 3f)))
                return false;

            FireStation(aircraft, pilot, target, wm, chosen);
            return true;
        }

     /// <summary>Select, target, notify, and fire an offensive station.</summary>
        private static void FireStation(Aircraft aircraft, Pilot pilot, Unit target,
                                        WeaponManager wm, WeaponStation station)
        {
            int guidedBefore = GetGuidedAmmo(aircraft);

            wm.currentWeaponStation = station;
            List<Unit> targets = wm.GetTargetList();
            targets.Clear();
            targets.Add(target);
            wm.TargetListChanged();

            pilot.SetPrimaryTarget(target);
            pilot.Fire();
            WingKillCredit.NoteShot(aircraft, target);

            if (station != null && station.WeaponInfo != null)
            {
                float dist = FastMath.Distance(aircraft.GlobalPosition(), target.GlobalPosition());
                float speed = Mathf.Max(station.WeaponInfo.muzzleVelocity, station.WeaponInfo.maxSpeed, 350f);
                WingDeliveryTracker.TrackShot(aircraft, target, station.WeaponInfo.shortName, dist / speed);
            }

            if (guidedBefore > 0 && GetGuidedAmmo(aircraft) == 0)
            {
                WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
                if (member != null && member.Ammo > 0)
                {
                    WingComms.Say(member, WingComms.Call.Expended);
                }
            }
        }

     /// <summary>Choose the most effective ready station that can fire now, preferring a different
     /// station from the last shot. Reuse the previous one only if no alternative can fire.</summary>
        private static WeaponStation MassedStationFor(Aircraft aircraft, Unit target,
                                                      WeaponStation justFired)
        {
            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            TargetClass targetClass = isAir ? TargetClass.Air : TargetClass.Surface;

            WeaponStation pick = BestStationFor(
                aircraft, targetClass, WingWeaponPreference.Auto, target, justFired);
            return pick ?? BestStationFor(aircraft, targetClass, WingWeaponPreference.Auto, target);
        }

     /// <summary>Choose a station for measured Attack using weapon preference. Splash uses
     /// MassedStationFor to cycle stores.</summary>
        private static WeaponStation DesignatedStationFor(Aircraft aircraft, Unit target)
        {
            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            TargetClass targetClass = isAir ? TargetClass.Air : TargetClass.Surface;
            return BestStationFor(aircraft, targetClass, PreferenceOf(aircraft), target);
        }

     /// <summary>Whether any remaining store can damage the target. Ignore temporary cooldown and range
     /// limits so Splash does not mistake a delayed shot for an empty loadout.</summary>
        public static bool CanStillEngage(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || target == null || target.disabled) return false;

            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            TargetClass targetClass = isAir ? TargetClass.Air : TargetClass.Surface;

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                if (station.Ammo <= 0) continue;

                RoleIdentity role = station.WeaponInfo.effectiveness;
                float value = targetClass == TargetClass.Air ? role.antiAir : role.antiSurface;
                if (value > 0f) return true;
            }

            return false;
        }

     /// <summary>Required height above a surface target to clear bomb release limits. Return zero for
     /// missile/gun-only loadouts to use the default attack height.</summary>
        public static float BombReleaseFloor(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || aircraft.weaponStations == null || target == null) return 0f;

            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            if (isAir) return 0f;

            float floor = 0f;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                if (station.Ammo <= 0) continue;
                WeaponInfo info = station.WeaponInfo;
                if (!info.bomb && !info.glideBomb) continue;
                if (info.effectiveness.antiSurface <= 0f) continue;
                float minAlt = info.targetRequirements.minAltitude;
                if (minAlt > floor) floor = minAlt;
            }

            return floor;
        }

     /// <summary>Whether a weapon station carries a jammer pod. Self-protection RadarJammer
     /// countermeasures do not qualify for the Jam order.</summary>
        public static bool HasJammer(Aircraft aircraft) => JammerStation(aircraft) != null;

     /// <summary>Fire the jammer pod at the designation. The native pod controls jamming range, power,
     /// and updates.</summary>
        public static bool EngageJammer(Aircraft aircraft, Pilot pilot, Unit target)
        {
            if (aircraft == null || pilot == null || target == null || target.disabled)
                return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            WeaponStation station = JammerStation(aircraft);
            if (station == null) return false;

            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
            wm.TargetListChanged();

            List<Weapon> weapons = station.Weapons;
            if (weapons != null)
            {
                for (int i = 0; i < weapons.Count; i++)
                {
                    Weapon weapon = weapons[i];
                    if (weapon != null) weapon.SetTarget(target);
                }
            }

            if (!station.Ready()) return false;

            pilot.SetPrimaryTarget(target);
            pilot.Fire();
            return true;
        }

        private static WeaponStation JammerStation(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return null;

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                if (!station.WeaponInfo.jammer) continue;
                if (station.Weapons == null || station.Weapons.Count == 0) continue;
                return station;
            }

            return null;
        }

     /// <summary>Release one cargo load through the native station. Ground release requires runtime
     /// confirmation: the caller checks ammunition changes and falls back to native transport if
     /// nothing drops.</summary>
        public static bool ReleaseCargo(Aircraft aircraft, Pilot pilot)
        {
            if (aircraft == null || pilot == null || aircraft.weaponStations == null) return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            WeaponStation cargo = null;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || !station.Cargo) continue;
                if (station.Ammo <= 0 || !station.Ready()) continue;
                cargo = station;
                break;
            }

            if (cargo == null) return false;

            wm.currentWeaponStation = cargo;
            wm.ClearTargetList();
            wm.TargetListChanged();
            pilot.Fire();
            return true;
        }

     /// <summary>Intercept inbound missiles through the native target search.</summary>
        public static bool InterceptMissiles(Aircraft aircraft, Pilot pilot, Aircraft protectee)
        {
            if (aircraft == null || !aircraft.LocalSim || pilot == null || aircraft.weaponManager == null ||
                aircraft.NetworkHQ == null) return false;
            if (protectee == null) protectee = aircraft;

            WeaponManager wm = aircraft.weaponManager;
            if (wm.currentWeaponStation != null && wm.currentWeaponStation.SalvoInProgress) return false;

            // Anchor the native search on a known inbound threatening the chosen protectee; a null
            // anchor returns no targets.
            MissileWarning warning = protectee.GetMissileWarningSystem();
            if (warning == null || !warning.IsWarning())
                return false;

            Missile incoming = ChooseIncoming(warning, protectee, aircraft, out WeaponStation station);
            if (incoming == null) return false;

            // Confirm a native search result before claiming, so failed searches cannot keep renewing a
            // reservation.
            interceptTargets.Clear();
            int found = CombatAI.LookForMissileTargets(aircraft, incoming, station, interceptTargets);
            if (found <= 0 || !interceptTargets.Contains(incoming) ||
                !TacticalCoordinator.TryClaim(incoming, aircraft, 1, 3f))
            {
                interceptTargets.Clear();
                return false;
            }

            wm.currentWeaponStation = station;
            List<Unit> targets = wm.GetTargetList();
            targets.Clear();
            // Fire only at this inbound; the native result can include aircraft and surface targets
            // forbidden under Hold.
            targets.Add(incoming);
            interceptTargets.Clear();
            wm.TargetListChanged();
            pilot.Fire();
            return true;
        }

        // Target selection.

        private enum TargetClass { Air, Surface, Missile }

        private static Unit ChooseTarget(Aircraft aircraft, Allow allow, float maxRange,
                                         out WeaponStation station, out int capacity)
        {
            station = null;
            capacity = 1;

            bool wantAir = allow == Allow.AirOnly || allow == Allow.AirAndGround;
            bool wantGround = allow == Allow.GroundOnly || allow == Allow.AirAndGround;

            // Bias target classes without excluding alternatives when the preferred class is
            // unavailable.
            WingWeaponPreference preference = PreferenceOf(aircraft);

            // Search other stations only when the preferred one cannot take this shot.
            WeaponStation airStation = wantAir
                ? BestStationFor(aircraft, TargetClass.Air, preference) : null;
            WeaponStation groundStation = wantGround
                ? BestStationFor(aircraft, TargetClass.Surface, preference) : null;
            if (airStation == null && groundStation == null) return null;

            Unit best = null;
            float bestScore = 0f;
            WeaponStation bestStation = null;

            GlobalPosition from = aircraft.GlobalPosition();
            FactionHQ hq = aircraft.NetworkHQ;

            // Use the native spatial grid to avoid scanning every mission unit.
            scratch.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(from, maxRange, scratch);

            for (int i = 0; i < scratch.Count; i++)
            {
                Unit unit = scratch[i];
                if (unit == null || unit.disabled || unit == aircraft || unit.definition == null) continue;
                if (unit.NetworkHQ == null || unit.NetworkHQ == hq) continue;   // Skip friendly and neutral units.

                TypeIdentity id = unit.definition.typeIdentity;
                bool isAir = id.air > 0.5f;

                WeaponStation candidate = isAir ? airStation : groundStation;
                if (candidate == null) continue;

                if (!ShotIsValid(aircraft, candidate, unit) ||
                    candidate.WeaponInfo.effectiveness.OpportunityAgainst(id) <= 0f)
                {
                    candidate = BestStationFor(aircraft,
                        isAir ? TargetClass.Air : TargetClass.Surface, preference, unit);
                    if (candidate == null) continue;
                }

                // Use native effectiveness to reject mismatched weapons and targets.
                float score = candidate.WeaponInfo.effectiveness.OpportunityAgainst(id);
                if (score <= 0f) continue;

                float distance = FastMath.Distance(unit.GlobalPosition(), from);
                if (distance > maxRange) continue;
                float weaponRange = Mathf.Min(maxRange,
                    Mathf.Max(candidate.WeaponInfo.targetRequirements.maxRange, 1f));
                int needed = RequiredAttackers(candidate, unit);
                if (TacticalCoordinator.CountClaims(unit, aircraft) >= needed) continue;
                int committed = TacticalCoordinator.CountCommitments(unit, aircraft);

                // Weight effectiveness by range and reservations so equal contacts do not all resolve
                // by grid enumeration order.
                score *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(distance / weaponRange));
                score /= 1f + committed * WingTuning.TargetSaturationPenalty;
                score *= ClassBias(preference, isAir);

                // Reserve guided munitions for valuable or threatening targets.
                if (candidate.WeaponInfo != null &&
                    (candidate.WeaponInfo.missile || candidate.WeaponInfo.laserGuided || candidate.WeaponInfo.glideBomb))
                {
                    if (candidate.Ammo <= 2)
                    {
                        bool highThreat = isAir ||
                                          (unit.definition != null && unit.definition.typeIdentity.radar > 0.25f) ||
                                          unit is Ship;
                        if (!highThreat)
                        {
                            score *= 0.25f;
                        }
                    }
                }

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

     /// <summary>Soft target-class preference. Both weights remain positive, so preference reorders
     /// candidates without excluding them.</summary>
        private static float ClassBias(WingWeaponPreference preference, bool isAir)
        {
            switch (preference)
            {
                case WingWeaponPreference.AirToAir:    return isAir ? 1.75f : 0.6f;
                case WingWeaponPreference.AirToGround: return isAir ? 0.6f : 1.75f;
                default:                               return 1f;
            }
        }

     /// <summary>Choose the unclaimed tracked inbound with the shortest time to impact that a ready
     /// station can engage.</summary>
        private static Missile ChooseIncoming(MissileWarning warning, Aircraft protectee,
                                              Aircraft interceptor, out WeaponStation station)
        {
            Missile best = null;
            float bestTime = float.MaxValue;
            station = null;

            List<Missile> missiles = warning.knownMissiles;
            for (int i = 0; i < missiles.Count; i++)
            {
                Missile missile = missiles[i];
                if (missile == null || missile.disabled || missile.targetID != protectee.persistentID)
                    continue;
                if (TacticalCoordinator.CountClaims(missile, interceptor) > 0) continue;
                if (!interceptor.NetworkHQ.TryGetKnownPosition(missile, out _)) continue;

                WeaponStation candidate = BestStationFor(
                    interceptor, TargetClass.Missile, WingWeaponPreference.Auto, missile);
                if (candidate == null) continue;

                Vector3 toMissile = missile.GlobalPosition() - protectee.GlobalPosition();
                Vector3 relativeVelocity = missile.rb != null && protectee.rb != null
                    ? missile.rb.velocity - protectee.rb.velocity
                    : Vector3.zero;
                float dist = toMissile.magnitude;
                float closing = dist > 1f
                    ? Mathf.Max(Vector3.Dot(-toMissile / dist, relativeVelocity), 1f)
                    : 1f;
                float impactTime = dist / closing;

                if (impactTime >= bestTime) continue;
                bestTime = impactTime;
                best = missile;
                station = candidate;
            }

            return best;
        }

     /// <summary>Remaining missiles, laser-guided bombs, and glide bombs.</summary>
        public static int GetGuidedAmmo(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return 0;
            int count = 0;
            for (int i = 0; i < aircraft.weaponStations.Count; i++)
            {
                WeaponStation st = aircraft.weaponStations[i];
                if (st == null || st.Cargo || st.WeaponInfo == null || st.Ammo <= 0) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info.missile || info.laserGuided || info.glideBomb)
                    count += st.Ammo;
            }
            return count;
        }

        internal static int RequiredAttackers(WeaponStation station, Unit target)
        {
            if (station == null || station.WeaponInfo == null || target == null) return 1;
            if (target is Missile) return 1;

            int estimated = Mathf.CeilToInt(station.WeaponInfo.CalcAttacksNeeded(target));
            int maxWingmen = Plugin.Settings != null ? Plugin.Settings.MaxWingmenPerTarget.Value : WingTuning.MaxWingmenPerTarget;
            return Mathf.Clamp(estimated, 1, maxWingmen);
        }

     /// <summary>Estimate useful simultaneous shooters for this aircraft and target.</summary>
        public static int RecommendedAttackers(Aircraft aircraft, Unit target)
        {
            if (aircraft == null || target == null || target.definition == null) return 1;

            bool isAir = target.definition.typeIdentity.air > 0.5f;
            WeaponStation station = BestStationFor(
                aircraft, target is Missile ? TargetClass.Missile
                                            : (isAir ? TargetClass.Air : TargetClass.Surface));
            return RequiredAttackers(station, target);
        }

     /// <summary>Reuse target-search storage to avoid per-tick allocation.</summary>
        private static readonly List<Unit> scratch = new List<Unit>(64);
        private static readonly List<Unit> interceptTargets = new List<Unit>();

     /// <summary>Rank ready stations by effectiveness and preference. For a specific target, reject
     /// invalid shots first so an out-of-envelope preferred weapon cannot hide one that can
     /// fire.</summary>
        private static WeaponStation BestStationFor(Aircraft aircraft, TargetClass targetClass) =>
            BestStationFor(aircraft, targetClass, PreferenceOf(aircraft));

        private static WeaponStation BestStationFor(Aircraft aircraft, TargetClass targetClass,
                                                    WingWeaponPreference preference,
                                                    Unit target = null,
                                                    WeaponStation exclude = null)
        {
            WeaponStation best = null;
            float bestScore = 0f;
            if (aircraft == null || aircraft.weaponStations == null) return null;

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo) continue;
                if (station.WeaponInfo == null) continue;
                if (station.Ammo <= 0 || !station.Ready()) continue;
                if (station.SafetyIsOn(aircraft)) continue;
                if (station.WeaponInfo.energy && (aircraft.GetPowerSupply()?.GetCharge() ?? 0f) < 0.6f)
                    continue;
                if (station == exclude) continue;
                if (target != null && !ShotIsValid(aircraft, station, target)) continue;

                RoleIdentity role = station.WeaponInfo.effectiveness;

                float value;
                switch (targetClass)
                {
                    case TargetClass.Air:     value = role.antiAir; break;
                    case TargetClass.Missile: value = role.antiMissile; break;
                    default:                  value = role.antiSurface; break;
                }

                if (value <= 0f) continue;
                if (target != null && target.definition != null &&
                    role.OpportunityAgainst(target.definition.typeIdentity) <= 0f) continue;

                float score = value * StationBias(preference, station, targetClass);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = station;
                }
            }

            return best;
        }

     /// <summary>Weight valid stations by preference, excluding missile defence. Close-in preference
     /// uses weapon range rather than weapon names.</summary>
        private static float StationBias(WingWeaponPreference preference, WeaponStation station,
                                         TargetClass targetClass)
        {
            if (preference != WingWeaponPreference.ShortRange ||
                targetClass == TargetClass.Missile)
                return 1f;

            float reach = Mathf.Max(station.WeaponInfo.targetRequirements.maxRange, 1f);

            // Short-range stores get up to 2x weight, tapering to 1x at 10 km; effectiveness still
            // governs large differences.
            return Mathf.Lerp(2f, 1f, Mathf.Clamp01(reach / 10000f));
        }

     /// <summary>Whether any carried weapon can engage missiles.</summary>
        public static bool HasMissileDefence(Aircraft aircraft)
        {
            return aircraft != null && BestStationFor(aircraft, TargetClass.Missile) != null;
        }

     /// <summary>Choose the most threatening enemy aircraft, or null. Rear-hemisphere contacts receive
     /// a distance advantage to favour threats behind the protectee.</summary>
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

                // Halve effective distance for rear-hemisphere contacts.
                float score = distanceSq;
                if (Vector3.Dot(toThreat, facing) < 0f) score *= 0.5f;

                if (score >= bestScore) continue;

                bestScore = score;
                best = other;
            }

            return best;
        }

     /// <summary>Choose the nearest live enemy within radius of near that remaining stores can damage.
     /// Different HQs are hostile. Return null when no usable target or ordnance remains.</summary>
        public static Unit NextExpendTarget(Aircraft aircraft, GlobalPosition near,
                                            float radius, Unit exclude)
        {
            if (aircraft == null) return null;

            FactionHQ hq = aircraft.NetworkHQ;
            float radiusSq = radius * radius;

            Unit best = null;
            float bestSq = float.MaxValue;

            List<Unit> all = UnitRegistry.allUnits;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || u.disabled || ReferenceEquals(u, exclude) ||
                    ReferenceEquals(u, aircraft))
                    continue;
                // No HQ means neutral; matching HQ means friendly.
                if (u.NetworkHQ == null || u.NetworkHQ == hq) continue;

                float d = (u.GlobalPosition() - near).sqrMagnitude;
                if (d > radiusSq || d >= bestSq) continue;
                if (!CanStillEngage(aircraft, u)) continue;

                bestSq = d;
                best = u;
            }

            return best;
        }
    }
}
