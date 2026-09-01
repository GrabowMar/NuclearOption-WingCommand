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
    internal class FormationFlyState : WingPilotState
    {
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

        private float lastEngageCheck;
        private float lastFiredTime;
        private float rejoinBoostUntil;
        private float rejoinHoldUntil;
        private float lastKeepUpDistance = float.MaxValue;
        private float losingGroundSince;
        private Vector3 smoothedAvoidance;
        private float threatSpacing = 1f;

        // Every member has the same leader, so scanning the global aircraft registry once
        // per member on every physics tick repeats an identical answer. Missile warnings
        // remain immediate; only the secondary "hostile nearby" cue is shared and polled.
        private const float NearbyThreatRefreshSeconds = 0.25f;
        private static Aircraft nearbyThreatLeader;
        private static float nextNearbyThreatRefresh;
        private static bool nearbyThreatPresent;
        private Vector3 smoothedSlotLocal;
        private bool slotLocalReady;
        private float lateralTurnScale = 1f;
        private float trailTurnScale = 1f;

        /// <summary>Seconds of leader motion fed into the slot position, so the slot is where the leader will be, not where it was.</summary>
        private const float SlotPredictionSeconds = 1f;
        private const float ShapeTransitionSeconds = 1.6f;
        private const float TurnGeometrySeconds = 0.7f;
        private RotaryFormation.Mode lastRotaryMode = (RotaryFormation.Mode)(-1);
        private float lastRotaryReport;

        // --- Smarter-formation state (each behaviour is config-gated) ---

        /// <summary>Signed seconds of consistent leader turn, for the turn-side slot mirror.</summary>
        private float turnPersist;
        private const float TurnMirrorRate = 0.05f;   // rad/s that counts as a real turn
        private const float TurnMirrorHold = 1.5f;    // consistent-turn seconds before the flip

        /// <summary>Eased aft-stagger multiplier while the leader is spiked (combat-spread reaction).</summary>
        private float combatSpread = 1f;
        private bool leaderMissileThreat;
        private const float CombatSpreadBackScale = 1.4f;
        private const float CombatSpreadEaseSeconds = 2f;

        /// <summary>Cached terrain-floor height for this wingman's slot, in local render Y.</summary>
        private float terrainFloorY = float.MinValue;
        private float nextTerrainProbe;
        private const float TerrainProbeInterval = 0.3f;

        /// <summary>Physics-tick counter for the fidelity geometry stride.</summary>
        private int geometryTick;

        public FormationFlyState(WingMember member) : base(member)
        {
            stateDisplayName = "Formation";
        }

        public Aircraft Leader => member.Leader;

        /// <summary>
        /// Leader track, low-pass filtered. Wingmen steer against this rather than the
        /// leader's instantaneous velocity, so the formation follows where the leader is
        /// going instead of reproducing every twitch of the stick.
        /// </summary>
        private Vector3 smoothedLeaderDir;

        /// <summary>The same track flattened into the horizontal plane; the formation's frame.</summary>
        private Vector3 flatLeaderTrack = Vector3.forward;

        /// <summary>Filtered rate of change of the leader's heading, in rad/s, positive to the right.</summary>
        private float leaderTurnRate;

        /// <summary>
        /// Seconds of smoothing. Long enough to reject stick noise, short enough not to lag a
        /// turn. Tightened to half a second: the track still filters the leader's every
        /// twitch, but a genuine manoeuvre is now followed rather than trailed.
        /// </summary>
        private const float LeaderTrackSmoothing = 0.5f;

        /// <summary>Seconds of smoothing on the differentiated heading rate.</summary>
        private const float TurnRateSmoothing = 0.35f;

        /// <summary>
        /// Heading rate below which the leader counts as flying straight, in rad/s. A third
        /// of a degree per second: far below any deliberate turn, and above the residue that
        /// differentiating a filtered signal always leaves behind.
        /// </summary>
        private const float TurnRateDeadband = 0.006f;

        /// <summary>Fastest heading rate treated as real, in rad/s. Well above any flyable turn.</summary>
        private const float MaxCredibleTurnRate = 1.5f;

        /// <summary>
        /// One report every five seconds, per wingman. The timer lives here rather than in
        /// the flight model because the model is static and shared: a single static timer
        /// meant three wingmen took turns logging, so consecutive lines described different
        /// aircraft and the numbers looked like one aircraft thrashing.
        /// </summary>
        private bool DueToReport()
        {
            if (!Plugin.Settings.VerboseLogging.Value) return false;
            if (Time.timeSinceLevelLoad - lastReport < 5f) return false;

            lastReport = Time.timeSinceLevelLoad;
            return true;
        }

        private float lastReport;

        /// <summary>
        /// Advance the filtered leader track and the heading rate derived from it, and
        /// return that track flattened into the horizontal plane.
        ///
        /// The heading rate is differentiated from the *smoothed track*, not read off the
        /// leader's rigidbody, and that is the entire point of it. Expanding the body rates,
        /// <c>Dot(angularVelocity, up)</c> comes out as
        /// <c>p·sin(pitch) - q·sin(bank)·cos(pitch) + r·cos(bank)·cos(pitch)</c>, so the roll
        /// rate leaks straight into it at any nose-up attitude. Ten degrees of pitch and a
        /// gentle one rad/s roll reported 0.17 rad/s of "turn" — enough on its own to
        /// saturate the formation's turn geometry and swing the steering aim point over a
        /// hundred metres sideways, changing sign on every roll reversal. That is the slow
        /// left-right sway. A differentiated heading cannot see roll at all.
        /// </summary>
        private Vector3 TrackLeader(Aircraft leader)
        {
            Vector3 instant = leader.rb != null && leader.rb.velocity.sqrMagnitude > 1f
                ? leader.rb.velocity.normalized
                : leader.transform.forward;

            if (smoothedLeaderDir.sqrMagnitude < 0.5f)
            {
                smoothedLeaderDir = instant;
                flatLeaderTrack = Flatten(instant);
                leaderTurnRate = 0f;
                return flatLeaderTrack;
            }

            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);

            smoothedLeaderDir = Vector3.Slerp(
                smoothedLeaderDir, instant,
                1f - Mathf.Exp(-dt / LeaderTrackSmoothing)).normalized;

            Vector3 flat = Flatten(smoothedLeaderDir);

            // Clamped to a rate no aircraft can actually fly — a nine-g turn at combat speed
            // is under half a rad/s — so that any discontinuity in the track, from whatever
            // source, can never be read as a turn and thrown at the formation geometry.
            float measured = Mathf.Clamp(
                Vector3.SignedAngle(flatLeaderTrack, flat, Vector3.up) * Mathf.Deg2Rad / dt,
                -MaxCredibleTurnRate, MaxCredibleTurnRate);
            flatLeaderTrack = flat;

            leaderTurnRate = Mathf.Lerp(
                leaderTurnRate, measured, 1f - Mathf.Exp(-dt / TurnRateSmoothing));

            return flat;
        }

        /// <summary>The turn rate the geometry acts on: filtered, and zero inside the noise band.</summary>
        private float LeaderTurnRate =>
            Mathf.Abs(leaderTurnRate) < TurnRateDeadband ? 0f : leaderTurnRate;

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }


        public override void EnterState(Pilot pilot)
        {
            // RotaryFormation re-selects its own hover regime later if it needs one; a
            // rejoin is a cruise, so BeginFlight's default hover release is right here.
            BeginFlight(pilot);

            slotLocalReady = false;
            lastRotaryMode = (RotaryFormation.Mode)(-1);
            lastRotaryReport = 0f;
            turnPersist = 0f;
            combatSpread = 1f;
            terrainFloorY = float.MinValue;
            nextTerrainProbe = 0f;
            geometryTick = 0;

            // Start the leader track from scratch. It survives a state exit, so a wingman
            // that broke off to fight and is now rejoining would otherwise differentiate a
            // heading minutes out of date and read it as one enormous turn.
            smoothedLeaderDir = Vector3.zero;
            leaderTurnRate = 0f;

            if (Plugin.Settings.VerboseLogging.Value)
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
            // Keep the gear up while flying. The one-shot retraction in EnterState can be
            // undone when the game's own spawn initialisation runs afterwards and lowers the
            // gear, so re-assert it every tick.
            if (aircraft != null && aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            Aircraft leader = Leader;

            // Leader gone, or we are no longer flyable: hand back to the stock AI.
            if (leader == null || leader.disabled || aircraft == null || aircraft.disabled)
            {
                member.ReleaseToCombat("leader lost");
                return;
            }

            // Jam Target is flown as ordinary formation, plus a jammer held on the
            // designated unit. When that unit dies the order is complete.
            if (member.Order == WingOrder.JamTarget)
            {
                Unit jamTarget = member.AssignedTarget;
                if (jamTarget == null || jamTarget.disabled)
                {
                    WingComms.Say(member, WingComms.Call.JammingOff);
                    member.Apply(WingOrder.Formation);
                    return;
                }
                member.Jammer.Pulse(aircraft);
            }

            // Fidelity throttle: at low settings recompute the slot geometry only every
            // Nth physics tick and let the autopilot coast on its last command in between.
            // Phased by slot so the wing does not all recompute on the same frame. The
            // missile-defence panic path runs from the manager loop, not here, so it is
            // never strided.
            int stride = WingBrain.GeometryStride;
            if (stride > 1 && (++geometryTick + member.Slot) % stride != 0)
                return;

            RunEngagement(leader);

            // One filtered leader signal drives every piece of geometry below: the frame the
            // slots hang off, the prediction that anchors them, the turn compensation and the
            // steering feed-forward. Those were derived separately from the nose direction,
            // the world-y angular velocity and the raw velocity vector, and could therefore
            // disagree with one another about which way the leader was actually going.
            Vector3 track = TrackLeader(leader);
            float turnRate = LeaderTurnRate;

            FormationShape shape = Plugin.Settings.Shape.Value;

            // Helicopters fly slower and much closer together than jets, so the same
            // spacing that reads as tight for a fighter formation looks scattered for them.
            float spacing = Plugin.Settings.SlotSpacing.Value;
            if (WingRegistry.IsRotary(aircraft))
                spacing *= Plugin.Settings.RotarySpacingScale.Value;

            spacing *= ThreatSpacingScale(leader);

            EaseSlotLocal(shape, spacing, turnRate);

            GlobalPosition slotPos = SlotPosition(leader, track, spacing, out Vector3 offset);

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
                    smoothedLeaderDir, turnRate, DueToReport(), shape, lateralTurnScale);
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
        /// Settle this frame's slot in leader-local space.
        ///
        /// Everything here is a shape change rather than a position: turn compression,
        /// the threat step-back and the turn-side mirror all move where the slot sits
        /// relative to the leader, and all of them ease so the autopilot is never handed
        /// a discontinuity to chase.
        /// </summary>
        private void EaseSlotLocal(FormationShape shape, float spacing, float turnRate)
        {
            // Fluid formation geometry: a hard turn compresses the line abreast component
            // and opens the trail component slightly. That reduces the impossible speed
            // difference between inside and outside slots while keeping the formation
            // recognisable instead of letting it dissolve into individual chases.
            float turn = Mathf.Clamp01(Mathf.Abs(turnRate) / 0.18f);
            float geometryBlend = 1f - Mathf.Exp(-Time.fixedDeltaTime / TurnGeometrySeconds);
            lateralTurnScale = Mathf.Lerp(lateralTurnScale, Mathf.Lerp(1f, 0.72f, turn), geometryBlend);
            trailTurnScale = Mathf.Lerp(trailTurnScale, Mathf.Lerp(1f, 1.12f, turn), geometryBlend);

            int mirrorSign = TurnMirrorSign(turnRate);

            // Step the formation aft when the leader is spiked: a covering trail is harder
            // to shoot at than a parade slot and leaves room to react. Eased, because a
            // stepped change moves every slot at once and the autopilot chases the jump.
            float combatSpreadTarget =
                WingBrain.SmartFormation && leaderMissileThreat
                    ? CombatSpreadBackScale : 1f;
            combatSpread = Mathf.Lerp(combatSpread, combatSpreadTarget,
                1f - Mathf.Exp(-Time.fixedDeltaTime / CombatSpreadEaseSeconds));

            Vector3 desiredSlotLocal = FormationSolver.SlotCoordinates(
                member.Slot, shape, spacing, Plugin.Settings.SlotStack.Value,
                lateralTurnScale, trailTurnScale);

            // Turn-side mirroring: a one-sided formation sitting on the inside of a
            // sustained turn is being commanded to an ever-tighter radius it cannot fly.
            // Flip it to the outside (echelon right becomes echelon left); the
            // smoothedSlotLocal lerp below carries the cross-under, and separation plus
            // path-cut avoidance keep it clear of the leader. Symmetric shapes already
            // split the turn, so they are left alone.
            if (WingBrain.SmartFormation && mirrorSign != 0 &&
                (shape == FormationShape.EchelonRight || shape == FormationShape.EchelonLeft) &&
                (int)Mathf.Sign(desiredSlotLocal.x) == mirrorSign)
            {
                desiredSlotLocal.x = -desiredSlotLocal.x;
            }

            // z is negative aft, so scaling it preserves the sign and only lengthens the trail.
            desiredSlotLocal.z *= combatSpread;

            if (!slotLocalReady)
            {
                smoothedSlotLocal = desiredSlotLocal;
                slotLocalReady = true;
            }
            else
            {
                // Shape, threat-spacing and manoeuvre changes all ease in leader-local
                // space. The shape moves continuously, but it still rotates with the leader
                // immediately instead of lagging behind in world space.
                smoothedSlotLocal = Vector3.Lerp(
                    smoothedSlotLocal, desiredSlotLocal,
                    1f - Mathf.Exp(-Time.fixedDeltaTime / ShapeTransitionSeconds));
            }
        }

        /// <summary>
        /// Turn the settled leader-local slot into a world position: rotate it onto the
        /// leader's track, lead the leader's motion, then apply separation, path-cut
        /// avoidance and the terrain floor.
        /// </summary>
        private GlobalPosition SlotPosition(Aircraft leader, Vector3 track, float spacing,
                                            out Vector3 offset)
        {
            // The frame the slots hang off is the leader's *track*, not its nose. Sideslip and
            // yaw wobble swing the nose several degrees either side of the flight path, and
            // rotating the whole formation by that moves every slot laterally in proportion
            // to how far out it sits — so the outermost wingman travelled several times as
            // far as the closest one, which is what the sway looked like.
            offset = FormationSolver.WorldOffset(track, smoothedSlotLocal);

            // Anchor the slot to where the leader is going, not where it is: a fast leader
            // drags an un-predicted slot behind it and the wingman spends the whole flight
            // chasing a moving target it can never sit on. Predicting the leader's own motion
            // removes that lag so station-keeping converges instead of perpetually trailing.
            //
            // The prediction runs along the filtered track rather than the instantaneous
            // velocity: a whole second of prediction magnifies every degree of stick wobble
            // into lateral slot motion. Only the vertical component stays raw, so a climb or
            // a dive is still led honestly.
            Vector3 leaderVel = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            Vector3 predictedMotion = track * leader.speed + Vector3.up * leaderVel.y;
            GlobalPosition slotPos = leader.GlobalPosition()
                                     + predictedMotion * SlotPredictionSeconds
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
            slotPos = ApplyTerrainFloor(slotPos);

            return slotPos;
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
            bool changed = mode != lastRotaryMode;
            // Flight-state memory is control state, not diagnostic state. This used to be
            // assigned below the verbose-logging guard, which disabled hover/cruise
            // hysteresis during normal gameplay.
            lastRotaryMode = mode;

            if (!Plugin.Settings.VerboseLogging.Value) return;

            bool due = Time.timeSinceLevelLoad - lastRotaryReport > 5f;
            if (!changed && !due) return;

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
        /// +1 / -1 once the leader has been turning that way for <see cref="TurnMirrorHold"/>
        /// seconds without a sign change, 0 otherwise. The persistence decays back toward
        /// zero when the turn eases, so a brief jink never flips the whole formation.
        /// </summary>
        private int TurnMirrorSign(float turnRate)
        {
            if (Mathf.Abs(turnRate) > TurnMirrorRate)
            {
                // Mathf.Sign returns exact +/-1, so this comparison is safe. A sign flip
                // means the leader reversed the turn; start counting the new one from zero.
                if (Mathf.Sign(turnRate) != Mathf.Sign(turnPersist)) turnPersist = 0f;
                turnPersist += Mathf.Sign(turnRate) * Time.fixedDeltaTime;
            }
            else
            {
                turnPersist = Mathf.MoveTowards(turnPersist, 0f, Time.fixedDeltaTime);
            }

            return Mathf.Abs(turnPersist) >= TurnMirrorHold ? (int)Mathf.Sign(turnPersist) : 0;
        }

        /// <summary>
        /// Push a slot up so it keeps clearance over rising ground. Slot offsets are
        /// relative to the leader, so on climbing terrain the low side of a stack can sit
        /// inside a hillside even while the leader is comfortably clear of it. Probed a few
        /// times a second, not every physics tick.
        /// </summary>
        private GlobalPosition ApplyTerrainFloor(GlobalPosition slotPos)
        {
            float clearance = WingBrain.TerrainClearance;
            if (clearance <= 0f) return slotPos;

            Vector3 local = slotPos.ToLocalPosition();

            if (Time.timeSinceLevelLoad >= nextTerrainProbe)
            {
                nextTerrainProbe = Time.timeSinceLevelLoad +
                                   WingBrain.Interval(TerrainProbeInterval);

                float ground = Datum.LocalSeaY;
                if (Physics.Raycast(new Vector3(local.x, Datum.LocalSeaY + 3000f, local.z),
                                    Vector3.down, out RaycastHit hit, 6000f,
                                    PhysicsLayers.StaticsMask))
                    ground = Mathf.Max(ground, hit.point.y);

                terrainFloorY = ground + clearance;
            }

            if (local.y >= terrainFloorY) return slotPos;
            local.y = terrainFloorY;
            return local.ToGlobalPosition();
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
            // Whether the leader is under a missile warning is needed by the combat-spread
            // reaction regardless of whether the widen behaviour is enabled, so resolve it
            // unconditionally here rather than inside the widen branch.
            MissileWarning leaderWarning = leader.GetMissileWarningSystem();
            leaderMissileThreat = leaderWarning != null && leaderWarning.IsWarning();

            // Driven by the fidelity slider now: the reactive widen is a smart-formation
            // behaviour, and a scale of 1 is the off switch.
            float scale = WingBrain.SmartFormation ? WingBrain.ThreatWidenScale : 1f;
            float target = 1f;

            if (scale > 1.001f)
            {
                MissileWarning ownWarning = aircraft != null
                    ? aircraft.GetMissileWarningSystem()
                    : null;

                bool threatened = leaderMissileThreat ||
                    (ownWarning != null && ownWarning.IsWarning());

                // A visual contact inside the tactical bubble is enough to loosen up even
                // before a missile is in the air. ROE alone is not: selecting Free while
                // cruising in an empty sky should not permanently scatter the wing.
                if (!threatened)
                    threatened = NearbyThreatToLeader(leader);

                if (threatened) target = scale;
            }

            threatSpacing = Mathf.Lerp(
                threatSpacing <= 0f ? 1f : threatSpacing, target,
                1f - Mathf.Exp(-Time.fixedDeltaTime / 2f));

            return threatSpacing;
        }

        private static bool NearbyThreatToLeader(Aircraft leader)
        {
            float now = Time.timeSinceLevelLoad;
            if (leader != nearbyThreatLeader || now >= nextNearbyThreatRefresh)
            {
                nearbyThreatLeader = leader;
                nextNearbyThreatRefresh = now + WingBrain.Interval(NearbyThreatRefreshSeconds);
                nearbyThreatPresent = WingWeapons.NearestThreatTo(leader, 8000f) != null;
            }

            return nearbyThreatPresent;
        }

        /// <summary>
        /// Apply the wing's rules of engagement from inside the slot.
        ///
        /// Nothing here touches attitude or throttle, so a Defensive wingman can shoot
        /// without ever compromising station-keeping. Free wingmen may additionally look
        /// for a target worth breaking formation for; Escort is restricted to air targets
        /// while Hold stays defensive.
        /// </summary>
        private void RunEngagement(Aircraft leader)
        {
            if (Time.timeSinceLevelLoad - lastEngageCheck < WingBrain.Interval(EngageInterval))
                return;
            lastEngageCheck = Time.timeSinceLevelLoad;

            OrderEngagementAuthority authority = OrderRoePolicy.Authority(member.Order);
            if (authority == OrderEngagementAuthority.DefensiveOnly) return;

            // A weapon that passes its own checks would otherwise be fired on every tick,
            // emptying the aircraft in seconds. The stock AI leaves five seconds between
            // launches; this is the same idea, exposed so it can be tuned.
            bool mayFire = Time.timeSinceLevelLoad - lastFiredTime >= WingWeapons.FireInterval(aircraft);

            WingRoe roe = RoeRules.Current;

            WingWeapons.Allow allow = authority == OrderEngagementAuthority.AutonomousCombat
                ? WingWeapons.Allow.AirAndGround
                : RoeRules.WeaponsFree(roe, aircraft);
            bool explicitTargetOrder = authority == OrderEngagementAuthority.ExplicitTarget;
            bool orderOwnsWeapons = explicitTargetOrder ||
                                     authority == OrderEngagementAuthority.AutonomousCombat;
            float range = orderOwnsWeapons
                ? RoeRules.ExplicitOrderRange()
                : RoeRules.EngageRange(roe);

            // Low fidelity: a formation wingman flies the slot and defends only. Explicit
            // attack/engage orders and inbound-missile interception still run; the
            // opportunity/priority-target hunt - which does the all-aircraft scans - does not.
            if (!WingBrain.OpportunityFire && !orderOwnsWeapons &&
                allow != WingWeapons.Allow.MissilesOnly)
                return;

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
            bool coveringLeader = false;
            if (assigned == null)
            {
                assigned = RoeRules.PriorityTarget(roe, aircraft, leader, range);
                coveringLeader = assigned != null;
            }

            bool fired;
            if (assigned != null && allow != WingWeapons.Allow.MissilesOnly)
            {
                fired = mayFire && WingWeapons.EngageSpecific(aircraft, pilot, assigned, range);
            }
            else if (allow == WingWeapons.Allow.MissilesOnly)
            {
                // Missile defence is time-critical and uses its own short interval.
                fired = Time.timeSinceLevelLoad - lastFiredTime >= 1f &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
                if (fired) WingComms.Say(member, WingComms.Call.Defending);
            }
            else
            {
                fired = mayFire &&
                        (authority == OrderEngagementAuthority.AutonomousCombat ||
                         RoeRules.MayChooseOpportunityTarget(roe)) &&
                        WingWeapons.Engage(aircraft, pilot, allow, range);
            }

            if (fired)
            {
                lastFiredTime = Time.timeSinceLevelLoad;
                if (coveringLeader) WingComms.Say(member, WingComms.Call.Covering);
            }
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
            if (!Plugin.Settings.KeepUpReports.Value) return;

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
