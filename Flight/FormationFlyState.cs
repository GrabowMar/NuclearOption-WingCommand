using UnityEngine;

namespace WingCommand
{
    /// <summary>Native pilot state for formation slots, installed through Pilot.SwitchState. Uses AutoAim
    /// for steering and controls throttle/destination without patching the state machine.</summary>
    internal class FormationFlyState : WingPilotState
    {
        internal override bool RestartOnOrderChange => false;
        private const float EngageInterval = 0.5f;

        // Scale avoidance geometry with slot spacing so the dimensions remain consistent across
        // formations.

        /// <summary>Separation radius relative to the nearest valid slot gap.</summary>
        private const float SeparationSpacings = FormationLayout.MinimumPlanarSeparation;

        /// <summary>Repulsion displacement strength in metres.</summary>
        private const float SeparationStrength = 12f;

        /// <summary>Protected corridor length ahead of the leader, in slot spacings.</summary>
        private const float PathCutSpacings = 3.3f;

        /// <summary>Protected corridor half-width in slot spacings.</summary>
        private const float PathCutRadiusSpacings = 1f;

        /// <summary>Path-clearance push strength in slot spacings.</summary>
        private const float PathCutStrengthSpacings = 1.7f;

        /// <summary>Avoidance smoothing duration in seconds, preventing target steps.</summary>
        private const float AvoidanceSmoothing = 0.4f;

        /// <summary>Slot error threshold for inability to hold formation, in metres.</summary>
        private const float UnableDistance = 3000f;

        /// <summary>Persistent failure duration before abandoning station keeping, in seconds.</summary>
        private const float UnableSeconds = 20f;

        /// <summary>Delegate station weapons to the same engagement helper used by orbit.</summary>
        private readonly SlotEngagement engagement = new SlotEngagement(EngageInterval);
        private float rejoinBoostUntil;
        private float rejoinHoldUntil;
        private float lastKeepUpDistance = float.MaxValue;
        private float losingGroundSince;
        private Vector3 smoothedAvoidance;
        private Vector3 smoothedSlotOffset;
        private Vector3 slotVelocity;
        private Aircraft slotVelocityLeader;
        private int collisionThreatId;
        private FixedWingFormation.FlightMemory fixedWingMemory = new FixedWingFormation.FlightMemory();
        private float threatSpacing = 1f;

        // Share and poll nearby-hostile scans for the leader; missile warnings remain immediate.
        private const float NearbyThreatRefreshSeconds = 0.25f;
        private static Aircraft nearbyThreatLeader;
        private static float nextNearbyThreatRefresh;
        private static bool nearbyThreatPresent;
        private Vector3 smoothedSlotLocal;
        private Vector3 slotTransitionVelocity;
        private bool slotLocalReady;
        private float lateralTurnScale = 1f;
        private float trailTurnScale = 1f;

        private const float TurnGeometrySeconds = 0.7f;
        private const float BankSmoothing = 0.45f;
        private RotaryFormation.Mode lastRotaryMode = (RotaryFormation.Mode)(-1);
        private float lastRotaryReport;

        // Adaptive formation state.

        /// <summary>Signed duration of a consistent leader turn for slot mirroring.</summary>
        private float turnPersist;
        private const float TurnMirrorRate = 0.05f;   // Yaw-rate threshold in rad/s.
        private const float TurnMirrorHold = 1.5f;    // Consistent-turn duration before mirroring, in seconds.

        /// <summary>Smoothed aft-spacing scale during leader threat response.</summary>
        private float combatSpread = 1f;
        private bool leaderMissileThreat;
        private const float CombatSpreadBackScale = 1.4f;
        private const float CombatSpreadEaseSeconds = 2f;

        /// <summary>Cached terrain-floor altitude above sea level, stable across origin shifts.</summary>
        private float terrainFloorY = float.MinValue;
        private float nextTerrainProbe;
        private const float TerrainProbeInterval = 0.3f;

        /// <summary>Quantized terrain-height cache shared across nearby members to reduce duplicate
        /// raycasts.</summary>
        private static readonly System.Collections.Generic.Dictionary<(int x, int z), float> terrainFloorCache =
            new System.Collections.Generic.Dictionary<(int x, int z), float>(256);
        private const int MaxTerrainCacheEntries = 1024;

        public static void ResetTerrainCache() => terrainFloorCache.Clear();

        /// <summary>Physics-step counter for mode-scaled geometry updates.</summary>
        private int geometryTick;

        public FormationFlyState(WingMember member) : base(member)
        {
            stateDisplayName = "Formation";
        }

        public Aircraft Leader => member.Leader;

        /// <summary>Low-pass leader track rejects brief control twitches while following sustained
        /// motion.</summary>
        private Vector3 smoothedLeaderDir;
        private Vector3 lastLeaderTrack;

        /// <summary>Horizontal leader track defining the formation frame.</summary>
        private Vector3 flatLeaderTrack = Vector3.forward;

        /// <summary>Filtered heading rate in rad/s; positive turns right.</summary>
        private float leaderTurnRate;

        /// <summary>Filtered speed derivative in m/s² for acceleration feed-forward.</summary>
        private float leaderSpeedRate;

        /// <summary>Filtered leader bank used by slot geometry and roll control.</summary>
        private float leaderBank;
        private float leaderBankRate;
        private Aircraft trackedLeader;



        /// <summary>Previous leader speed for acceleration sampling.</summary>
        private float lastLeaderSpeed;

        /// <summary>Filtered leader throttle and whether it is readable.</summary>
        private float leaderThrottle;
        private bool leaderThrottleKnown;

        /// <summary>Last geometry-update time. Differentiate over actual elapsed time because Performance
        /// mode skips physics ticks.</summary>
        private float lastGeometryTime;

        /// <summary>Leader-track smoothing duration, balancing stick-noise rejection and turn
        /// lag.</summary>
        private const float LeaderTrackSmoothing = 0.35f;

        /// <summary>Heading-rate smoothing duration in seconds.</summary>
        private const float TurnRateSmoothing = 0.20f;

        /// <summary>Heading-rate noise threshold in rad/s, below deliberate turns but above differentiated
        /// filter residue.</summary>
        private const float TurnRateDeadband = 0.006f;

        /// <summary>Maximum credible heading rate in rad/s, used to reject discontinuities.</summary>
        private const float MaxCredibleTurnRate = 1.5f;

        /// <summary>Per-member five-second report timer. A shared static timer would interleave different
        /// aircraft's samples.</summary>
        private bool DueToReport()
        {
            if (!Plugin.Settings.VerboseLogging.Value) return false;
            if (Time.timeSinceLevelLoad - lastReport < 5f) return false;

            lastReport = Time.timeSinceLevelLoad;
            return true;
        }

        private float lastReport;

        /// <summary>Filter leader track and differentiate its horizontal heading for turn rate. Rigidbody
        /// world-y angular velocity mixes roll and pitch into yaw and can create false formation
        /// sway.</summary>
        private LeaderState TrackLeader(Aircraft leader, float dt)
        {
            Vector3 instant = leader.rb != null && leader.rb.velocity.sqrMagnitude > 1f
                ? leader.rb.velocity.normalized
                : leader.transform.forward;

            if (trackedLeader != leader)
            {
                trackedLeader = leader;
                smoothedLeaderDir = Vector3.zero;
                leaderThrottleKnown = false;
                smoothedAvoidance = Vector3.zero;
                turnPersist = 0f;
            }
            ReadLeaderThrottle(leader, dt);

            if (smoothedLeaderDir.sqrMagnitude < 0.5f)
            {
                smoothedLeaderDir = instant;
                lastLeaderTrack = instant;
                flatLeaderTrack = Flatten(instant);
                leaderTurnRate = 0f;
                leaderSpeedRate = 0f;
                leaderBank = FixedWingFormation.BankOf(leader);
                leaderBankRate = 0f;
                lastLeaderSpeed = leader.speed;
                return State();
            }

            float trackResponse = FormationTracking.TrackResponse(
                Vector3.Angle(smoothedLeaderDir, instant), LeaderTrackSmoothing);
            smoothedLeaderDir = Vector3.Slerp(
                smoothedLeaderDir, instant,
                1f - Mathf.Exp(-dt / trackResponse)).normalized;

            // Preserve the last horizontal heading through near-vertical flight.
            Vector3 flat = FormationTracking.HorizontalTrackWeight(
                smoothedLeaderDir.x, smoothedLeaderDir.z) > 0f
                ? Flatten(smoothedLeaderDir) : flatLeaderTrack;

            // Clamp implausible heading discontinuities before they reach slot geometry.
            // Differentiate observed motion before filtering the rate; differentiating the smoothed
            // steering direction delayed turn onset and kept predicting the old turn after reversals.
            float measured = FormationTracking.TrackTurnRate(lastLeaderTrack.x, lastLeaderTrack.z,
                instant.x, instant.z, dt, MaxCredibleTurnRate);
            lastLeaderTrack = instant;
            flatLeaderTrack = flat;

            leaderTurnRate = Mathf.Lerp(
                leaderTurnRate, measured, 1f - Mathf.Exp(-dt / TurnRateSmoothing));

            // Differentiate the same leader speed used by throttle control, then smooth; consumers
            // clamp prediction after discontinuities.
            float rate = (leader.speed - lastLeaderSpeed) / dt;
            lastLeaderSpeed = leader.speed;

            leaderSpeedRate = Mathf.Lerp(
                leaderSpeedRate,
                Mathf.Clamp(rate, -WingTuning.MaxCredibleAccel, WingTuning.MaxCredibleAccel),
                1f - Mathf.Exp(-dt / WingTuning.SpeedRateSmoothing));

            float previousBank = leaderBank;
            float observedBank = FixedWingFormation.BankOf(leader);
            float bankResponse = FormationTracking.BankResponse(observedBank - leaderBank, BankSmoothing);
            leaderBank = FormationTracking.SmoothBank(
                leaderBank, observedBank, bankResponse, dt);
            leaderBankRate = Mathf.Lerp(leaderBankRate,
                Mathf.Clamp(Mathf.DeltaAngle(previousBank, leaderBank) * Mathf.Deg2Rad / dt,
                    -Mathf.PI, Mathf.PI),
                1f - Mathf.Exp(-dt / bankResponse));

            return State();
        }

        /// <summary>Package filtered leader signals for the current flight update.</summary>
        private LeaderState State() =>
            new LeaderState(smoothedLeaderDir, flatLeaderTrack, LeaderTurnRate,
                            leaderSpeedRate, leaderBank, leaderBankRate, lastLeaderSpeed, leaderThrottle,
                            leaderThrottleKnown);

        /// <summary>Briefly smooth leader throttle for immediate acceleration/deceleration anticipation
        /// without propagating AI throttle chatter.</summary>
        private void ReadLeaderThrottle(Aircraft leader, float dt)
        {
            ControlInputs inputs = leader.GetInputs();
            if (inputs == null)
            {
                // Unknown throttle must not be interpreted as idle.
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

        /// <summary>Filtered heading rate with the noise deadband removed.</summary>
        private float LeaderTurnRate =>
            FormationTracking.QuietTurnRate(leaderTurnRate, TurnRateDeadband);

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }


        public override void EnterState(Pilot pilot)
        {
            // Start rejoin in cruise; RotaryFormation engages hover later if needed.
            BeginFlight(pilot);

            slotLocalReady = false;
            slotTransitionVelocity = Vector3.zero;
            slotVelocityLeader = null;
            slotVelocity = Vector3.zero;
            collisionThreatId = 0;
            fixedWingMemory = new FixedWingFormation.FlightMemory();
            lastRotaryMode = (RotaryFormation.Mode)(-1);
            lastRotaryReport = 0f;
            turnPersist = 0f;
            combatSpread = 1f;
            terrainFloorY = float.MinValue;
            nextTerrainProbe = 0f;
            geometryTick = 0;

            // Reset stale track, speed, and throttle filters on re-entry so old samples cannot become
            // false turn or acceleration spikes.
            smoothedLeaderDir = Vector3.zero;
            trackedLeader = null;
            leaderTurnRate = 0f;
            leaderSpeedRate = 0f;
            lastLeaderSpeed = 0f;
            leaderThrottleKnown = false;
            lastGeometryTime = 0f;

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose($"[Formation] {aircraft.unitName} id={aircraft.GetInstanceID()} entering slot {member.Slot}");
        }

        public override void LeaveState()
        {
            // Release only this state's active airbrake demand before the next EnterState; exact zero
            // throttle deploys native brakes.
            if (fixedWingMemory.Airbraking && controlInputs != null && controlInputs.throttle == 0f)
                controlInputs.throttle = 0.01f;
            fixedWingMemory.Airbraking = false;
        }

        public override void UpdateState(Pilot pilot)
        {
        }

        public override void FixedUpdateState(Pilot pilot)
        {
            // Reassert gear retraction because late spawn initialisation can undo EnterState's request.
            if (aircraft != null && aircraft.gearState != LandingGear.GearState.LockedRetracted)
                aircraft.SetGear(deployed: false);

            Aircraft leader = Leader;

            // Wait for arbitration when leader or aircraft is unavailable. Do not use teardown handoff,
            // which would erase standing intent before LeaderLost can preserve it.
            if (leader == null || leader.disabled || aircraft == null || aircraft.disabled)
                return;

            // Sample the current slot job once for this update.
            SlotTask task = member.SlotTask;

            // Retire JamTarget when its target dies, even though SlotTask has already stopped reporting
            // Jam.
            if (member.Order == WingOrder.JamTarget && task != SlotTask.Jam)
            {
                WingComms.Say(member, WingComms.Call.JammingOff);
                CompleteTask(WingOrder.Formation);
                return;
            }

            // Recompute geometry at the fidelity stride, phased by slot. Keep previous commands between
            // updates; manager-driven missile defence is not strided.
            int stride = WingFidelity.GeometryStride;
            if (stride > 1 && (++geometryTick + member.Slot) % stride != 0)
                return;

            // Stride pod jamming too because the native weapon produces networked Unit.Jam updates.
            if (task == SlotTask.Jam) RunJam();

            engagement.Run(member, aircraft, pilot, leader);

            // Use clamped elapsed geometry time for all filters and derivatives, including skipped
            // physics ticks and hitches.
            float now = Time.timeSinceLevelLoad;
            float dt = lastGeometryTime > 0f
                ? Mathf.Clamp(now - lastGeometryTime, 0.0001f, 0.5f)
                : Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            lastGeometryTime = now;

            // Use one filtered leader state for slot frame, prediction, turn compensation, steering,
            // and throttle.
            LeaderState leaderState = TrackLeader(leader, dt);
            float turnRate = leaderState.TurnRate;

            FormationShape shape = WingFormation.Shape;

            // Scale spacing for slower, closer rotary formation flight.
            float spacing = WingFormation.SlotSpacing;
            if (WingRegistry.IsRotary(aircraft))
                spacing *= WingTuning.RotarySpacingScale;

            // Use the larger of ROE spacing and reactive widening, never their product. Always evaluate
            // threat spacing because it also updates the combat-spread warning latch.
            float roeScale = CombatFacade.Roe.SpacingScale(CombatFacade.Roe.Current);
            float threatScale = ThreatSpacingScale(leader, dt);
            spacing *= threatScale > 1.001f ? Mathf.Max(roeScale, threatScale) : roeScale;
            spacing *= FormationSolver.SharedFlightSpacing(member.Siblings, leader);

            EaseSlotLocal(shape, spacing, turnRate, dt);

            GlobalPosition slotPos = SlotPosition(leader, leaderState, spacing, dt);

            Vector3 toSlot = slotPos - aircraft.GlobalPosition();
            float distance = toSlot.magnitude;

            member.SlotError = distance;
            CheckAbleToKeepUp(leader, distance);

            // Share geometry and avoidance, then dispatch to the correct native plane or rotary AutoAim
            // overload.
            if (aircraft.autopilot is AutopilotPlane)
            {
                if (fixedWingMemory.Leader != leader)
                    fixedWingMemory = new FixedWingFormation.FlightMemory { Leader = leader };
                FixedWingFormation.Fly(
                    aircraft, leader, controlInputs,
                    slotPos, toSlot, distance, spacing,
                    new FixedWingFormation.Rejoin(rejoinHoldUntil, rejoinBoostUntil),
                    leaderState, DueToReport(), slotVelocity,
                    member.Siblings, member, fixedWingMemory, dt,
                    out Aircraft collisionThreat, out float predictedMiss);
                int threatId = collisionThreat != null ? collisionThreat.GetInstanceID() : 0;
                if (threatId != collisionThreatId)
                {
                    Plugin.LogVerbose("[Formation] " + aircraft.unitName + " id=" + aircraft.GetInstanceID() +
                        (threatId == 0 ? " collision avoidance clear; resuming slot" :
                        " collision avoidance priority: neighbor=" + threatId + " predicted miss=" + predictedMiss.ToString("F0") + " m"));
                    collisionThreatId = threatId;
                }
            }
            else
            {
                RotaryFormation.Mode mode = RotaryFormation.Fly(
                    aircraft, leader, slotPos, toSlot, distance, spacing,
                    lastRotaryMode, leaderState, out float horizontalError);

                ReportRotaryMode(mode, distance, horizontalError);
            }
        }

        /// <summary>Ease local shape changes for turn compression, threat spacing, and side mirroring
        /// before transforming them into world space.</summary>
        private void EaseSlotLocal(FormationShape shape, float spacing, float turnRate, float dt)
        {
            // Compress lateral and extend aft spacing during turns to reduce inner/outer speed
            // differences.
            float turn = Mathf.Clamp01(Mathf.Abs(turnRate) / 0.18f);
            float geometryBlend = 1f - Mathf.Exp(-dt / TurnGeometrySeconds);
            lateralTurnScale = Mathf.Lerp(lateralTurnScale, Mathf.Lerp(1f, FormationLayout.TurnLateralScale, turn), geometryBlend);
            trailTurnScale = Mathf.Lerp(trailTurnScale, Mathf.Lerp(1f, FormationLayout.TurnBackScale, turn), geometryBlend);

            int mirrorSign = TurnMirrorSign(turnRate, dt);

            // Ease slots aft under leader threat so the whole formation does not chase a sudden
            // position jump.
            float combatSpreadTarget =
                WingFidelity.SmartFormation && leaderMissileThreat
                    ? CombatSpreadBackScale : 1f;
            combatSpread = Mathf.Lerp(combatSpread, combatSpreadTarget,
                1f - Mathf.Exp(-dt / CombatSpreadEaseSeconds));

            Vector3 desiredSlotLocal = FormationSolver.SlotCoordinates(
                member.Slot, shape, spacing, WingTuning.SlotStack,
                lateralTurnScale, trailTurnScale);

            // Move eligible asymmetric shapes to the outside of sustained turns. Smooth the crossing
            // and retain separation/path avoidance; symmetric shapes stay unchanged.
            if (CombatFacade.Roe.Current != WingRoe.Hold && WingFidelity.SmartFormation && mirrorSign != 0 &&
                (shape == FormationShape.EchelonRight || shape == FormationShape.EchelonLeft) &&
                (int)Mathf.Sign(desiredSlotLocal.x) == mirrorSign)
            {
                desiredSlotLocal.x = -desiredSlotLocal.x;
            }

            // Negative local Z is aft; scaling lengthens trail without reversing it.
            desiredSlotLocal.z *= combatSpread;

            if (!slotLocalReady)
            {
                smoothedSlotLocal = desiredSlotLocal;
                slotTransitionVelocity = Vector3.zero;
                slotLocalReady = true;
            }
            else
            {
                // Smooth shape changes in leader-local space while retaining immediate rotation with
                // its heading.
                smoothedSlotLocal = Vector3.SmoothDamp(
                    smoothedSlotLocal, desiredSlotLocal, ref slotTransitionVelocity,
                    WingTuning.ShapeTransitionSeconds, WingTuning.ShapeTransitionSpeed, dt);
            }
        }

        /// <summary>Transform the continuous local slot into the filtered flight frame, then apply
        /// avoidance and terrain clearance.</summary>
        private GlobalPosition SlotPosition(Aircraft leader, LeaderState leaderState,
                                            float spacing, float dt)
        {
            // Use leader travel direction rather than nose yaw for the 3D slot frame. Apply bank only
            // where terrain clearance permits.
            Vector3 desiredOffset = FormationSolver.WorldOffset(
                leaderState.Track, smoothedSlotLocal, FormationBank(leaderState),
                velocityPlane: true);

            if (slotVelocityLeader != leader)
            {
                smoothedSlotOffset = desiredOffset;
                slotVelocity = Vector3.zero;
                slotVelocityLeader = leader;
            }
            else
            {
                // Track a bounded continuous slot curve and its derivative so bank changes cannot
                // create position and velocity spikes.
                float speedLimit = WingTuning.SlotVelocityLimit / Mathf.Sqrt(3f);
                FormationTracking.DampedAxis(smoothedSlotOffset.x, slotVelocity.x, desiredOffset.x,
                    FormationTracking.SlotResponseSeconds, speedLimit, dt,
                    out smoothedSlotOffset.x, out slotVelocity.x);
                FormationTracking.DampedAxis(smoothedSlotOffset.y, slotVelocity.y, desiredOffset.y,
                    FormationTracking.SlotResponseSeconds, speedLimit, dt,
                    out smoothedSlotOffset.y, out slotVelocity.y);
                FormationTracking.DampedAxis(smoothedSlotOffset.z, slotVelocity.z, desiredOffset.z,
                    FormationTracking.SlotResponseSeconds, speedLimit, dt,
                    out smoothedSlotOffset.z, out slotVelocity.z);
            }
            // Do not add climb velocity again to slot position; steering handles climb and throttle
            // owns along-track prediction.
            GlobalPosition slotPos = leader.GlobalPosition() + smoothedSlotOffset;

            // Scale separation and leader-path protection with actual spacing so rotary slots are not
            // permanently inside their own avoidance radius.
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

            // Ease threshold-driven avoidance to avoid stepping the autopilot destination.
            smoothedAvoidance = Vector3.Lerp(
                smoothedAvoidance, avoidance,
                1f - Mathf.Exp(-dt / AvoidanceSmoothing));

            slotPos += smoothedAvoidance;
            slotPos = ApplyTerrainFloor(slotPos);

            return slotPos;
        }

        /// <summary>Common fixed-wing slot bank, reduced near terrain.</summary>
        private float FormationBank(LeaderState leaderState)
        {
            Aircraft leader = Leader;
            if (WingRegistry.IsRotary(aircraft) || leader == null) return 0f;
            // Use one bank for all slots; per-member scaling can make neighbouring targets intersect.
            Vector3 footprint = Vector3.zero;
            FormationSolver.IncludeBankFootprint(ref footprint, smoothedSlotLocal);
            float fallbackSpacing = WingFormation.SlotSpacing * CombatFacade.Roe.SpacingScale(CombatFacade.Roe.Current) *
                FormationSolver.SharedFlightSpacing(member.Siblings, leader);
            if (member.Siblings != null)
                foreach (WingMember wingman in member.Siblings)
                {
                    if (wingman == null || !wingman.Alive || wingman.DeliveryPending ||
                        wingman.Leader != leader || WingRegistry.IsRotary(wingman.Aircraft)) continue;
                    var formation = wingman.Pilot != null
                        ? wingman.Pilot.currentState as FormationFlyState : null;
                    if (formation != null && formation.slotLocalReady)
                    {
                        // Include actual transitioning slots, not just nominal shape bounds.
                        FormationSolver.IncludeBankFootprint(ref footprint, formation.smoothedSlotLocal);
                    }
                    else
                    {
                        FormationSolver.IncludeBankFootprint(ref footprint, FormationSolver.SlotCoordinates(
                            wingman.Slot, WingFormation.Shape, fallbackSpacing, WingTuning.SlotStack));
                        FormationSolver.IncludeBankFootprint(ref footprint, FormationSolver.SlotCoordinates(
                            wingman.Slot, WingFormation.Shape, fallbackSpacing, WingTuning.SlotStack,
                            FormationLayout.TurnLateralScale, FormationLayout.TurnBackScale));
                    }
                }
            float requested = FormationCollision.SlotBank(leaderState.Bank) *
                Mathf.Clamp01(leader.radarAlt / 150f);
            return FormationCollision.TerrainBank(requested, leader.radarAlt, WingFidelity.TerrainClearance,
                footprint.x, footprint.y, footprint.z, leaderState.Track.y);
        }

        /// <summary>Report rotary regime changes and periodic slot errors for flight diagnosis.</summary>
        private void ReportRotaryMode(RotaryFormation.Mode mode, float distance, float horizontalError)
        {
            bool changed = mode != lastRotaryMode;
            // Update mode memory before logging gates; hover/cruise hysteresis must work with
            // diagnostics disabled.
            lastRotaryMode = mode;

            if (!Plugin.Settings.VerboseLogging.Value) return;

            bool due = Time.timeSinceLevelLoad - lastRotaryReport > 5f;
            if (!changed && !due) return;

            lastRotaryReport = Time.timeSinceLevelLoad;

            Aircraft leader = Leader;
            Plugin.LogVerbose(
                $"[Rotary] {aircraft.unitName} id={aircraft.GetInstanceID()} slot {member.Slot}: {mode}, " +
                $"leaderId={(leader != null ? leader.GetInstanceID() : 0)}, " +
                $"error {distance:F0} m (flat {horizontalError:F0}), " +
                $"own speed {aircraft.speed:F0}, " +
                $"leader {(leader != null ? leader.speed : 0f):F0} m/s, " +
                $"alt {aircraft.radarAlt:F0} m");
        }

        /// <summary>Return sustained turn sign after TurnMirrorHold, otherwise zero. Decay persistence
        /// when turns ease so brief jinks do not mirror the formation.</summary>
        private int TurnMirrorSign(float turnRate, float dt)
        {
            if (Mathf.Abs(turnRate) > TurnMirrorRate)
            {
                // Reset persistence on turn reversal; Mathf.Sign returns exact unit signs.
                if (Mathf.Sign(turnRate) != Mathf.Sign(turnPersist)) turnPersist = 0f;
                turnPersist += Mathf.Sign(turnRate) * dt;
            }
            else
            {
                turnPersist = Mathf.MoveTowards(turnPersist, 0f, dt);
            }

            return Mathf.Abs(turnPersist) >= TurnMirrorHold ? (int)Mathf.Sign(turnPersist) : 0;
        }

        /// <summary>Periodically raise slots above rising terrain, including low elements beneath a safely
        /// cleared leader.</summary>
        private GlobalPosition ApplyTerrainFloor(GlobalPosition slotPos)
        {
            float clearance = WingFidelity.TerrainClearance;
            if (clearance <= 0f) return slotPos;

            Vector3 local = slotPos.ToLocalPosition();

            if (Time.timeSinceLevelLoad >= nextTerrainProbe)
            {
                nextTerrainProbe = Time.timeSinceLevelLoad +
                                   WingFidelity.Interval(TerrainProbeInterval);

                // Use global coordinate pairs as keys; hashes can collide and local cells shift with
                // the floating origin.
                var key = (Mathf.RoundToInt(slotPos.x * 0.05f), Mathf.RoundToInt(slotPos.z * 0.05f));

                if (!terrainFloorCache.TryGetValue(key, out float ground))
                {
                    ground = 0f;
                    if (Physics.Raycast(new Vector3(local.x, Datum.LocalSeaY + 3000f, local.z),
                                        Vector3.down, out RaycastHit hit, 6000f,
                                        PhysicsLayers.StaticsMask))
                    {
                        ground = Mathf.Max(ground, hit.point.y - Datum.LocalSeaY);
                    }

                    if (terrainFloorCache.Count >= MaxTerrainCacheEntries)
                    {
                        terrainFloorCache.Clear();
                    }
                    terrainFloorCache[key] = ground;
                }

                terrainFloorY = ground + clearance;
            }

            slotPos.y = Mathf.Max(slotPos.y, terrainFloorY);
            return slotPos;
        }

        /// <summary>Smoothly widen under threat and close when clear so slot targets remain
        /// continuous.</summary>
        private float ThreatSpacingScale(Aircraft leader, float dt)
        {
            // Sample the leader warning once for both smart-formation reactions; skip it when neither
            // uses it.
            if (WingFidelity.SmartFormation)
            {
                MissileWarning leaderWarning = leader.GetMissileWarningSystem();
                leaderMissileThreat = leaderWarning != null && leaderWarning.IsWarning();
            }

            // Disable reactive widening with scale 1 outside Smart formation.
            float scale = WingFidelity.SmartFormation ? WingTuning.ThreatWidenScale : 1f;
            float target = 1f;

            if (scale > 1.001f)
            {
                MissileWarning ownWarning = aircraft != null
                    ? aircraft.GetMissileWarningSystem()
                    : null;

                bool threatened = leaderMissileThreat ||
                    (ownWarning != null && ownWarning.IsWarning());

                // Nearby hostile contacts can widen the formation before missile warning; ROE alone is
                // not a threat cue.
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
                nextNearbyThreatRefresh = now + WingFidelity.Interval(NearbyThreatRefreshSeconds);
                nearbyThreatPresent = CombatFacade.Weapons.NearestThreatTo(leader, 8000f) != null;
            }

            return nearbyThreatPresent;
        }

        /// <summary>Operate the fitted native jammer pod at the designation while holding station; the pod
        /// owns range, power, and network updates.</summary>
        private void RunJam()
        {
            Unit jamTarget = member.AssignedTarget;
            if (jamTarget == null) return;
            CombatFacade.Weapons.EngageJammer(aircraft, pilot, jamTarget);
        }

        /// <summary>Report unable and return home after sustained, growing separation caused by
        /// insufficient airframe performance.</summary>
        private void CheckAbleToKeepUp(Aircraft leader, float distance)
        {
            // Require a real speed-capability deficit; distant same-type members may still be closing
            // successfully.
            float mine = aircraft.GetAircraftParameters().maxSpeed;
            float theirs = leader.GetAircraftParameters().maxSpeed;
            if (mine >= theirs * 0.9f) return;

            float threshold = UnableDistance;

            // Reset failure timing when close or not meaningfully falling back; tolerate small slot
            // shifts during manoeuvres.
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

            Plugin.LogVerbose(
                $"[Wing] {aircraft.unitName} cannot hold station " +
                $"({distance:F0} m out, max speed {mine:F0} vs leader {theirs:F0}) - returning to base");

            WingComms.Say(member, WingComms.Call.Unable);
            CompleteTask(WingOrder.ReturnToBase);
        }

        /// <summary>Start a brief rejoin boost after an optional delay to stagger slot arrivals.</summary>
        public void BoostRejoin(float delay = 0f)
        {
            rejoinHoldUntil = Time.timeSinceLevelLoad + delay;
            rejoinBoostUntil = rejoinHoldUntil + 8f;
        }
    }
}
