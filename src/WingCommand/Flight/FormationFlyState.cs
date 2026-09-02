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

        /// <summary>Seconds between massed shots while expending on a Splash 'Em target from the slot.</summary>
        private const float SplashFireInterval = 0.8f;

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

        /// <summary>Shooting from the slot. Shared with OrbitState; this state only flies.</summary>
        private readonly SlotEngagement engagement = new SlotEngagement(EngageInterval);

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

        /// <summary>
        /// Seconds of the leader's vertical motion fed into the slot position, so a climb or
        /// dive is led rather than trailed. The horizontal along-track component is deliberately
        /// not led: at cruise speed a second of lead is several hundred metres, which parked the
        /// slots ahead of the leader — the leader is the front of the formation, always. The
        /// along-track lag a fast leader would otherwise open is the throttle's job to close
        /// (see <see cref="FixedWingFormation.Throttle"/> and its speed lead), not the slot's.
        /// </summary>
        private const float SlotVerticalLeadSeconds = 1f;
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

        /// <summary>Filtered rate of change of the leader's speed, m/s². The acceleration feed-forward.</summary>
        private float leaderSpeedRate;

        /// <summary>Filtered vertical speed of the leader, m/s. The slot's height feed-forward.</summary>
        private float leaderClimbRate;

        /// <summary>Leader speed last sample, for differentiating the above.</summary>
        private float lastLeaderSpeed;

        /// <summary>Smoothed leader lever position, and whether it could be read at all.</summary>
        private float leaderThrottle;
        private bool leaderThrottleKnown;

        /// <summary>
        /// When the geometry last ran, so every filter below can use the time that actually
        /// elapsed. It is not <c>Time.fixedDeltaTime</c>: Performance mode recomputes the
        /// geometry only every third physics tick, so a filter assuming a full-rate tick was
        /// differentiating over a third of the real interval and reporting three times the
        /// leader's true turn rate for it.
        /// </summary>
        private float lastGeometryTime;

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
        /// Fastest leader vertical speed treated as real, m/s. Above any sustained climb or
        /// dive in the game, so a genuine manoeuvre is never clipped. Like
        /// <see cref="MaxCredibleTurnRate"/> it exists only so that a respawn, a collision or
        /// a dropped frame cannot be read off the rigidbody and projected into the slot.
        /// </summary>
        private const float MaxCredibleClimbRate = 250f;

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
        private LeaderState TrackLeader(Aircraft leader, float dt)
        {
            Vector3 instant = leader.rb != null && leader.rb.velocity.sqrMagnitude > 1f
                ? leader.rb.velocity.normalized
                : leader.transform.forward;

            ReadLeaderThrottle(leader, dt);

            if (smoothedLeaderDir.sqrMagnitude < 0.5f)
            {
                smoothedLeaderDir = instant;
                flatLeaderTrack = Flatten(instant);
                leaderTurnRate = 0f;
                leaderSpeedRate = 0f;
                leaderClimbRate = leader.rb != null ? leader.rb.velocity.y : 0f;
                lastLeaderSpeed = leader.speed;
                return State();
            }

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

            // The leader's acceleration, differentiated from its speed for the same reason
            // the turn rate is differentiated from the track rather than read off the
            // rigidbody: it is the quantity the throttle law actually needs, and deriving it
            // from anything else lets it disagree with the speed it is added to. Smoothed,
            // because a raw frame-to-frame speed delta is mostly noise, and clamped where it
            // is consumed so a respawn cannot be projected forward as a speed demand.
            float rate = (leader.speed - lastLeaderSpeed) / dt;
            lastLeaderSpeed = leader.speed;

            leaderSpeedRate = Mathf.Lerp(
                leaderSpeedRate,
                Mathf.Clamp(rate, -WingTuning.MaxCredibleAccel, WingTuning.MaxCredibleAccel),
                1f - Mathf.Exp(-dt / WingTuning.SpeedRateSmoothing));

            // The leader's vertical speed, filtered like every other signal here and for the
            // same reason. It is fed forward into the slot's height over a full second, so
            // read raw off the rigidbody it hands the wingman a destination that moves up and
            // down with the leader's every pitch twitch - the vertical twin of the roll-rate
            // leak that used to be the formation's left-right sway. Nothing else in this
            // struct was allowed to reach the geometry unfiltered; this was the omission.
            float climb = leader.rb != null ? leader.rb.velocity.y : 0f;
            leaderClimbRate = Mathf.Lerp(
                leaderClimbRate,
                Mathf.Clamp(climb, -MaxCredibleClimbRate, MaxCredibleClimbRate),
                1f - Mathf.Exp(-dt / WingTuning.SpeedRateSmoothing));

            return State();
        }

        /// <summary>Bundle this tick's filtered leader signals for the flight models.</summary>
        private LeaderState State() =>
            new LeaderState(smoothedLeaderDir, flatLeaderTrack, LeaderTurnRate,
                            leaderSpeedRate, leaderClimbRate, leaderThrottle,
                            leaderThrottleKnown);

        /// <summary>
        /// Smooth the leader's lever position. This is the anticipation's whole input, and
        /// the reason it is worth having: the lever moves on the frame the player moves it,
        /// while the acceleration it causes takes a second or more to become measurable.
        ///
        /// The smoothing is short on purpose — it exists to stop an AI leader's bang-bang
        /// throttle chattering the whole wing's power, not to slow down a player's hand.
        /// </summary>
        private void ReadLeaderThrottle(Aircraft leader, float dt)
        {
            ControlInputs inputs = leader.GetInputs();
            if (inputs == null)
            {
                // Deliberately not "throttle zero": see LeaderState.ThrottleKnown.
                leaderThrottleKnown = false;
                return;
            }

            float lever = Mathf.Clamp01(inputs.throttle);

            if (!leaderThrottleKnown)
            {
                leaderThrottle = lever;
                leaderThrottleKnown = true;
                return;
            }

            leaderThrottle = Mathf.Lerp(
                leaderThrottle, lever,
                1f - Mathf.Exp(-dt / WingTuning.LeaderThrottleSmoothing));
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
            // heading minutes out of date and read it as one enormous turn. The speed rate
            // and the throttle are differentiated and filtered the same way and go stale the
            // same way, so they reset with it.
            smoothedLeaderDir = Vector3.zero;
            leaderTurnRate = 0f;
            leaderSpeedRate = 0f;
            leaderClimbRate = 0f;
            lastLeaderSpeed = 0f;
            leaderThrottleKnown = false;
            lastGeometryTime = 0f;

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

            // No leader to hold a slot on, or we are no longer flyable. Stop flying and wait
            // for the next arbitration pass — the LeaderLost reflex owns this case and will
            // put the aircraft into a holding orbit.
            //
            // This used to call ReleaseToCombat, which is a teardown: it overwrites the
            // standing directive with Engage and hands the aircraft to the stock combat AI.
            // That is right for a member leaving the roster and wrong for one whose leader
            // just died — and because FixedUpdate runs before Update, it beat the arbiter to
            // the punch on the very tick the player was killed. The order the wing was given
            // was destroyed before anything could preserve it, and on respawn the whole wing
            // resolved to Engage and flew off under stock AI while still on the roster.
            if (leader == null || leader.disabled || aircraft == null || aircraft.disabled)
                return;

            // What this wingman is working from the slot, asked once. It used to be inferred
            // from the standing order in two separate places, which is how one state came to
            // be running three behaviours with no way to name which.
            SlotTask task = member.SlotTask;

            // A jam order whose target has died is finished. Asked of the order rather than
            // the task because that is exactly the difference being tested: the order still
            // says JamTarget, and SlotTask has already stopped saying Jam because the unit
            // is gone.
            if (member.Order == WingOrder.JamTarget && task != SlotTask.Jam)
            {
                WingComms.Say(member, WingComms.Call.JammingOff);
                member.Complete(WingOrder.Formation);
                return;
            }

            // Fidelity throttle: at low settings recompute the slot geometry only every
            // Nth physics tick and let the autopilot coast on its last command in between.
            // Phased by slot so the wing does not all recompute on the same frame. The
            // missile-defence panic path runs from the manager loop, not here, so it is
            // never strided.
            int stride = WingBrain.GeometryStride;
            if (stride > 1 && (++geometryTick + member.Slot) % stride != 0)
                return;

            // Behind the stride, deliberately. Unit.Jam broadcasts a ClientRpc, so running
            // it ahead of the stride made the one behaviour with a per-tick networked side
            // effect the one behaviour Performance mode could not thin out - on a host
            // simulating a squadron per player, which is the case the mode exists for.
            if (task == SlotTask.Jam) RunJam();

            if (task == SlotTask.Splash)
                RunSplash();
            else
                engagement.Run(member, aircraft, pilot, leader);

            // The time that actually elapsed since the geometry last ran, which under the
            // Performance stride is several physics ticks rather than one. Every filter and
            // differentiator below is written in seconds, so they all need this and not
            // Time.fixedDeltaTime. Clamped because the first tick after a state entry, a
            // pause or a scene hitch has no meaningful interval to offer.
            float now = Time.timeSinceLevelLoad;
            float dt = lastGeometryTime > 0f
                ? Mathf.Clamp(now - lastGeometryTime, 0.0001f, 0.5f)
                : Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            lastGeometryTime = now;

            // One filtered leader signal drives every piece of geometry below: the frame the
            // slots hang off, the prediction that anchors them, the turn compensation, the
            // steering feed-forward and now the speed law. Those were derived separately from
            // the nose direction, the world-y angular velocity and the raw velocity vector,
            // and could therefore disagree with one another about which way the leader was
            // actually going.
            LeaderState leaderState = TrackLeader(leader, dt);
            Vector3 track = leaderState.FlatTrack;
            float turnRate = leaderState.TurnRate;

            FormationShape shape = WingFormation.Shape;

            // Helicopters fly slower and much closer together than jets, so the same
            // spacing that reads as tight for a fighter formation looks scattered for them.
            float spacing = WingFormation.SlotSpacing;
            if (WingRegistry.IsRotary(aircraft))
                spacing *= WingTuning.RotarySpacingScale;

            // Rules of engagement set the resting spread — Defend pulls in, Free opens out —
            // and the reactive threat widen is layered on as the larger of the two, never
            // multiplied in: a Free wing already flying a spread should not scatter further
            // still the moment a missile is called, and a Defend wing must still be allowed
            // to open up when it is actually being shot at. ThreatSpacingScale is called
            // unconditionally regardless, because it also latches leaderMissileThreat for
            // the combat-spread reaction.
            float roeScale = RoeRules.SpacingScale(RoeRules.Current);
            float threatScale = ThreatSpacingScale(leader, dt);
            spacing *= threatScale > 1.001f ? Mathf.Max(roeScale, threatScale) : roeScale;

            EaseSlotLocal(shape, spacing, turnRate, dt);

            GlobalPosition slotPos = SlotPosition(leader, leaderState, track, spacing, dt,
                                                  out Vector3 offset);

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
                    leaderState, DueToReport(), shape, lateralTurnScale);
            }
            else
            {
                RotaryFormation.Mode mode = RotaryFormation.Fly(
                    aircraft, leader, slotPos, toSlot, distance, offset.y, spacing,
                    lastRotaryMode, leaderState, out float horizontalError);

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
        private void EaseSlotLocal(FormationShape shape, float spacing, float turnRate, float dt)
        {
            // Fluid formation geometry: a hard turn compresses the line abreast component
            // and opens the trail component slightly. That reduces the impossible speed
            // difference between inside and outside slots while keeping the formation
            // recognisable instead of letting it dissolve into individual chases.
            float turn = Mathf.Clamp01(Mathf.Abs(turnRate) / 0.18f);
            float geometryBlend = 1f - Mathf.Exp(-dt / TurnGeometrySeconds);
            lateralTurnScale = Mathf.Lerp(lateralTurnScale, Mathf.Lerp(1f, 0.72f, turn), geometryBlend);
            trailTurnScale = Mathf.Lerp(trailTurnScale, Mathf.Lerp(1f, 1.12f, turn), geometryBlend);

            int mirrorSign = TurnMirrorSign(turnRate, dt);

            // Step the formation aft when the leader is spiked: a covering trail is harder
            // to shoot at than a parade slot and leaves room to react. Eased, because a
            // stepped change moves every slot at once and the autopilot chases the jump.
            float combatSpreadTarget =
                WingBrain.SmartFormation && leaderMissileThreat
                    ? CombatSpreadBackScale : 1f;
            combatSpread = Mathf.Lerp(combatSpread, combatSpreadTarget,
                1f - Mathf.Exp(-dt / CombatSpreadEaseSeconds));

            Vector3 desiredSlotLocal = FormationSolver.SlotCoordinates(
                member.Slot, shape, spacing, WingTuning.SlotStack,
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
                    1f - Mathf.Exp(-dt / ShapeTransitionSeconds));
            }
        }

        /// <summary>
        /// Turn the settled leader-local slot into a world position: rotate it onto the
        /// leader's track, lead the leader's motion, then apply separation, path-cut
        /// avoidance and the terrain floor.
        /// </summary>
        private GlobalPosition SlotPosition(Aircraft leader, LeaderState leaderState,
                                            Vector3 track, float spacing,
                                            float dt, out Vector3 offset)
        {
            // The frame the slots hang off is the leader's *track*, not its nose. Sideslip and
            // yaw wobble swing the nose several degrees either side of the flight path, and
            // rotating the whole formation by that moves every slot laterally in proportion
            // to how far out it sits — so the outermost wingman travelled several times as
            // far as the closest one, which is what the sway looked like.
            offset = FormationSolver.WorldOffset(track, smoothedSlotLocal);

            // The slot hangs off the leader's current position plus its formation offset, so
            // it is always behind the leader — the leader is the front of the formation. Only
            // the leader's vertical motion is led: without it a climbing or diving leader drags
            // every slot up or down behind it and the wingmen perpetually trail the altitude.
            // The along-track lag a fast leader would otherwise open is closed by the throttle's
            // own speed lead (FixedWingFormation.Throttle), not by moving the slot forward.
            //
            // The climb rate is the *filtered* one. Read straight off the rigidbody it was the
            // one leader signal reaching the geometry raw, and a full second of gain on an
            // unfiltered vertical velocity moves this destination up and down with every pitch
            // twitch of the leader — the vertical twin of the roll-rate leak that TrackLeader's
            // whole comment block exists to explain.
            Vector3 predictedMotion = Vector3.up * leaderState.ClimbRate;
            GlobalPosition slotPos = leader.GlobalPosition()
                                     + predictedMotion * SlotVerticalLeadSeconds
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
                1f - Mathf.Exp(-dt / AvoidanceSmoothing));

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
        private int TurnMirrorSign(float turnRate, float dt)
        {
            if (Mathf.Abs(turnRate) > TurnMirrorRate)
            {
                // Mathf.Sign returns exact +/-1, so this comparison is safe. A sign flip
                // means the leader reversed the turn; start counting the new one from zero.
                if (Mathf.Sign(turnRate) != Mathf.Sign(turnPersist)) turnPersist = 0f;
                turnPersist += Mathf.Sign(turnRate) * dt;
            }
            else
            {
                turnPersist = Mathf.MoveTowards(turnPersist, 0f, dt);
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
        private float ThreatSpacingScale(Aircraft leader, float dt)
        {
            // Whether the leader is under a missile warning is needed by the combat-spread
            // reaction as well as by the widen below, so it is resolved once here rather
            // than inside either branch - but both readers are smart-formation behaviours,
            // so in Performance this was an engine call per member per geometry tick whose
            // answer nothing went on to read.
            if (WingBrain.SmartFormation)
            {
                MissileWarning leaderWarning = leader.GetMissileWarningSystem();
                leaderMissileThreat = leaderWarning != null && leaderWarning.IsWarning();
            }

            // Driven by the fidelity slider now: the reactive widen is a smart-formation
            // behaviour, and a scale of 1 is the off switch.
            float scale = WingBrain.SmartFormation ? WingTuning.ThreatWidenScale : 1f;
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
                1f - Mathf.Exp(-dt / 2f));

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
        /// Splash 'Em flown from the slot: hold station and work every effective store into
        /// the designated target until it dies or the aircraft has nothing left that can
        /// hurt it. ROE is ignored — an explicit designation is weapons authorization — and
        /// the massed cadence is the short one the attack run used, so the loadout goes out
        /// as a sustained volley rather than paced shots.
        ///
        /// Finishing does not rejoin anything. The wingman never left its slot, so the order
        /// simply retires: the arbiter resolves back to the standing task, which is this same
        /// state, and no switch happens at all. It used to re-enter formation with a rejoin
        /// boost, which produced a visible surge every time a target died under an aircraft
        /// that had been holding station the whole time.
        /// </summary>
        private void RunSplash()
        {
            if (Time.timeSinceLevelLoad - lastEngageCheck < WingBrain.Interval(EngageInterval))
                return;
            lastEngageCheck = Time.timeSinceLevelLoad;

            Unit target = member.AssignedTarget;
            if (target == null || target.disabled)
            {
                if (target != null) WingComms.Say(member, WingComms.Call.Splash, target.unitName);
                FinishSplash();
                return;
            }

            if (!WingWeapons.CanStillEngage(aircraft, target))
            {
                WingComms.Say(member, WingComms.Call.Expended);
                FinishSplash();
                return;
            }

            if (Time.timeSinceLevelLoad - lastFiredTime < SplashFireInterval) return;

            if (WingWeapons.EngageMassed(aircraft, pilot, target, RoeRules.ExplicitOrderRange()))
                lastFiredTime = Time.timeSinceLevelLoad;
        }

        /// <summary>
        /// <summary>
        /// Hold the jammer on the designated unit while flying the slot.
        ///
        /// The pulse only ever protects this aircraft: RadarJammer.Fire calls
        /// Aircraft.AddECMIntensity on itself, nothing about the target. Actually denying
        /// that unit's own radar needs Unit.Jam - the call the stock JammingPod weapon makes
        /// against whatever it is aimed at - which raises jamAccumulation on every Radar
        /// attached to the target until Radar.IsJammed blinds it. That decays continuously
        /// in Radar.Update, so it rides the same pulse cadence to stay saturated. Host-only:
        /// Unit.Jam broadcasts a ClientRpc.
        /// </summary>
        private void RunJam()
        {
            Unit jamTarget = member.AssignedTarget;
            if (jamTarget == null) return;

            if (member.Jammer.Pulse(aircraft) && aircraft.IsServer)
                jamTarget.Jam(new Unit.JamEventArgs
                {
                    jammingUnit = aircraft,
                    jamAmount = WingTuning.JamTargetAmount
                });
        }

        /// <summary>
        /// Retire a finished Splash 'Em without moving the aircraft. The directive falls
        /// back to Formation, which resolves to the state already running — so the wingman
        /// keeps flying the slot it is in, and the only thing that changes is that it stops
        /// shooting.
        /// </summary>
        private void FinishSplash()
        {
            member.ClearAssignedTarget();
            member.Complete(WingOrder.Formation);
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
            member.Complete(WingOrder.ReturnToBase);
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
