using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>One AI aircraft under the player's command, plus the slot it holds.</summary>
    internal class WingMember
    {
        public readonly Aircraft Aircraft;
        public readonly Pilot Pilot;
        public int Slot;

        /// <summary>Distance to the assigned slot, in metres. Diagnostic only.</summary>
        public float SlotError;

        public WingDirective Directive { get; private set; }
        public WingOrder Order => Directive.Order;

        private readonly FormationFlyState formationState;
        private readonly FallBackState fallBackState;
        private readonly OrbitState orbitState;
        private readonly LandInPlaceState landState;
        private readonly WaypointTaskState waypointState;
        private readonly AttackRunState attackState;
        private readonly DefensiveManeuverState defensiveState;

        /// <summary>Flying back to the wing while a standing Engage order is still in force.</summary>
        private bool recalled;
        private readonly float joinedAt;
        private WingRegistry owner;
        private readonly List<GlobalPosition> waypointQueue = new List<GlobalPosition>();

        public WingMember(WingRegistry owner, Aircraft aircraft, Pilot pilot, int slot)
        {
            this.owner = owner;
            Aircraft = aircraft;
            Pilot = pilot;
            Slot = slot;
            formationState = new FormationFlyState(this);
            fallBackState = new FallBackState(this);
            orbitState = new OrbitState(this);
            landState = new LandInPlaceState(this);
            waypointState = new WaypointTaskState(this);
            attackState = new AttackRunState(this);
            defensiveState = new DefensiveManeuverState(this);
            joinedAt = Time.timeSinceLevelLoad;
            Directive = WingDirective.Simple(WingOrder.Formation);
        }

        public Aircraft Leader => owner?.Leader;

        /// <summary>The rest of the wing, for separation steering.</summary>
        public System.Collections.Generic.IReadOnlyList<WingMember> Siblings =>
            owner != null ? owner.Members : null;

        public bool Alive =>
            Aircraft != null && !Aircraft.disabled &&
            Pilot != null && !Pilot.dead && !Pilot.ejected;

        public string Name => Aircraft != null ? Aircraft.unitName : "(gone)";

        public void Apply(WingOrder order) => Apply(WingDirective.Simple(order));

        public void Apply(WingDirective directive)
        {
            TacticalCoordinator.Release(Aircraft);

            if (directive.Order != WingOrder.MoveToPoint)
                waypointQueue.Clear();

            Directive = directive;
            TacticalMapOverlay.Invalidate();
            recalled = false;
            OnLeash = false;

            // A player order received during a missile break is queued as the standing
            // intent. Self-preservation continues until clear, then resumes this exact order.
            if (IsPanicking)
            {
                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"[Panic] {Name} queued {directive.Order} while defensive");
                return;
            }

            switch (directive.Order)
            {
                case WingOrder.Formation:
                    formationState.BoostRejoin(Slot * Plugin.Config2.RejoinStagger.Value);
                    Pilot.SwitchState(formationState);
                    break;

                case WingOrder.Engage:
                    SwitchToCombat();
                    break;

                case WingOrder.ReturnToBase:
                    SwitchToLanding();
                    break;

                case WingOrder.FallBack:
                    Pilot.SwitchState(fallBackState);
                    break;

                case WingOrder.OrbitHere:
                {
                    Aircraft leader = Leader;
                    GlobalPosition anchor = directive.HasPoint
                        ? directive.Point
                        : leader != null
                            ? leader.GlobalPosition()
                            : Aircraft.GlobalPosition();

                    orbitState.SetAnchor(anchor, Plugin.Config2.OrbitRadius.Value);
                    Pilot.SwitchState(orbitState);
                    break;
                }

                case WingOrder.DeliverCargo:
                    // The stock transport state configures itself in EnterState — nearest
                    // airbase, nearest known ground enemy, landing zone search — so this is
                    // a complete supply-run behaviour for the cost of a state switch.
                    Pilot.SwitchState(Pilot.AIHeloTransportState);
                    break;

                case WingOrder.LandHere:
                    if (directive.HasPoint) landState.SetDestination(directive.Point);
                    else landState.ClearDestination();
                    Pilot.SwitchState(landState);
                    break;

                case WingOrder.MoveToPoint:
                    if (!directive.HasPoint)
                    {
                        Apply(WingOrder.Formation);
                        break;
                    }
                    waypointState.SetDestination(directive.Point);
                    Pilot.SwitchState(waypointState);
                    break;

                case WingOrder.Attack:
                    // Reached only if something re-applies a standing attack order.
                    // AttackTarget is the normal entry point and sets the target first.
                    if (AssignedTarget != null && !AssignedTarget.disabled)
                        Pilot.SwitchState(attackState);
                    else
                        Pilot.SwitchState(formationState);
                    break;
            }

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} -> {directive.Order}");
        }

        /// <summary>True when this aircraft can be told to run cargo.</summary>
        public bool CanDeliverCargo
        {
            get
            {
                if (Pilot == null || Pilot.AIHeloTransportState == null) return false;
                if (Aircraft == null || Aircraft.weaponStations == null) return false;

                foreach (WeaponStation s in Aircraft.weaponStations)
                {
                    if (s != null && s.Cargo && s.Ammo > 0) return true;
                }
                return false;
            }
        }

        /// <summary>True when this aircraft can set down where it is.</summary>
        public bool CanLandInPlace => WingRegistry.IsRotary(Aircraft);
        /// <summary>
        /// Give control back to the stock combat AI. Used both for an explicit Engage
        /// order and for automatic breaks (leader lost, mutual support).
        /// </summary>
        public void ReleaseToCombat(string reason)
        {
            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} releasing to combat AI: {reason}");

            Directive = WingDirective.Simple(WingOrder.Engage);
            OnLeash = false;
            IsPanicking = false;
            SwitchToCombat();
        }

        /// <summary>True while this member is off the wing on a leashed engagement.</summary>
        public bool OnLeash { get; private set; }

        /// <summary>A target the player has explicitly assigned, or null.</summary>
        public Unit AssignedTarget => Directive.Target;

        /// <summary>True while a missile warning temporarily owns the flight controls.</summary>
        public bool IsPanicking { get; private set; }

        /// <summary>Fuel remaining, 0-1.</summary>
        public float Fuel => Aircraft != null ? Aircraft.GetFuelLevel() : 0f;

        /// <summary>Rounds/missiles remaining across all stations.</summary>
        public int Ammo
        {
            get
            {
                if (Aircraft == null || Aircraft.weaponStations == null) return 0;

                int total = 0;
                foreach (WeaponStation s in Aircraft.weaponStations)
                {
                    if (s != null && !s.Cargo) total += s.Ammo;
                }
                return total;
            }
        }

        /// <summary>
        /// Order this member onto a specific target.
        ///
        /// An order to attack now flies an attack. It used to set AssignedTarget and hope,
        /// which only worked while the wingman happened to be holding station: AssignedTarget
        /// is read by FormationFlyState, so under an Engage order - where the stock combat AI
        /// is flying - it was ignored entirely. The Pilot.SetPrimaryTarget call that looked
        /// like it bridged the gap was dead code; AIPilotCombatModes never reads it.
        /// </summary>
        public void AttackTarget(Unit target)
        {
            if (target == null) return;
            Apply(WingDirective.Attack(target));
            if (!IsPanicking)
                WingComms.Say(this, WingComms.Call.Engaging, target.unitName);
        }
        public void ClearAssignedTarget() => Directive = Directive.WithoutTarget();

        /// <summary>Issue a tactical-map move, replacing or appending to this member's route.</summary>
        public void IssueWaypoint(GlobalPosition point, bool append)
        {
            if (!Alive) return;
            if (!append) waypointQueue.Clear();
            waypointQueue.Add(point);

            Apply(WingDirective.AtPoint(WingOrder.MoveToPoint, waypointQueue[0]));
        }

        public int WaypointCount => waypointQueue.Count;

        /// <summary>
        /// The route this member is flying, current leg first. Read by the tactical map to
        /// draw the queue; the list is the live queue, so callers must not hold on to it
        /// across a <see cref="CompleteWaypoint"/>.
        /// </summary>
        public IReadOnlyList<GlobalPosition> Route => waypointQueue;

        /// <summary>Advance a route, then resolve the wing's ROE at its final endpoint.</summary>
        internal void CompleteWaypoint()
        {
            if (waypointQueue.Count > 0) waypointQueue.RemoveAt(0);

            if (waypointQueue.Count > 0)
            {
                GlobalPosition next = waypointQueue[0];
                Directive = WingDirective.AtPoint(WingOrder.MoveToPoint, next);
                waypointState.SetDestination(next);
                if (!IsPanicking) Pilot.SwitchState(waypointState);
                return;
            }

            // A map move is temporary. Defend and Escort return to formation, where their
            // ROE owns weapons policy; Free transitions into autonomous combat.
            if (RoeRules.Current == WingRoe.Free)
                Apply(WingOrder.Engage);
            else
                Apply(WingOrder.Formation);
        }

        /// <summary>
        /// Send the member home when it can no longer contribute. A wingman with no
        /// weapons or no fuel is just a liability holding station.
        /// </summary>
        public void CheckReserves()
        {
            if (!Alive || !Plugin.Config2.AutoReturnOnEmpty.Value) return;
            if (IsPanicking) return;

            // Orders that are already going somewhere deliberate are not interrupted by a
            // bingo call. A wingman on the deck does not need telling to land, and one
            // mid-cargo-run or mid-retreat has a better reason to be where it is than its
            // fuel state.
            switch (Order)
            {
                case WingOrder.ReturnToBase:
                case WingOrder.LandHere:
                case WingOrder.DeliverCargo:
                case WingOrder.FallBack:
                case WingOrder.MoveToPoint:
                    return;
            }

            // A freshly spawned aircraft can be sampled before its weapon stations have
            // finished initialising, which reads as zero ammunition and would send it
            // straight home the moment it joined.
            if (Time.timeSinceLevelLoad - joinedAt < 10f) return;

            if (Fuel <= Plugin.Config2.BingoFuel.Value)
            {
                WingComms.Say(this, WingComms.Call.Bingo);
                Apply(WingOrder.ReturnToBase);
                return;
            }

            if (Ammo <= 0)
            {
                WingComms.Say(this, WingComms.Call.Winchester);
                Apply(WingOrder.ReturnToBase);
            }
        }

        /// <summary>
        /// Break formation to fight, but stay tethered. Unlike a plain Engage order this
        /// is temporary: <see cref="CheckLeash"/> pulls the member back once the fight
        /// takes it too far from the leader. Follows the Falcon BMS model, where an attack
        /// order means acquire, fire, then rejoin — not leave for good.
        /// </summary>
        public void BreakToEngage(string reason)
        {
            if (IsPanicking || OnLeash || Order != WingOrder.Formation) return;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} breaking to engage: {reason}");

            OnLeash = true;
            Directive = WingDirective.Simple(WingOrder.Engage);
            WingComms.Say(this, WingComms.Call.Breaking);
            SwitchToCombat();
        }

        /// <summary>
        /// Keep a hunting wingman on a tether.
        ///
        /// Engage used to be a one-way handoff to the stock combat AI: the wingman stayed on
        /// the roster but flew off and never came back, which made it indistinguishable from
        /// Disband except in the paperwork. It is now a standing order to hunt *within*
        /// LeashRadius of the leader.
        ///
        /// The two thresholds are deliberate. Recalling at the leash and releasing again at
        /// half of it gives the hysteresis that stops a wingman flip-flopping between
        /// hunting and rejoining every frame it sits on the boundary — with a single
        /// threshold that is exactly what would happen.
        ///
        /// <see cref="OnLeash"/> separates the two callers: an automatic mutual-support
        /// break is temporary and reverts to Formation once it has rejoined, while a
        /// standing Engage order resumes hunting and keeps its order.
        /// </summary>
        public void CheckLeash()
        {
            if (!Alive || IsPanicking) return;
            if (Order != WingOrder.Engage && Order != WingOrder.Attack && !OnLeash) return;

            Aircraft leader = Leader;
            if (leader == null)
            {
                OnLeash = false;
                return;
            }

            float leash = Plugin.Config2.LeashRadius.Value;
            float distanceSq = FastMath.SquareDistance(Aircraft.GlobalPosition(), leader.GlobalPosition());

            if (!recalled)
            {
                if (distanceSq < leash * leash) return;

                if (Plugin.Config2.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"[Wing] {Name} past leash - rejoining");

                WingComms.Say(this, WingComms.Call.Rejoining);

                // An automatic break is over the moment it rejoins; a standing order is not.
                if (OnLeash)
                {
                    OnLeash = false;
                    Apply(WingOrder.Formation);
                    return;
                }

                recalled = true;
                formationState.BoostRejoin(0f);
                Pilot.SwitchState(formationState);
                return;
            }

            // Recalled and on the way back: turn loose again once genuinely close, not the
            // instant the leash is nominally satisfied.
            float release = leash * 0.5f;
            if (distanceSq > release * release) return;

            recalled = false;

            // Resume whatever the standing order actually was. An attack order goes back to
            // its target rather than to free hunting, which is the difference between "go
            // and get that" and "go and find something".
            if (Order == WingOrder.Attack && AssignedTarget != null && !AssignedTarget.disabled)
            {
                WingComms.Say(this, WingComms.Call.Engaging, AssignedTarget.unitName);
                Pilot.SwitchState(attackState);
                return;
            }

            if (Order == WingOrder.Attack)
            {
                // Target died while we were on our way back.
                ClearAssignedTarget();
                Apply(WingOrder.Formation);
                return;
            }

            WingComms.Say(this, WingComms.Call.Engaging);
            SwitchToCombat();
        }

        /// <summary>Enter the temporary defensive interrupt when this aircraft is warned.</summary>
        public void CheckThreats()
        {
            if (!Alive || IsPanicking || !Plugin.Config2.PanicSystem.Value) return;
            if (Aircraft.radarAlt < 5f) return;

            MissileWarning warning = Aircraft.GetMissileWarningSystem();
            if (warning == null || !warning.IsWarning()) return;

            IsPanicking = true;
            TacticalCoordinator.Release(Aircraft);
            Pilot.SwitchState(defensiveState);
        }

        /// <summary>
        /// Called by <see cref="DefensiveManeuverState"/> after the warning stays clear.
        /// The standing order may have changed while defensive, so resolve it at this exact
        /// moment instead of caching a stale pilot state at panic entry.
        /// </summary>
        public void ResumeAfterPanic()
        {
            if (!IsPanicking) return;

            WingDirective resume = Directive;
            IsPanicking = false;

            if (resume.Order == WingOrder.Attack &&
                (resume.Target == null || resume.Target.disabled))
            {
                resume = WingDirective.Simple(WingOrder.Formation);
            }

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Panic] {Name} clear -> {resume.Order}");

            Apply(resume);
        }

        private void SwitchToCombat()
        {
            if (Pilot == null) return;

            if (Pilot.AICombatState != null)
                Pilot.SwitchState(Pilot.AICombatState);
            else if (Pilot.AIHeloCombatState != null)
                Pilot.SwitchState(Pilot.AIHeloCombatState);
            else
                Plugin.Logger.LogWarning($"[Wing] {Name} has no combat state to return to.");
        }

        private void SwitchToLanding()
        {
            if (Pilot == null) return;

            if (Pilot.AILandingState != null)
                Pilot.SwitchState(Pilot.AILandingState);
            else if (Pilot.AIHeloLandingState != null)
                Pilot.SwitchState(Pilot.AIHeloLandingState);
            else
                SwitchToCombat();
        }
    }

    /// <summary>
    /// What a wingman has been told to do - that is, where it flies. What it *shoots* is
    /// the separate question answered by <see cref="WingRoe"/>.
    /// </summary>
    internal enum WingOrder
    {
        Formation,
        Engage,
        ReturnToBase,
        FallBack,
        OrbitHere,
        DeliverCargo,
        LandHere,
        Attack,
        MoveToPoint,
    }
}
