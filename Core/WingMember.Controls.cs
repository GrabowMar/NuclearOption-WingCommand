using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingMember
    {
        /// <summary>
        /// Put the resolved behaviour on the aircraft. The only caller of
        /// <c>Pilot.SwitchState</c> (through <see cref="SwitchTo"/>) for a commandable
        /// wingman, which is what makes the
        /// behaviour graph describable at all.
        /// </summary>
        private void EnterBehaviour(string behaviourId)
        {
            // A surface member takes one behaviour whatever the arbiter picked. Every case
            // below steers through the autopilot it does not have, and the bands above Task
            // - a missile break, a deck hold, a leash recall - would each route it into one.
            // Where it should actually go is published through WingSurface, which reads the
            // same directive the cases below read.
            if (IsSurface)
            {
                if (WingBehaviourCatalog.TryEnter(this, WingBehaviours.Surface)) return;

                // A removed surface extension has no aircraft autopilot fallback.
                // Stop only the pilot state we installed; native/other-owner state stays
                // untouched. Pilot.SwitchState(null) is the game's own idle transition.
                if (enteredState != null && ReferenceEquals(Pilot.currentState, enteredState))
                    Pilot.SwitchState(null);
                enteredState = null;
                WarnSurfaceUnhandled();
                return;
            }

            switch (behaviourId)
            {
                case WingBehaviours.Held:
                    EnterHeld();
                    return;

                case WingBehaviours.MissileBreak:
                    TacticalCoordinator.Release(Aircraft);
                    SwitchTo(defensiveState);
                    return;

                case WingBehaviours.DeckHold:
                    EnterDeckHold();
                    return;

                case WingBehaviours.TerrainAbort:
                    TacticalCoordinator.Release(Aircraft);
                    SwitchTo(terrainAbortState);
                    return;

                case WingBehaviours.Rejoin:
                    // Unconditional, unlike the Formation task above: this behaviour exists
                    // precisely because the wingman is a long way out, so it wants the boost
                    // whether or not it was already nominally holding a slot.
                    WingComms.Say(this, WingComms.Call.Rejoining);
                    SwitchTo(formationState);
                    formationState.BoostRejoin(0f);
                    return;

                case WingBehaviours.Task:
                    EnterTask();
                    return;

                default:
                    // A third-party behaviour id. Registered states are looked up here; an
                    // unknown one falls back to the standing order rather than leaving the
                    // aircraft in whatever state it happened to be flying.
                    if (WingBehaviourCatalog.TryEnter(this, behaviourId)) return;
                    // A missing/faulted extension cannot keep winning while its fallback
                    // silently flies a different task or ignores later payload changes.
                    // A failed factory may have installed its own successor. Keep that
                    // reflex eligible for a fresh attempt on the next tick.
                    brain.FallBackToTask(Time.timeSinceLevelLoad,
                        rejectUnavailable: !WingBehaviourCatalog.IsAvailable(behaviourId));
                    EnterTask();
                    return;
            }
        }

        /// <summary>
        /// Give the airframe back to whatever the game would be flying.
        ///
        /// The contract for <see cref="WingBehaviours.Held"/> is "hands off entirely", and
        /// this used to implement it by returning without doing anything — correct only
        /// because the one reflex producing it is the delivery lockout, whose aircraft was
        /// still under the stock taxi AI and had never been taken over in the first place.
        /// Any other reflex resolving to Held got the mod's own formation or attack state
        /// still flying the aircraft while the log said it had been released; a third-party
        /// one, which the catalog explicitly invites, would have hit exactly that.
        ///
        /// A delivery still on the apron is left strictly alone — switching a parked pilot
        /// into a combat state is the one thing worse than not handing off.
        /// </summary>
        private void EnterHeld()
        {
            if (deliveryPending) return;

            TacticalCoordinator.Release(Aircraft);
            SwitchToCombat();
        }

        /// <summary>
        /// Orbit overhead while the leader is on the runway, or while there is no leader to
        /// form on at all. The standing directive is left alone - it used to be overwritten
        /// with an OrbitHere order, which is why the panel showed an order the player had
        /// never given.
        /// </summary>
        private void EnterDeckHold()
        {
            Aircraft leader = Leader;
            GlobalPosition anchor = leader != null
                ? leader.GlobalPosition()
                : Aircraft.GlobalPosition();

            // Tracking, not captured: this behaviour is entered once, and a leader that
            // lands at one end of a runway then taxis to a hangar would otherwise leave the
            // wing circling the touchdown point. With no leader at all there is nothing to
            // track, so the aircraft holds where it is.
            orbitState.SetAnchor(anchor, WingTuning.OrbitRadius, trackLeader: leader != null);
            SwitchTo(orbitState);
        }

        /// <summary>Fly the standing order. The old Apply switch, unchanged in substance.</summary>
        private void EnterTask()
        {
            switch (Directive.Order)
            {
                case WingOrder.Formation:
                    // Boost only on a genuine arrival. Retiring a Splash 'Em or a Jam order
                    // lands here on an aircraft that is already flying its slot, and it has
                    // nothing to hurry back to.
                    if (SwitchTo(formationState))
                        formationState.BoostRejoin(Slot * WingTuning.RejoinStagger);
                    break;

                case WingOrder.Engage:
                    SwitchToCombat();
                    break;

                case WingOrder.ReturnToBase:
                    SwitchToLanding();
                    break;

                case WingOrder.FallBack:
                    SwitchTo(fallBackState);
                    break;

                case WingOrder.OrbitHere:
                    EnterOrbit(Directive);
                    break;

                case WingOrder.DeliverCargo:
                    EnterCargoRun(Directive);
                    break;

                case WingOrder.LandHere:
                    EnterLanding(Directive);
                    break;

                case WingOrder.MoveToPoint:
                case WingOrder.SeekAndDestroy:
                    EnterWaypoint(Directive);
                    break;

                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                    // Splash 'Em used to hold the slot and shoot from there. That works for
                    // a fighter with a gun or a missile already on the nose; a bomber in
                    // formation is looking at the leader, not the target, so ShotIsValid
                    // refused every pickle and FinishSplash sent it "back" to Form Up
                    // without ever firing. An expend order is a run-in.
                    EnterAttack(Directive);
                    break;

                case WingOrder.JamTarget:
                    EnterSlotTask();
                    break;

                case WingOrder.Maneuver:
                    maneuverState.SetManeuver(Directive.Maneuver);
                    SwitchTo(maneuverState);
                    break;
            }
        }

        /// <summary>
        /// Which extra job this wingman is working from its slot. Read by
        /// <see cref="FormationFlyState"/> in place of the order itself, so one state stops
        /// having to infer which of its three behaviours it is supposed to be running.
        /// </summary>
        public SlotTask SlotTask
        {
            get
            {
                if (AssignedTarget == null || AssignedTarget.disabled) return SlotTask.None;
                if (Order == WingOrder.JamTarget) return SlotTask.Jam;
                return SlotTask.None;
            }
        }

        /// <summary>Hold over the named point, or over the leader when none was given.</summary>
        private void EnterOrbit(WingDirective directive)
        {
            Aircraft leader = Leader;
            GlobalPosition anchor = directive.HasPoint
                ? directive.Point
                : leader != null
                    ? leader.GlobalPosition()
                    : Aircraft.GlobalPosition();

            orbitState.SetAnchor(anchor, WingTuning.OrbitRadius);
            SwitchTo(orbitState);
        }

        /// <summary>
        /// Two routes, and the difference is whether the player named a place.
        ///
        /// With a drop point, CargoRunState flies there and releases — the same shape as
        /// Hold and Land, and available to any airframe carrying a load rather than only to
        /// helicopters.
        ///
        /// Without one, the stock transport state configures itself in EnterState — nearest
        /// airbase, nearest known ground enemy, landing zone search — so it remains a
        /// complete supply-run behaviour for the cost of a state switch, and is what the
        /// order has always done.
        ///
        /// Neither reports back on its own. CheckCargoRun watches the cargo station itself,
        /// which is the only ground truth available, and either calls the delivery or gives
        /// the airframe back.
        /// </summary>
        private void EnterCargoRun(WingDirective directive)
        {
            cargoProgress.Reset(CargoAmmo, Time.timeSinceLevelLoad);

            if (directive.HasPoint)
            {
                cargoRunState.SetDestination(directive.Point);
                SwitchTo(cargoRunState);
                return;
            }

            if (Pilot.AIHeloTransportState != null)
            {
                SwitchTo(Pilot.AIHeloTransportState);
                return;
            }

            // A fixed-wing transport has no stock supply route to fall back on, so say which
            // half of the order is missing rather than silently doing nothing with a load
            // aboard.
            WingCommandManager.Instance?.Toast(
                Name + " needs a drop point - it has no standard supply route");

            // Complete, not Apply: this runs inside EnterTask, which runs inside
            // EnterBehaviour, which runs inside Resolve. Apply would re-enter Resolve from
            // the middle of itself.
            Complete(WingOrder.Formation);
        }

        private void EnterLanding(WingDirective directive)
        {
            if (directive.HasPoint) landState.SetDestination(directive.Point);
            else landState.ClearDestination();
            SwitchTo(landState);
        }

        private void EnterWaypoint(WingDirective directive)
        {
            if (!directive.HasPoint)
            {
                // See EnterCargoRun: Apply here would recurse into Resolve.
                Complete(WingOrder.Formation);
                return;
            }

            waypointState.SetDestination(directive.Point);
            SwitchTo(waypointState);
        }

        /// <summary>
        /// Reached only if something re-applies a standing attack order. AttackTarget is the
        /// normal entry point and sets the target first.
        /// </summary>
        private void EnterAttack(WingDirective directive)
        {
            if (AssignedTarget != null && !AssignedTarget.disabled)
            {
                SwitchTo(attackState);
            }
            else
            {
                // The target died. Retire the order rather than flying formation under a
                // standing Attack directive, which would still read as explicit weapons
                // authority to the engagement code.
                Complete(WingOrder.Formation);
            }
        }

        /// <summary>
        /// Jam Target: hold the slot and work the designated unit from where we are.
        /// Splash 'Em used to share this path and never pickled a bomber; it now flies an
        /// attack run. FormationFlyState still reads <see cref="SlotTask"/> for jam.
        ///
        /// No rejoin boost. The wingman is already in its slot, so hurrying it back to a
        /// place it has not left only produced a visible surge every time a target was
        /// designated.
        /// </summary>
        private void EnterSlotTask()
        {
            if (AssignedTarget != null && !AssignedTarget.disabled)
            {
                SwitchTo(formationState);
            }
            else
            {
                Complete(WingOrder.Formation);
            }
        }

        private BehaviourStateCache<Aircraft, PilotBaseState> extraBehaviours;

        /// <summary>
        /// The pilot state for a third-party behaviour on this wingman, built on first use
        /// and cached for this registration's lifetime. Replacing or re-registering an ID
        /// builds a fresh state without sharing controller memory between aircraft.
        /// </summary>
        internal PilotBaseState CachedBehaviour(string behaviourId,
            BehaviourFactoryRegistry<Aircraft, PilotBaseState>.Registration registration)
        {
            extraBehaviours ??= new BehaviourStateCache<Aircraft, PilotBaseState>();
            return extraBehaviours.GetOrCreate(behaviourId, registration, Aircraft);
        }

        internal bool HasCurrentCachedBehaviour(string behaviourId,
            BehaviourFactoryRegistry<Aircraft, PilotBaseState>.Registration registration) =>
            extraBehaviours != null ? extraBehaviours.IsCurrent(behaviourId, registration) : registration == null;

        internal bool IsRegisteredBehaviourStale(string behaviourId) =>
            extraBehaviours != null && extraBehaviours.Contains(behaviourId) &&
            !WingBehaviourCatalog.IsCurrent(this, behaviourId);

        internal void AcknowledgeMissingRegisteredBehaviour(string behaviourId)
        {
            extraBehaviours ??= new BehaviourStateCache<Aircraft, PilotBaseState>();
            extraBehaviours.ObserveMissing(behaviourId);
        }

        /// <summary>The pilot state we last put this aircraft into.</summary>
        private PilotBaseState enteredState;

        /// <summary>
        /// Switch, unless the aircraft is already flying this exact state. Returns true when
        /// a switch actually happened.
        ///
        /// The guard matters because a behaviour can be re-entered without changing: the
        /// arbiter re-runs the standing task whenever the directive underneath it changes,
        /// and several orders are flown by the same state. Finishing a Splash 'Em retires
        /// the order to Formation, which is flown by the state already running — and
        /// re-entering it ran <c>EnterState</c> again, resetting the whole leader filter
        /// bank and arming an eight-second wide-open-throttle rejoin boost on an aircraft
        /// that had never left its slot. That surge is exactly what the Splash 'Em work set
        /// out to remove, and it survived two attempts at removing it.
        ///
        /// Compare with the live pilot state: native initialization and delegated states
        /// can transition independently of the last behaviour we entered.
        /// </summary>
        private bool SwitchTo(PilotBaseState state)
        {
            if (state == null) return false;
            if (ReferenceEquals(state, Pilot.currentState))
            {
                enteredState = state;
                if (state is WingPilotState active)
                {
                    if (active.RestartOnOrderChange && active.OrderRevision != directiveSerial)
                    {
                        // Native SwitchState is idempotent on the same instance. A
                        // retasked maneuver/cargo/landing needs fresh phases and timers.
                        Pilot.SwitchStateNew(state);
                        return true;
                    }
                    active.AcceptOrderRevision(directiveSerial);
                }
                return false;
            }

            // The one guard that makes a surface member safe. Every built-in state, and both
            // of the game's own AI combat states, steer through Autopilot.AutoAim - twenty
            // of those call sites are unguarded, and a null autopilot classifies as rotary,
            // so a hull reaching any of them is a NullReferenceException on the first fixed
            // update. Refusing here makes all of them unreachable without touching one.
            if (IsSurface && !enteringRegisteredBehaviour) return false;

            Pilot.SwitchState(state);
            enteredState = state;
            return true;
        }

        // Set only while WingBehaviourCatalog is installing a registered behaviour - the one
        // route a surface member is allowed through, because a registered state is the only
        // kind that was written knowing there is no autopilot.
        private bool enteringRegisteredBehaviour;

        /// <summary>
        /// Install a behaviour that came from <see cref="WingBehaviourCatalog"/>.
        ///
        /// Routed through <see cref="SwitchTo"/> rather than calling <c>Pilot.SwitchState</c>
        /// directly, so a registered behaviour gets the same re-entry protection everything
        /// else does. It previously did not, which meant re-resolving to the same registered
        /// behaviour re-ran its EnterState every pass.
        /// </summary>
        internal bool SwitchToRegistered(PilotBaseState state)
        {
            enteringRegisteredBehaviour = true;
            try { return SwitchTo(state); }
            finally { enteringRegisteredBehaviour = false; }
        }

        private bool surfaceUnhandledReported;

        private void WarnSurfaceUnhandled()
        {
            if (surfaceUnhandledReported) return;
            surfaceUnhandledReported = true;

            Plugin.Logger.LogWarning(
                $"[Wing] {Name} has no autopilot and nothing is registered for " +
                $"'{WingBehaviours.Surface}'. It will hold its position until a plugin " +
                "supplies a surface behaviour.");
        }

        private void SwitchToCombat()
        {
            if (Pilot == null) return;

            if (Pilot.AICombatState != null)
                SwitchTo(Pilot.AICombatState);
            else if (Pilot.AIHeloCombatState != null)
                SwitchTo(Pilot.AIHeloCombatState);
            else
                Plugin.Logger.LogWarning($"[Wing] {Name} has no combat state to return to.");
        }

        /// <summary>
        /// Fly the stock approach home.
        ///
        /// The whole of Return To Base, deliberately. Both stock landing states pick their
        /// own airbase, fly their own pattern and put the aircraft on a runway or a vertical
        /// landing point; a hand-flown approach would be a worse one, and it is the taxi
        /// that follows touchdown — not the approach — that this mod has to intercept.
        /// </summary>
        private void SwitchToLanding()
        {
            if (Pilot == null) return;

            if (Pilot.AILandingState != null)
            {
                // AIPilotLandingState.EnterState searches for a runway synchronously and,
                // finding none it can use, ejects the pilot and clears the pilot state
                // outright. An order to go home must not be a way to destroy the aircraft,
                // so the same query is asked first and the order refused if it fails.
                if (!WingAirfield.HasLandingRunway(Aircraft))
                {
                    WingCommandManager.Instance?.Toast(
                        Name + " has no reachable landing runway - holding station");
                    Plugin.Logger.LogWarning(
                        "[Wing] " + Name + " cannot RTB: no friendly field has a runway it can land on");
                    Complete(WingOrder.Formation);
                    return;
                }
                SwitchTo(Pilot.AILandingState);
                return;
            }

            if (Pilot.AIHeloLandingState != null)
                SwitchTo(Pilot.AIHeloLandingState);
            else
                SwitchToCombat();
        }
    }
}
