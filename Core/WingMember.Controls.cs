using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingMember
    {
        /// <summary>Apply the resolved behaviour through the shared state-switch path for commandable
        /// members.</summary>
        private void EnterBehaviour(string behaviourId)
        {
            // The new behaviour owns targeting. Shots already in flight keep their firing slots.
            TacticalCoordinator.ReleaseSelection(Aircraft);

            // Surface members require their registered behaviour because built-ins assume an autopilot.
            // WingSurface publishes their directive destination.
            if (IsSurface)
            {
                if (WingBehaviourCatalog.TryEnter(this, WingBehaviours.Surface)) return;

                // When a surface extension disappears, idle only the state we installed; preserve
                // native or other-owner control.
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
                    SwitchTo(defensiveState);
                    return;

                case WingBehaviours.DeckHold:
                    EnterDeckHold();
                    return;

                case WingBehaviours.TerrainAbort:
                    SwitchTo(terrainAbortState);
                    return;

                case WingBehaviours.Rejoin:
                    // Leash recall needs rejoin boost even if formation was already active.
                    WingComms.Say(this, WingComms.Call.Rejoining);
                    SwitchTo(formationState);
                    formationState.BoostRejoin(0f);
                    return;

                case WingBehaviours.Task:
                    EnterTask();
                    return;

                default:
                    // Resolve extension IDs through the catalog; fall back to the standing task if
                    // unavailable.
                    if (WingBehaviourCatalog.TryEnter(this, behaviourId)) return;
                    // Reflect extension failure in the brain's owner. Retry next tick if the factory
                    // installed a successor before failing.
                    brain.FallBackToTask(Time.timeSinceLevelLoad,
                        rejectUnavailable: !WingBehaviourCatalog.IsAvailable(behaviourId));
                    EnterTask();
                    return;
            }
        }

        /// <summary>Release mod flight control to native AI. Leave pending apron deliveries untouched;
        /// switching them to combat would interrupt taxi.</summary>
        private void EnterHeld()
        {
            if (deliveryPending) return;

            SwitchToCombat();
        }

        /// <summary>Orbit above a grounded or missing leader without changing the standing
        /// directive.</summary>
        private void EnterDeckHold()
        {
            Aircraft leader = Leader;
            GlobalPosition anchor = leader != null
                ? leader.GlobalPosition()
                : Aircraft.GlobalPosition();

            // Track a taxiing leader instead of freezing its touchdown position; without a leader, hold
            // here.
            orbitState.SetAnchor(anchor, WingTuning.OrbitRadius, trackLeader: leader != null);
            SwitchTo(orbitState);
        }

        /// <summary>Enter the state for the standing directive.</summary>
        private void EnterTask()
        {
            switch (Directive.Order)
            {
                case WingOrder.Formation:
                    // Boost only after a real transition into formation, not a payload change while
                    // already in the slot.
                    bool recovered = enteredState is DefensiveManeuverState || enteredState is TerrainAbortState;
                    if (SwitchTo(formationState))
                        formationState.BoostRejoin(recovered ? 0f : Slot * WingTuning.RejoinStagger);
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
                case WingOrder.StandDown:
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
                    // Splash needs an attack run so bombers can reach a valid release geometry.
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

        /// <summary>Additional station-keeping job, read by FormationFlyState independently of the
        /// standing order.</summary>
        public SlotTask SlotTask
        {
            get
            {
                if (AssignedTarget == null || AssignedTarget.disabled) return SlotTask.None;
                if (Order == WingOrder.JamTarget) return SlotTask.Jam;
                return SlotTask.None;
            }
        }

        /// <summary>Orbit the directive point, or the leader if no point was supplied.</summary>
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

        /// <summary>With a point, CargoRunState flies and drops any loaded airframe. Without one, use
        /// native transport's supply-route search. CheckCargoRun confirms delivery from ammunition changes
        /// and handles timeout.</summary>
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

            // Report the missing drop point when fixed-wing transport has no native supply-route
            // fallback.
            WingCommandManager.Instance?.Toast(
                Name + " needs a drop point - it has no standard supply route");

            // Complete defers resolution; Apply would recurse through EnterTask into Resolve.
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
                // Defer resolution here to avoid re-entering Resolve.
                Complete(WingOrder.Formation);
                return;
            }

            waypointState.SetDestination(directive.Point);
            SwitchTo(waypointState);
        }

        /// <summary>Enter or re-enter the attack state using the directive's target.</summary>
        private void EnterAttack(WingDirective directive)
        {
            if (AssignedTarget != null && !AssignedTarget.disabled)
            {
                SwitchTo(attackState);
            }
            else
            {
                // Retire a dead designation so formation flight cannot retain explicit attack
                // authority.
                Complete(WingOrder.Formation);
            }
        }

        /// <summary>Hold formation and jam the designation via SlotTask. Do not trigger rejoin boost for
        /// an aircraft already in its slot.</summary>
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

        /// <summary>Build extension state on first use and cache it per member and registration lifetime;
        /// replacement creates fresh controller memory.</summary>
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

        /// <summary>Last pilot state installed by this member.</summary>
        private PilotBaseState enteredState;

        /// <summary>Switch only when the live pilot state differs; return true on transition. Re-entering
        /// a shared state resets filters and timers, while native or delegated transitions may change it
        /// independently.</summary>
        private bool SwitchTo(PilotBaseState state)
        {
            if (state == null || Pilot == null || Aircraft == null ||
                !Aircraft.LocalSim || Aircraft.Player != null) return false;
            if (ReferenceEquals(state, Pilot.currentState))
            {
                enteredState = state;
                if (state is WingPilotState active)
                {
                    if (active.RestartOnOrderChange && active.OrderRevision != directiveSerial)
                    {
                        // Restart phases and timers for retasked manoeuvres, cargo runs, and landings;
                        // native SwitchState skips identical instances.
                        Pilot.SwitchStateNew(state);
                        return true;
                    }
                    active.AcceptOrderRevision(directiveSerial);
                }
                return false;
            }

            // Reject built-in flight states for surface members; their steering requires an autopilot.
            if (IsSurface && !enteringRegisteredBehaviour) return false;

            Pilot.SwitchState(state);
            enteredState = state;
            return true;
        }

        // Allow surface entry only while installing a registered behaviour designed for that vehicle.
        private bool enteringRegisteredBehaviour;

        /// <summary>Install catalog state through SwitchTo so repeated resolution preserves re-entry
        /// protection.</summary>
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

        /// <summary>Delegate RTB approach and runway or pad selection to native landing states; recovery
        /// handles touchdown and taxi.</summary>
        private void SwitchToLanding()
        {
            if (Pilot == null) return;

            // Prefer native vertical landing for rotary/hovering airframes, even when a modded pilot
            // also has a runway state.
            if (WingRegistry.IsRotary(Aircraft) && Pilot.AIHeloLandingState != null)
            {
                SwitchTo(Pilot.AIHeloLandingState);
                return;
            }

            if (Pilot.AILandingState != null)
            {
                // Check runway availability before entering native landing, which otherwise ejects
                // immediately when no runway is usable.
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
