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
        /// <summary>
        /// How long this aircraft waits between shots.
        ///
        /// The configured interval, shortened slightly for an experienced pilot. This is
        /// the "reaction" half of the rank effect and the only place the cadence is read,
        /// so every firing path — formation, orbit and attack run — inherits it.
        /// </summary>
        public static float FireInterval(Aircraft aircraft) =>
            WingTuning.FireInterval * WingPilotRoster.ReactionScale(aircraft);

        /// <summary>
        /// The weapon preference standing for this aircraft, or Auto when it is not a
        /// commandable member of the player's wing.
        /// </summary>
        private static WingWeaponPreference PreferenceOf(Aircraft aircraft)
        {
            WingMember member = WingCommandManager.Instance?.Wing?.Find(aircraft);
            return member != null ? member.WeaponPreference : WingWeaponPreference.Auto;
        }

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
                    Mathf.Max(FireInterval(aircraft) * 1.5f, 3f)))
                return false;

            int guidedBefore = GetGuidedAmmo(aircraft);
            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
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

            // An experienced pilot gets slightly more out of the same weapon: a little more
            // reach and a little more off-boresight tolerance. The scale is never below one,
            // so rank can only ever widen this envelope — a low-ranked pilot shoots exactly
            // as the mod always made them shoot.
            float envelope = WingPilotRoster.EnvelopeScale(aircraft);

            float distance = FastMath.Distance(target.GlobalPosition(), aircraft.GlobalPosition());
            if (req.maxRange > 0f && distance > req.maxRange * envelope) return false;
            if (distance < req.minRange) return false;

            if (req.minAltitude > 0f && aircraft.radarAlt < req.minAltitude) return false;
            if (req.maxAltitude > 0f && aircraft.radarAlt > req.maxAltitude * envelope) return false;

            // Bombs and glide bombs are pickled, not aimed like a missile. The nose check
            // is a seeker cone: a bomber on a run-in is looking at a point above the
            // target, so the target sits below the boresight and every drop was refused.
            if (info.bomb || info.glideBomb) return true;

            // Alignment: the target has to be somewhere near the nose. minAlignment is the
            // widest off-boresight angle the weapon accepts.
            if (req.minAlignment > 0f)
            {
                Vector3 toTarget = target.GlobalPosition() - aircraft.GlobalPosition();
                if (Vector3.Angle(aircraft.transform.forward, toTarget) > req.minAlignment * envelope)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Fire at one specific unit, chosen by the player rather than by the wingman.
        /// Returns true if it fired.
        /// </summary>
        public static bool EngageSpecific(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
            EngageDesignated(aircraft, pilot, target, maxRange, massed: false);

        /// <summary>
        /// Put everything that can hurt this target into it, without the wing-wide
        /// concurrency cap.
        ///
        /// This is the Splash 'Em order's shooting, and the only place in the mod that
        /// deliberately skips <see cref="TacticalCoordinator"/>. Massed fire on one
        /// designation is the entire point of the order, so the reservation that normally
        /// stops a four-ship spending four missiles on a two-missile target is exactly what
        /// has to be suspended — the player has asked for that.
        ///
        /// What is <em>not</em> suspended is the weapon/target matching. A station still has
        /// to be effective against this class of target and the shot still has to be inside
        /// the weapon's own stated envelope, so a wingman works down through its missiles,
        /// then its rockets, then its gun as each runs dry, rather than throwing anti-air
        /// missiles at a tank. "Everything it has" means everything that can do the job.
        /// </summary>
        public static bool EngageMassed(Aircraft aircraft, Pilot pilot, Unit target, float maxRange) =>
            EngageDesignated(aircraft, pilot, target, maxRange, massed: true);

        private static bool EngageDesignated(Aircraft aircraft, Pilot pilot, Unit target,
                                             float maxRange, bool massed)
        {
            if (aircraft == null || pilot == null || target == null || target.disabled) return false;

            WeaponManager wm = aircraft.weaponManager;
            if (wm == null) return false;

            WeaponStation current = wm.currentWeaponStation;
            if (current != null && current.SalvoInProgress) return false;

            if (FastMath.SquareDistance(target.GlobalPosition(), aircraft.GlobalPosition())
                > maxRange * maxRange)
                return false;

            // Splash 'Em walks every effective store at the target, interleaved. A measured
            // Attack order still massed some fire but respects the wing-wide cap, so a
            // four-ship does not spend four missiles on a target that needed one or two.
            if (massed)
            {
                WeaponStation station = MassedStationFor(aircraft, target, current);
                if (station == null) return false;
                FireStation(aircraft, pilot, target, wm, station);
                return true;
            }

            WeaponStation chosen = DesignatedStationFor(aircraft, target);
            if (chosen == null) return false;
            if (!ShotIsValid(aircraft, chosen, target)) return false;

            int capacity = RequiredAttackers(chosen, target);
            if (!TacticalCoordinator.TryClaim(
                    target, aircraft, capacity,
                    Mathf.Max(FireInterval(aircraft) * 1.5f, 3f)))
                return false;

            FireStation(aircraft, pilot, target, wm, chosen);
            return true;
        }

        /// <summary>The shared select-and-fire sequence used by every designated shot.</summary>
        private static void FireStation(Aircraft aircraft, Pilot pilot, Unit target,
                                        WeaponManager wm, WeaponStation station)
        {
            int guidedBefore = GetGuidedAmmo(aircraft);

            wm.currentWeaponStation = station;
            wm.ClearTargetList();
            wm.AddTargetList(target);
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

        /// <summary>
        /// The station a Splash 'Em run fires next: the most effective ready store that can
        /// actually take the shot right now, rotated past the one just fired so a volley
        /// interleaves missiles, rockets and gun instead of emptying one store before the
        /// next is touched. The store just fired is only reused when nothing else can fire.
        /// </summary>
        private static WeaponStation MassedStationFor(Aircraft aircraft, Unit target,
                                                      WeaponStation justFired)
        {
            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            TargetClass targetClass = isAir ? TargetClass.Air : TargetClass.Surface;

            WeaponStation pick = BestMassedStation(aircraft, target, targetClass, justFired);
            return pick ?? BestMassedStation(aircraft, target, targetClass, null);
        }

        /// <summary>
        /// Highest-effectiveness ready station that can hit <paramref name="target"/> now,
        /// optionally skipping <paramref name="exclude"/> to rotate the loadout. Unlike
        /// <see cref="BestStationFor"/>, this enforces <see cref="ShotIsValid"/> inline so a
        /// rotated-to store that is out of its own envelope is passed over rather than fired
        /// at, and it applies no weapon preference — Splash 'Em expends what is there.
        /// </summary>
        private static WeaponStation BestMassedStation(Aircraft aircraft, Unit target,
                                                       TargetClass targetClass,
                                                       WeaponStation exclude)
        {
            WeaponStation best = null;
            float bestScore = 0f;

            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station == null || station.Cargo || station.WeaponInfo == null) continue;
                if (station.Ammo <= 0 || !station.Ready()) continue;
                if (station == exclude) continue;
                if (!ShotIsValid(aircraft, station, target)) continue;

                RoleIdentity role = station.WeaponInfo.effectiveness;
                float value = targetClass == TargetClass.Air ? role.antiAir : role.antiSurface;
                if (value <= 0f) continue;

                if (value > bestScore)
                {
                    bestScore = value;
                    best = station;
                }
            }

            return best;
        }

        /// <summary>
        /// The station a measured Attack order uses against a designated unit. The player's
        /// weapon preference still weighs in here; Splash 'Em, which expends everything, is
        /// served by <see cref="MassedStationFor"/> instead.
        /// </summary>
        private static WeaponStation DesignatedStationFor(Aircraft aircraft, Unit target)
        {
            bool isAir = target.definition != null && target.definition.typeIdentity.air > 0.5f;
            TargetClass targetClass = isAir ? TargetClass.Air : TargetClass.Surface;
            return BestStationFor(aircraft, targetClass);
        }

        /// <summary>
        /// Whether this aircraft still carries anything effective against a target.
        ///
        /// Read by the Splash 'Em run to know when it has genuinely finished, rather than
        /// circling a survivor with nothing left that can touch it. It ignores a station's
        /// cooldown and range: those only pause a shot for a moment, and the run must wait
        /// for them rather than read them as "out of ammunition" and go home early.
        /// </summary>
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

        /// <summary>
        /// Height a bomber wants above a surface target so its bombs clear their own
        /// min-altitude gate. Fighters with only missiles or guns return zero and keep the
        /// attack-run default.
        /// </summary>
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

        /// <summary>
        /// True when this airframe is carrying a jammer <i>weapon</i> — a pod whose
        /// <c>WeaponInfo.jammer</c> flag is set, typically the stock <c>JammingPod</c>.
        ///
        /// Self-protection ECM (<c>RadarJammer</c>, a countermeasure) is a different thing
        /// and does not count. Almost every combat aircraft has one, and using it as the
        /// gate made the Jam order available to the whole wing, then applied <c>Unit.Jam</c>
        /// as a cheat that never touched a pod.
        /// </summary>
        public static bool HasJammer(Aircraft aircraft) => JammerStation(aircraft) != null;

        /// <summary>
        /// Run the aircraft's jammer pod against a designated unit, using the same
        /// select-and-fire sequence every other shot in this file uses.
        ///
        /// The stock <c>JammingPod</c> is what actually blinds the target: <c>SetTarget</c>
        /// plus <c>Fire</c> lets its own range falloff, power and <c>FixedUpdate</c> tick
        /// drive <c>Unit.Jam</c>. Calling <c>Unit.Jam</c> from here with a hardcoded amount
        /// was how every wingman jammed at any range without carrying a pod.
        /// </summary>
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

        /// <summary>
        /// Let go of one cargo load, using the same station-select-and-fire sequence every
        /// other shot in this file uses.
        ///
        /// There is no target list: a cargo station drops what it is carrying where the
        /// aircraft is. Whether the stock station answers <c>Fire</c> on the ground is not
        /// something a plugin build can prove, so the caller watches the station's own
        /// ammunition for the answer and falls back to the stock transport behaviour if
        /// nothing moves.
        /// </summary>
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

            // The preference biases which kind of contact is worth breaking off for. It
            // deliberately does not clear wantAir/wantGround: a wingman told to prefer
            // air-to-air still shoots the tank in front of it rather than nothing.
            WingWeaponPreference preference = PreferenceOf(aircraft);

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
                score /= 1f + committed * WingTuning.TargetSaturationPenalty;
                score *= ClassBias(preference, isAir);

                // Preserve scarce guided munitions for high-threat / high-value targets
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

        /// <summary>
        /// How much the player's weapon preference favours this class of contact.
        ///
        /// A multiplier rather than a filter, and a mild one: the preferred class has to be
        /// clearly worse before the other is chosen, but it can still be chosen. The damped
        /// side is never zero, so a preference can reorder targets and can never empty the
        /// list.
        /// </summary>
        private static float ClassBias(WingWeaponPreference preference, bool isAir)
        {
            switch (preference)
            {
                case WingWeaponPreference.AirToAir:    return isAir ? 1.75f : 0.6f;
                case WingWeaponPreference.AirToGround: return isAir ? 0.6f : 1.75f;
                default:                               return 1f;
            }
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
                float dist = toMissile.magnitude;
                float closing = dist > 1f
                    ? Mathf.Max(Vector3.Dot(-toMissile / dist, relativeVelocity), 1f)
                    : 1f;
                float impactTime = dist / closing;

                if (impactTime >= bestTime) continue;
                bestTime = impactTime;
                best = missile;
            }

            return best;
        }

        /// <summary>Total remaining guided munitions (missiles, laser-guided bombs, glide bombs).</summary>
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
            return Mathf.Clamp(estimated, 1, WingTuning.MaxWingmenPerTarget);
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

        /// <summary>
        /// The ready station this aircraft should use against a class of target.
        ///
        /// Effectiveness still decides, as it always has. The player's preference only
        /// reweights stations that are already valid for this target class, so the choice
        /// stays inside the same set the stock ranking would have picked from — and an
        /// aircraft whose preferred stores are empty, unready or absent simply gets the
        /// most effective station it has, exactly as before.
        /// </summary>
        private static WeaponStation BestStationFor(Aircraft aircraft, TargetClass targetClass) =>
            BestStationFor(aircraft, targetClass, PreferenceOf(aircraft));

        private static WeaponStation BestStationFor(Aircraft aircraft, TargetClass targetClass,
                                                    WingWeaponPreference preference)
        {
            WeaponStation best = null;
            float bestScore = 0f;

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

                if (value <= 0f) continue;

                float score = value * StationBias(preference, station, targetClass);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = station;
                }
            }

            return best;
        }

        /// <summary>
        /// The preference's weighting of one already-valid station.
        ///
        /// Missile defence is deliberately excluded: shooting down an inbound missile is
        /// the most time-critical thing a wingman does, and there is no sense in which the
        /// player wanting the gun used on trucks should change which interceptor it picks.
        ///
        /// The close-in weighting reads reach from the weapon's own stated maximum range,
        /// so "the gun end of the loadout" needs no list of weapon names to identify: on
        /// every airframe it is simply the shortest-ranged store that can take the shot.
        /// </summary>
        private static float StationBias(WingWeaponPreference preference, WeaponStation station,
                                         TargetClass targetClass)
        {
            if (preference != WingWeaponPreference.ShortRange ||
                targetClass == TargetClass.Missile)
                return 1f;

            float reach = Mathf.Max(station.WeaponInfo.targetRequirements.maxRange, 1f);

            // 2x at gun range, tapering to no advantage by the time a store reaches out
            // past ten kilometres. A short-ranged store therefore wins a close contest but
            // never displaces a weapon that is several times more effective.
            return Mathf.Lerp(2f, 1f, Mathf.Clamp01(reach / 10000f));
        }

        /// <summary>True when the aircraft carries anything able to engage missiles.</summary>
        public static bool HasMissileDefence(Aircraft aircraft)
        {
            return aircraft != null && BestStationFor(aircraft, TargetClass.Missile) != null;
        }

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
