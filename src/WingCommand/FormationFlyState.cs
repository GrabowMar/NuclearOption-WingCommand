using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// A pilot state that flies a formation slot on a leader aircraft.
    ///
    /// This subclasses the game's own <see cref="PilotBaseState"/> and is installed with
    /// <c>Pilot.SwitchState</c>, exactly as the stock AI states are — no patching of the
    /// state machine is involved. It steers through <c>Autopilot.AutoAim</c>, the same
    /// primitive <see cref="AIPilotCombatModes"/> uses, and owns only throttle and
    /// destination.
    /// </summary>
    internal class FormationFlyState : PilotBaseState
    {
        private readonly WingMember member;

        private const float EngageInterval = 0.5f;

        // Avoidance geometry, all expressed as multiples of the slot spacing in use so a
        // change to spacing moves them together. These were config entries; none of them
        // ever needed tuning independently, and leaving them free is how one of them came
        // to sit wider than a whole rotary formation.

        /// <summary>Separation radius: just inside the gap between neighbouring slots.</summary>
        private const float SeparationSpacings = 0.75f;

        /// <summary>Repulsion strength at that radius, in metres of slot displacement.</summary>
        private const float SeparationStrength = 12f;

        /// <summary>Length of the protected corridor ahead of the leader.</summary>
        private const float PathCutSpacings = 3.3f;

        /// <summary>Half-width of that corridor.</summary>
        private const float PathCutRadiusSpacings = 1f;

        /// <summary>How hard a wingman is pushed out of the leader's path.</summary>
        private const float PathCutStrengthSpacings = 1.7f;

        /// <summary>Seconds over which avoidance eases in, so it never steps the target.</summary>
        private const float AvoidanceSmoothing = 0.4f;

        /// <summary>Slot error, in metres, that counts as failing to hold station.</summary>
        private const float UnableDistance = 3000f;

        /// <summary>How long it must keep losing ground before a wingman gives up.</summary>
        private const float UnableSeconds = 20f;

        private float lastSupportCheck;
        private float lastEngageCheck;
        private float lastFiredTime;
        private float rejoinBoostUntil;
        private float rejoinHoldUntil;
        private float lastKeepUpDistance = float.MaxValue;
        private float losingGroundSince;
        private Vector3 smoothedAvoidance;
        private float threatSpacing = 1f;

        /// <summary>Seconds of leader motion fed into the slot position, so the slot is where the leader will be, not where it was.</summary>
        private const float SlotPredictionSeconds = 1f;
        private RotaryFormation.Mode lastRotaryMode = (RotaryFormation.Mode)(-1);
        private float lastRotaryReport;

        public FormationFlyState(WingMember member)
        {
            this.member = member;
            stateDisplayName = "Formation";
        }

        public Aircraft Leader => member.Leader;

        /// <summary>
        /// Leader track, low-pass filtered. Wingmen steer against this rather than the
        /// leader's instantaneous velocity, so the formation follows where the leader is
        /// going instead of reproducing every twitch of the stick.
        /// </summary>
        private Vector3 smoothedLeaderDir;

        /// <summary>
        /// Seconds of smoothing. Long enough to reject stick noise, short enough not to lag a
        /// turn. Tightened to half a second: the track still filters the leader's every
        /// twitch, but a genuine manoeuvre is now followed rather than trailed.
        /// </summary>
        private const float LeaderTrackSmoothing = 0.5f;

        /// <summary>
        /// One report every five seconds, per wingman. The timer lives here rather than in
        /// the flight model because the model is static and shared: a single static timer
        /// meant three wingmen took turns logging, so consecutive lines described different
        /// aircraft and the numbers looked like one aircraft thrashing.
        /// </summary>
        private bool DueToReport()
        {
            if (!Plugin.Config2.VerboseLogging.Value) return false;
            if (Time.timeSinceLevelLoad - lastReport < 5f) return false;

            lastReport = Time.timeSinceLevelLoad;
            return true;
        }

        private float lastReport;

        private Vector3 TrackLeader(Aircraft leader)
        {
            Vector3 instant = leader.rb != null && leader.rb.velocity.sqrMagnitude > 1f
                ? leader.rb.velocity.normalized
                : leader.transform.forward;

            if (smoothedLeaderDir.sqrMagnitude < 0.5f)
            {
                smoothedLeaderDir = instant;
                return smoothedLeaderDir;
            }

            smoothedLeaderDir = Vector3.Slerp(
                smoothedLeaderDir, instant,
                1f - Mathf.Exp(-Time.fixedDeltaTime / LeaderTrackSmoothing)).normalized;

            return smoothedLeaderDir;
        }


        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            controlInputs = aircraft.GetInputs();

            aircraft.SetFlightAssist(enabled: true);
            // Retract the gear whenever it is not already up. A freshly spawned helicopter
            // can still be Uninitialized here (a frame after spawn), and skipping that case
            // is what left it sitting with the gear hanging out.
            if (aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            pilot.flightInfo.HasTakenOff = true;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Formation] {aircraft.unitName} entering slot {member.Slot}");
        }

        public override void LeaveState()
        {
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            Aircraft leader = Leader;

            // Leader gone, or we are no longer flyable: hand back to the stock AI.
            if (leader == null || leader.disabled || aircraft == null || aircraft.disabled)
            {
                member.ReleaseToCombat("leader lost");
                return;
            }

            if (CheckMutualSupport(leader))
                return;

            RunEngagement(leader);

            FormationShape shape = Plugin.Config2.Shape.Value;

            // Helicopters fly slower and much closer together than jets, so the same
            // spacing that reads as tight for a fighter formation looks scattered for them.
            float spacing = Plugin.Config2.SlotSpacing.Value;
            if (WingRegistry.IsRotary(aircraft))
                spacing *= Plugin.Config2.RotarySpacingScale.Value;

            spacing *= ThreatSpacingScale(leader);

            Vector3 offset = FormationSolver.SlotOffset(
                leader.transform.forward, member.Slot, shape, spacing,
                Plugin.Config2.SlotStack.Value);

            // Anchor the slot to where the leader is going, not where it is: a fast leader
            // drags an un-predicted slot behind it and the wingman spends the whole flight
            // chasing a moving target it can never sit on. Predicting the leader's own motion
            // removes that lag so station-keeping converges instead of perpetually trailing.
            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            GlobalPosition slotPos = leader.GlobalPosition()
                                     + leaderVel * SlotPredictionSeconds
                                     + offset;

            // Separation keeps wingmen out of each other during a rejoin, and path-cut
            // avoidance keeps them out of the leader's nose.
            //
            // Every distance here is derived from the spacing actually in use rather than
            // configured separately. That is not only tidier: a fixed separation radius sat
            // wider than a rotary formation's own slots, so helicopters repelled each other
            // permanently and the formation could never settle. Deriving them means a
            // setting like RotarySpacingScale moves the whole geometry together and cannot
            // leave one threshold contradicting another.
            Vector3 avoidance =
                FormationSolver.Separation(
                    aircraft, member.Siblings,
                    radius: spacing * SeparationSpacings,
                    strength: SeparationStrength) +
                FormationSolver.AvoidLeaderPath(
                    aircraft, leader,
                    lookAhead: spacing * PathCutSpacings,
                    corridorRadius: spacing * PathCutRadiusSpacings,
                    strength: spacing * PathCutStrengthSpacings);

            // Both switch on and off as distances cross thresholds, so applied raw they
            // step the destination and the autopilot chases the step. Easing them in makes
            // the target something an aircraft can actually track.
            smoothedAvoidance = Vector3.Lerp(
                smoothedAvoidance, avoidance,
                1f - Mathf.Exp(-Time.fixedDeltaTime / AvoidanceSmoothing));

            slotPos += smoothedAvoidance;

            Vector3 toSlot = slotPos - aircraft.GlobalPosition();
            float distance = toSlot.magnitude;

            member.SlotError = distance;
            CheckAbleToKeepUp(leader, distance);

            // Two flight models, chosen by autopilot type, each in its own file. They are
            // separate because AutopilotPlane and AutopilotHelo override different AutoAim
            // overloads and answer to completely different commands — calling the wrong one
            // produces no control input at all. Everything above this point (slot geometry,
            // avoidance, diagnostics) is shared; everything below is not.
            if (aircraft.autopilot is AutopilotPlane)
            {
                FixedWingFormation.Fly(
                    aircraft, leader, controlInputs, member.Slot,
                    slotPos, toSlot, distance, spacing,
                    new FixedWingFormation.Rejoin(rejoinHoldUntil, rejoinBoostUntil),
                    TrackLeader(leader), DueToReport());
            }
            else
            {
                RotaryFormation.Mode mode = RotaryFormation.Fly(
                    aircraft, leader, slotPos, toSlot, distance, offset.y, spacing,
                    lastRotaryMode, out float horizontalError);

                ReportRotaryMode(mode, distance, horizontalError);
            }
        }
        /// <summary>
        /// Log rotary regime changes and periodic slot error.
        ///
        /// Four attempts at helicopter formation failed partly because the only evidence
        /// available was a description of how it looked. This says which control path is
        /// running and how far out the aircraft actually is, so the next diagnosis starts
        /// from data.
        /// </summary>
        private void ReportRotaryMode(RotaryFormation.Mode mode, float distance, float horizontalError)
        {
            if (!Plugin.Config2.VerboseLogging.Value) return;

            bool changed = mode != lastRotaryMode;
            bool due = Time.timeSinceLevelLoad - lastRotaryReport > 5f;
            if (!changed && !due) return;

            lastRotaryMode = mode;
            lastRotaryReport = Time.timeSinceLevelLoad;

            Aircraft leader = Leader;
            Plugin.Logger.LogInfo(
                $"[Rotary] {aircraft.unitName} slot {member.Slot}: {mode}, " +
                $"error {distance:F0} m (flat {horizontalError:F0}), " +
                $"own speed {aircraft.speed:F0}, " +
                $"leader {(leader != null ? leader.speed : 0f):F0} m/s, " +
                $"alt {aircraft.radarAlt:F0} m");
        }

        /// <summary>
        /// Open the formation up under threat and close it again when clear.
        ///
        /// Real formations widen when they expect to fight — a tight parade formation is
        /// easy to shoot at and leaves nobody room to manoeuvre. Eased rather than stepped,
        /// because a sudden change in spacing moves every slot at once and the autopilot
        /// would chase the jump.
        /// </summary>
        private float ThreatSpacingScale(Aircraft leader)
        {
            // One setting now, not a bool plus a scale that could disagree: a value of 1
            // is the off switch.
            float scale = Plugin.Config2.ThreatSpacingScale.Value;
            float target = 1f;

            if (scale > 1.001f)
            {
                bool threatened =
                    RoeRules.Current == WingRoe.Free;

                if (!threatened)
                {
                    MissileWarning warning = leader.GetMissileWarningSystem();
                    threatened = warning != null && warning.IsWarning();
                }

                if (threatened) target = scale;
            }

            threatSpacing = Mathf.Lerp(
                threatSpacing <= 0f ? 1f : threatSpacing, target,
                1f - Mathf.Exp(-Time.fixedDeltaTime / 2f));

            return threatSpacing;
        }
        /// <summary>
        /// Roll with the leader once settled.
        ///
        /// AutopilotPlane derives roll from the desired flight direction and offers no way
        /// to command it, so this blends the result afterwards and re-filters. Wingmen that
        /// stay wings-level through a banked turn are one of the clearest giveaways that a
        /// formation is being simulated rather than flown.
        /// </summary>
        /// <summary>
        /// Apply the wing's rules of engagement from inside the slot.
        ///
        /// Nothing here touches attitude or throttle, so a Defensive wingman can shoot
        /// without ever compromising station-keeping. Aggressive wingmen additionally look
        /// for an air target worth breaking formation for; ground targets are engaged from
        /// the slot in both postures.
        /// </summary>
        private void RunEngagement(Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastEngageCheck < EngageInterval) return;
            lastEngageCheck = Time.timeSinceLevelLoad;

            // A weapon that passes its own checks would otherwise be fired on every tick,
            // emptying the aircraft in seconds. The stock AI leaves five seconds between
            // launches; this is the same idea, exposed so it can be tuned.
            bool mayFire = Time.timeSinceLevelLoad - lastFiredTime >= Plugin.Config2.FireInterval.Value;

            WingRoe roe = RoeRules.Current;

            WingWeapons.Allow allow = RoeRules.WeaponsFree(roe, aircraft);
            float range = RoeRules.EngageRange(roe);

            // An explicitly assigned target outranks whatever the wingman would pick, and
            // survives until it dies. Missile defence still takes precedence: a missile in
            // the air is more urgent than any order.
            Unit assigned = member.AssignedTarget;
            if (assigned != null && assigned.disabled)
            {
                WingComms.Say(member, WingComms.Call.Splash, assigned.unitName);
                member.ClearAssignedTarget();
                assigned = null;
            }

            // Escort: with no explicit order standing, shoot at what is hunting the leader
            // rather than at whatever is nearest to us. This is the entire difference
            // between Escort and Hold - station-keeping and fire gating are untouched, only
            // the choice of target changes.
            if (assigned == null && RoeRules.GuardsLeader(roe))
            {
                assigned = WingWeapons.NearestThreatTo(leader, range);
                if (assigned != null) WingComms.Say(member, WingComms.Call.Covering);
            }

            bool fired;
            if (assigned != null && allow != WingWeapons.Allow.MissilesOnly)
            {
                fired = mayFire && WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
            }
            else if (allow == WingWeapons.Allow.MissilesOnly)
            {
                WingComms.Say(member, WingComms.Call.Defending);

                // Missile defence is time-critical and uses its own short interval.
                fired = Time.timeSinceLevelLoad - lastFiredTime >= 1f &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
            }
            else
            {
                fired = mayFire && WingWeapons.Engage(aircraft, pilot, allow, range);
            }

            if (fired) lastFiredTime = Time.timeSinceLevelLoad;
        }

        /// <summary>
        /// Break formation to fight when the leader is being shot at. This is the
        /// "smarter AI" behaviour: stock wingmen have no idea their leader is in trouble.
        /// </summary>
        private bool CheckMutualSupport(Aircraft leader)
        {
            if (!Plugin.Config2.MutualSupport.Value) return false;

            // Only the Free rung leaves the slot, and only for this. The cautious rungs
            // answer the same event with a weapon rather than a manoeuvre: Hold shoots the
            // missile down, Escort shoots the aircraft that launched it. Three different
            // responses to one event is what makes the three rungs worth having.
            if (!RoeRules.MayBreakForEmergency(RoeRules.Current)) return false;

            if (Time.timeSinceLevelLoad - lastSupportCheck < 1f) return false;
            lastSupportCheck = Time.timeSinceLevelLoad;

            MissileWarning mw = leader.GetMissileWarningSystem();
            if (mw == null || !mw.IsWarning())
                return false;

            member.BreakToEngage("leader under missile attack");
            return true;
        }

        /// <summary>
        /// Notice when a wingman simply cannot hold the slot and stop it killing itself
        /// trying.
        ///
        /// A helicopter recruited into a jet flight is the clear case: it has nowhere near
        /// the speed, falls further behind every second, and chases flat out and nose-down
        /// until it sinks into the ground. Nothing else in the controller registers that
        /// the task is impossible — the slot is simply somewhere it can never reach.
        ///
        /// If the wingman is a long way out and losing ground for a sustained period, it
        /// reports unable and returns to base rather than pursuing to destruction.
        /// </summary>
        private void CheckAbleToKeepUp(Aircraft leader, float distance)
        {
            if (!Plugin.Config2.KeepUpReports.Value) return;

            // Only a genuine performance gap counts as "unable". This check was written for
            // a helicopter recruited into a jet flight, but it fired on wingmen in the
            // *same airframe* as the leader that were simply a long way back and closing —
            // its own message read "max speed 100 vs leader 100" while the aircraft was
            // overtaking at 87 against 78. Being behind is not the same as being incapable,
            // and sending those wingmen home is what looked like ignoring the formation.
            float mine = aircraft.GetAircraftParameters().maxSpeed;
            float theirs = leader.GetAircraftParameters().maxSpeed;
            if (mine >= theirs * 0.9f) return;

            float threshold = UnableDistance;

            // Close enough, or the gap is not meaningfully growing: reset and carry on.
            //
            // The margin matters. Comparing bare distances meant any frame where the slot
            // shifted outward counted as losing ground, and a slot moves constantly as the
            // leader manoeuvres, so a wingman that was closing overall could still be
            // condemned by the noise.
            if (distance < threshold || distance < lastKeepUpDistance + 1f)
            {
                lastKeepUpDistance = Mathf.Min(lastKeepUpDistance, distance);
                losingGroundSince = 0f;
                return;
            }

            lastKeepUpDistance = distance;

            if (losingGroundSince <= 0f)
            {
                losingGroundSince = Time.timeSinceLevelLoad;
                return;
            }

            if (Time.timeSinceLevelLoad - losingGroundSince < UnableSeconds)
                return;

            losingGroundSince = 0f;

            Plugin.Logger.LogInfo(
                $"[Wing] {aircraft.unitName} cannot hold station " +
                $"({distance:F0} m out, max speed {mine:F0} vs leader {theirs:F0}) - returning to base");

            WingComms.Say(member, WingComms.Call.Unable);
            member.Apply(WingOrder.ReturnToBase);
        }

        /// <summary>
        /// Run the throttle wide open for a few seconds after a rejoin order. The delay
        /// staggers arrivals across the flight so they slot in one at a time.
        /// </summary>
        public void BoostRejoin(float delay = 0f)
        {
            rejoinHoldUntil = Time.timeSinceLevelLoad + delay;
            rejoinBoostUntil = rejoinHoldUntil + 8f;
        }
    }
}
