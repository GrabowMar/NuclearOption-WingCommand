using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>One AI aircraft under the player's command, plus the slot it holds.</summary>
    internal partial class WingMember
    {
        public readonly Aircraft Aircraft;
        public readonly Pilot Pilot;
        public int Slot;

        /// <summary>Distance to the assigned slot, in metres. Diagnostic only.</summary>
        public float SlotError;
        public WingFlightProfile FlightProfile => brain.Flight;

        private readonly StandingOrder<WingDirective> standingOrder = new StandingOrder<WingDirective>(
            WingDirective.Simple(WingOrder.Formation), (a, b) => a.SameIntentAs(in b));
        public WingDirective Directive => standingOrder.Current;
        public WingOrder Order => Directive.Order;

        /// <summary>
        /// Which of its own weapons this wingman reaches for first.
        ///
        /// Held per member rather than per wing because the useful case is a mixed flight:
        /// two aircraft holding the missiles for the fighters while the third works the
        /// ground with rockets. Read by <see cref="WingWeapons"/> on every station choice.
        /// </summary>
        public WingWeaponPreference WeaponPreference { get; set; } = WingWeaponPreference.Auto;

        /// <summary>What this airframe is carrying, as far as this mod configured it.</summary>
        public WingLoadoutChoice Loadout => WingLoadoutBook.AboardOf(Aircraft);

        /// <summary>
        /// False for an aircraft this mod did not fit — an active mission aircraft the
        /// player assigned arrives with whatever the mission gave it, and the panel says so
        /// rather than claiming it is carrying the standard fit.
        /// </summary>
        public bool LoadoutKnown => WingLoadoutBook.IsKnown(Aircraft);

        /// <summary>
        /// The person flying it, or null before one has been assigned. Distinct from
        /// <see cref="Pilot"/>, which is the game's pilot state machine rather than a
        /// squadron record.
        /// </summary>
        public WingPilot Crew => WingPilotRoster.Of(Aircraft);

        private readonly FormationFlyState formationState;
        private readonly TerrainAbortState terrainAbortState;
        private readonly FallBackState fallBackState;
        private readonly OrbitState orbitState;
        private readonly LandInPlaceState landState;
        private readonly CargoRunState cargoRunState;
        private readonly WaypointTaskState waypointState;
        private readonly AttackRunState attackState;
        private readonly DefensiveManeuverState defensiveState;
        private readonly ManeuverState maneuverState;

        private readonly WingMemberBrain brain = new WingMemberBrain();
        private int directiveSerial => standingOrder.Revision;
        internal int OrderRevision => directiveSerial;

        private readonly float joinedAt;
        private WingRegistry owner;
        private readonly List<GlobalPosition> waypointQueue = new List<GlobalPosition>();
        private bool deliveryPending;

        private readonly CargoProgressTracker cargoProgress = new CargoProgressTracker();
        private float lastIntegrity;
        private bool damageReported;
        private bool criticalDamageReported;

        /// <summary>True while a hangar delivery is still taxiing or waiting to launch.</summary>
        public bool DeliveryPending => deliveryPending;
        public bool RefitPending { get; private set; }

        public void RequestRefit()
        {
            Apply(WingOrder.ReturnToBase);
            RefitPending = true;
        }

        public void CompleteRefit()
        {
            if (!RefitPending || Pilot == null || Pilot.dead || Pilot.ejected) return;
            foreach (FuelTank tank in Aircraft.GetFuelTanks())
                if (tank != null) tank.Refuel(1f);
            Aircraft.NetworkfuelLevel = Aircraft.GetFuelLevel();
            var ammunition = new int[Aircraft.weaponStations.Count];
            for (int i = 0; i < ammunition.Length; i++)
            {
                var station = Aircraft.weaponStations[i];
                ammunition[i] = Mathf.Max(0, station.FullAmmo - station.GetAmmoTotal());
            }
            Aircraft.RpcRearm(new RearmEventArgs { Rearmer = Aircraft, Stations = ammunition });
            WingPilotRoster.NoteSortie(Aircraft);
            SetDirective(WingDirective.Simple(WingOrder.Formation));
            RefitPending = false;
        }

        /// <summary>
        /// A ship or a ground vehicle rather than an aircraft.
        ///
        /// Asked of the autopilot, which is the same question <c>WingShop.IsFlyableAircraft</c>
        /// asks of a prefab and the same one that makes <c>WingRegistry.IsRotary</c> answer
        /// "rotary" for a hull. Nothing here knows which mod supplied the vehicle, and that
        /// is deliberate: an addon nobody has written yet is handled for the same reason.
        /// </summary>
        public bool IsSurface => Aircraft != null && Aircraft.autopilot == null;

        /// <summary>True when player commands may be applied to this member.</summary>
        public bool IsCommandable => Alive && !deliveryPending;

        public WingMember(WingRegistry owner, Aircraft aircraft, Pilot pilot, int slot,
                          bool deliveryPending = false)
        {
            this.owner = owner;
            Aircraft = aircraft;
            Pilot = pilot;
            Slot = slot;
            this.deliveryPending = deliveryPending;
            formationState = new FormationFlyState(this);
            terrainAbortState = new TerrainAbortState(this);
            fallBackState = new FallBackState(this);
            orbitState = new OrbitState(this);
            landState = new LandInPlaceState(this);
            cargoRunState = new CargoRunState(this);
            waypointState = new WaypointTaskState(this);
            attackState = new AttackRunState(this);
            defensiveState = new DefensiveManeuverState(this);
            maneuverState = new ManeuverState(this);
            joinedAt = Time.timeSinceLevelLoad;
            lastIntegrity = Integrity;
        }

        /// <summary>
        /// The aircraft this wingman formates on. Normally the player; when the player has
        /// named a flight lead, every other member forms on that aircraft instead, while the
        /// lead itself still forms on the player. This is the single chokepoint the whole
        /// formation stack reads, so retargeting it here is the entire behavioural core of
        /// the flight-lead feature.
        /// </summary>
        public Aircraft Leader
        {
            get
            {
                WingMember lead = owner?.FlightLead;
                return FlightLeadPolicy.FormationLeader(
                    ReferenceEquals(lead, this), lead?.Aircraft, owner?.Leader);
            }
        }

        /// <summary>True when the player has granted this wingman temporary flight lead.</summary>
        public bool IsFlightLead => ReferenceEquals(owner?.FlightLead, this);

        /// <summary>The rest of the wing, for separation steering.</summary>
        public System.Collections.Generic.IReadOnlyList<WingMember> Siblings =>
            owner != null ? owner.Members : null;

        public bool Alive =>
            Aircraft != null && !Aircraft.disabled &&
            Pilot != null && !Pilot.dead && !Pilot.ejected;

        public string Name => Aircraft != null ? Aircraft.unitName : "(gone)";

        public void Apply(WingOrder order) => Apply(WingDirective.Simple(order));

        /// <summary>
        /// Record a new standing intent and let the arbiter act on it now.
        ///
        /// This no longer decides anything. It used to be a twelve-case switch that called
        /// <c>SwitchState</c> directly, which is why every temporary override had to grow
        /// its own way of suppressing it. Setting the intent and resolving are now two
        /// separate things, and only the second one touches the aircraft.
        /// </summary>
        public void Apply(WingDirective directive)
        {
            // A hangar-delivered aircraft belongs to the roster immediately, but the stock
            // taxi/launch state must own it until it is airborne. Record the standing order
            // anyway so ActivateWhenAirborne can fly it instead of defaulting to Form Up.
            if (deliveryPending)
            {
                if (!WingOrderRules.CanQueueWhilePending(directive.Order)) return;
                if (directive.Order != WingOrder.MoveToPoint)
                    waypointQueue.Clear();
                SetDirective(directive);
                return;
            }

            // A scripted manoeuvre is transient and cannot usefully wait behind a missile
            // break - by the time the break clears the moment has passed. Drop it rather
            // than overwriting a real standing order with one that would be discarded.
            if (directive.Order == WingOrder.Maneuver && IsPanicking) return;

            TacticalCoordinator.Release(Aircraft);

            if (directive.Order != WingOrder.MoveToPoint)
                waypointQueue.Clear();

            // Start the idle clock fresh whenever an open-ended fight order is issued, so the
            // rest-state timeout is measured from the order rather than from the last one.
            if (directive.Order == WingOrder.Engage || directive.Order == WingOrder.Attack)
                engageActivityAt = Time.timeSinceLevelLoad;

            SetDirective(directive);

            // A player order given during a missile break is retained as the standing intent
            // and takes effect the moment the break releases - no queue, no cached pilot
            // state, because the arbiter re-reads the directive on every pass anyway.
            Resolve(force: true);
        }

        /// <summary>
        /// Finish the current task from inside the state that was flying it.
        ///
        /// Deliberately does not resolve inline: these calls arrive from
        /// <c>FixedUpdateState</c>, and switching a pilot state from within its own update
        /// is the re-entrancy that every self-completing state used to risk.
        /// </summary>
        internal void Complete(WingDirective directive) => CompleteOrder(directive, null);

        internal void CompleteFrom(WingPilotState source, WingDirective directive)
        {
            if (source == null || !ReferenceEquals(Pilot?.currentState, source)) return;
            CompleteOrder(directive, source.OrderRevision);
        }

        private void CompleteOrder(WingDirective directive, int? startedRevision)
        {
            if (deliveryPending) return;
            // Check ownership before changing either the payload or its waypoint queue.
            if (!SetDirective(directive, startedRevision)) return;

            TacticalCoordinator.Release(Aircraft);
            if (directive.Order != WingOrder.MoveToPoint) waypointQueue.Clear();
            brain.RequestEvaluation();
        }

        /// <summary>Finish the current task and fall back to holding the slot.</summary>
        internal void Complete(WingOrder order) => Complete(WingDirective.Simple(order));

        /// <summary>
        /// Record a new standing intent, and do nothing at all if it is the intent already
        /// standing. The serial is what tells <see cref="Resolve"/> to re-enter the Task
        /// behaviour, so bumping it for an identical order is what made a re-issued Form Up
        /// restart the formation state and fire the rejoin boost.
        /// </summary>
        private bool SetDirective(WingDirective directive, int? startedRevision = null)
        {
            bool changed;
            if (startedRevision.HasValue)
            {
                if (!standingOrder.TryComplete(startedRevision.Value, directive, out changed)) return false;
            }
            else changed = standingOrder.Set(directive);
            if (!changed) return true;
            RefitPending = false;
            TacticalMapOverlay.Invalidate();
            return true;
        }

        /// <summary>Release launch ownership once safely airborne, without waiting for cruise altitude.</summary>
        internal bool ActivateWhenAirborne()
        {
            if (!deliveryPending) return false;
            if (!Alive || !Aircraft.LocalSim || Pilot.flightInfo == null ||
                Time.timeSinceLevelLoad - joinedAt < 0.25f) return false;
            bool takingOff = Pilot.currentState is AIPilotTakeoffState ||
                             Pilot.currentState is AIHeloTakeoffState;
            AircraftParameters parameters = Aircraft.GetAircraftParameters();
            Vector3 airVelocity = Aircraft.rb != null ? Aircraft.rb.velocity : Vector3.zero;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level != null) airVelocity -= level.GetWind(Aircraft.GlobalPosition());
            if (!IsSurface && !LaunchSafety.CanHandOff(Pilot.flightInfo.HasTakenOff, takingOff,
                WingRegistry.IsRotary(Aircraft), Aircraft.radarAlt, airVelocity.magnitude,
                parameters != null ? parameters.takeoffSpeed : 0f)) return false;

            Pilot.flightInfo.HasTakenOff = true;
            deliveryPending = false;
            WingDepartureChatter.Activated(this);
            // Resolve the retained order without treating liftoff as a new player
            // command or resetting the queued task's clocks.
            brain.RequestEvaluation();
            Resolve(force: true);
            Plugin.Logger.LogInfo("[Wing] " + Name + " vanilla takeoff handoff; flying " + Order);
            return true;
        }

        /// <summary>
        /// True when this aircraft can be told to run cargo.
        ///
        /// A loaded cargo station and nothing else. It used to require the stock helicopter
        /// transport state as well, which quietly made the order rotary-only — but nothing
        /// about a cargo station is rotary-specific, and a fixed-wing transport with a load
        /// aboard can fly it to a drop point perfectly well. The stock state is only needed
        /// for the point-less route, and is checked where that route is taken.
        /// </summary>
        public bool CanDeliverCargo
        {
            get
            {
                if (Aircraft == null || Aircraft.weaponStations == null) return false;

                foreach (WeaponStation s in Aircraft.weaponStations)
                {
                    if (s != null && s.Cargo && s.Ammo > 0) return true;
                }
                return false;
            }
        }

        /// <summary>Cargo remaining across every cargo station.</summary>
        public int CargoAmmo
        {
            get
            {
                if (Aircraft == null || Aircraft.weaponStations == null) return 0;

                int total = 0;
                foreach (WeaponStation s in Aircraft.weaponStations)
                {
                    if (s != null && s.Cargo) total += s.Ammo;
                }
                return total;
            }
        }

        /// <summary>How long a supply run may go unfulfilled before it is abandoned.</summary>
        private const float CargoRunTimeout = 300f;

        /// <summary>
        /// Follow a supply run to its end.
        ///
        /// A drop is visible as the cargo station's own ammunition falling, which is the
        /// same field <see cref="CanDeliverCargo"/> gates on — so this confirms a real
        /// delivery rather than trusting that entering the stock transport state implies
        /// one. An empty transport rejoins; one that has been out for five minutes with its
        /// cargo still aboard has not found anywhere to put it and is given back rather
        /// than left circling for the rest of the mission.
        /// </summary>
        public void CheckCargoRun()
        {
            if (Order != WingOrder.DeliverCargo || !IsCommandable || IsPanicking) return;

            int remaining = CargoAmmo;

            if (cargoProgress.Observe(remaining, Time.timeSinceLevelLoad))
            {
                WingComms.Say(this, WingComms.Call.Delivered);
            }

            if (remaining <= 0)
            {
                if (cargoProgress.MadeProgress) WingPilotRoster.NoteSortie(Aircraft);
                Apply(WingOrder.Formation);
                return;
            }

            if (!cargoProgress.IsStalled(Time.timeSinceLevelLoad, CargoRunTimeout)) return;

            WingComms.Say(this, WingComms.Call.NoDropOff);
            WingCommandManager.Instance?.Toast(
                Name + " found nowhere to deliver its cargo - rejoining");
            Apply(WingOrder.Formation);
        }

        /// <summary>
        /// True when this aircraft can set down where it is.
        ///
        /// Asked of the hover controller rather than the autopilot type. The two disagree
        /// on exactly the aircraft this order exists for: a thrust-vectoring jet flies an
        /// <c>AutopilotPlane</c>, so it failed the rotary test, but it hovers and lands
        /// vertically as readily as any helicopter. <see cref="WingRegistry.IsRotary"/>
        /// still decides which formation model to fly, which is a different question.
        /// </summary>
        public bool CanLandInPlace => HoverAssist.CanHover(Aircraft);

        /// <summary>
        /// True when this airframe carries a jammer pod it can be told to run against a
        /// designated target. Walked each time rather than cached: a mid-mission rearm
        /// can add or drop the station, and a cache from the empty hangar fit would
        /// permanently hide the order.
        /// </summary>
        public bool CanJam => WingWeapons.HasJammer(Aircraft);

        /// <summary>
        /// How intact the airframe is, 0-1, from the game's own part hit points. Read by
        /// the Wing tab; a detached part counts as fully lost rather than merely damaged.
        /// </summary>
        public float Integrity
        {
            get
            {
                if (Aircraft == null || Aircraft.partLookup == null) return 0f;

                int counted = 0;
                float total = 0f;

                foreach (UnitPart part in Aircraft.partLookup)
                {
                    if (part == null) continue;
                    counted++;
                    total += part.IsDetached() ? 0f : Mathf.Clamp01(part.hitPoints / 100f);
                }

                return counted > 0 ? total / counted : 1f;
            }
        }

        /// <summary>
        /// Report meaningful damage transitions, not every hit-point tick. A heavy first hit
        /// goes straight to the critical call instead of queuing both lines back-to-back.
        /// </summary>
        public void CheckDamage()
        {
            // partLookup is populated asynchronously. Integrity deliberately reads zero
            // while it is absent for the roster UI, but treating that temporary zero as
            // combat damage would make a freshly spawned aircraft report itself critical.
            if (!Alive || Aircraft.partLookup == null) return;

            float current = Integrity;
            if (!criticalDamageReported && current <= 0.35f)
            {
                criticalDamageReported = true;
                damageReported = true;
                WingComms.Say(this, WingComms.Call.Critical);
            }
            else if (!damageReported && current <= 0.72f && lastIntegrity > 0.72f)
            {
                damageReported = true;
                WingComms.Say(this, WingComms.Call.Damaged);
            }

            lastIntegrity = current;
        }
        /// <summary>
        /// Give the airframe back to the stock combat AI, permanently.
        ///
        /// <b>Teardown only</b>, and the one caller is <c>DisbandAll</c>. It overwrites the
        /// standing directive with Engage and switches state without asking the arbiter,
        /// which is correct for a member leaving the roster and destructive for one that is
        /// staying — <see cref="FormationFlyState"/> used to call it when the leader died,
        /// and because FixedUpdate runs before Update it destroyed the player's orders on
        /// the very tick they were shot down, before the LeaderLost reflex could preserve
        /// anything. Do not add a caller that expects the member to keep flying for us.
        /// </summary>
        public void ReleaseToCombat(string reason)
        {
            if (deliveryPending)
            {
                // The aircraft is still under the airbase's taxi/launch AI. Removing it from
                // the player's roster must not switch that parked pilot into combat flight.
                deliveryPending = false;
                TacticalCoordinator.Release(Aircraft);
                return;
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} releasing to combat AI: {reason}");

            // A release is a teardown, not a decision: this member is leaving the roster and
            // will not be ticked again, so the handoff is unconditional rather than
            // arbitrated. Going through Resolve here could hand a departing aircraft to the
            // missile break instead of to the AI that is about to own it.
            SetDirective(WingDirective.Simple(WingOrder.Engage));
            SwitchToCombat();
        }

        /// <summary>
        /// Dismiss this aircraft: send it home rather than back to the stock combat AI.
        ///
        /// The right ending for a release the player asked for. Handing a released wingman
        /// to the combat AI left it fighting on the player's behalf without being theirs to
        /// command, and holding a squadron slot indefinitely; flying it home ends the sortie
        /// properly, returns the airframe to stock and gives the capacity back.
        ///
        /// Automatic breaks still use <see cref="ReleaseToCombat"/> — a wingman that loses
        /// its leader mid-fight should keep fighting, not run for the runway.
        /// </summary>
        public void SendHome(string reason)
        {
            if (deliveryPending)
            {
                // Still under the airbase's own taxi/launch AI, and not airborne to be sent
                // anywhere. Hand it back untouched.
                deliveryPending = false;
                TacticalCoordinator.Release(Aircraft);
                return;
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"[Wing] {Name} released and sent home: {reason}");

            TacticalCoordinator.Release(Aircraft);
            SetDirective(WingDirective.Simple(WingOrder.ReturnToBase));

            // The pilot flew a sortie and is going home from it, exactly as one ordered to
            // Return To Base does. Credited here because the settlement that normally
            // credits it runs long after this pilot has left the seat.
            WingPilotRoster.NoteSortie(Aircraft);

            // Registered before the state switch, so that an aircraft already sitting on a
            // runway - settled by the very next recovery pass - is tracked rather than
            // settled as an aircraft nobody released.
            WingDeparture.Begin(this);
            WingComms.Say(this, WingComms.Call.Detached);
            SwitchToLanding();
        }


        /// <summary>A target the player has explicitly assigned, or null.</summary>
        public Unit AssignedTarget => Directive.Target;

        /// <summary>
        /// True while a missile warning temporarily owns the flight controls. Derived from
        /// the winning reflex rather than stored: it used to be a field that four unrelated
        /// checks had to remember to consult, and one that forgot would silently disable
        /// missile defence.
        /// </summary>
        public bool IsPanicking =>
            brain.Current.BehaviourId == WingBehaviours.MissileBreak;

        /// <summary>Which reflex is in control, for the panel and the debug overlay.</summary>
        internal WingResolution Behaviour => brain.Current;

        /// <summary>
        /// What this wingman may shoot at, given what it is actually doing rather than what
        /// it was last told to do. The two differ whenever a reflex has the controls.
        /// </summary>
        internal OrderEngagementAuthority EngagementAuthority =>
            OrderRoePolicy.AuthorityFor(brain.Current.BehaviourId, Order);

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
        public void AttackTarget(Unit target, bool report = true)
        {
            if (target == null || !Alive) return;
            Apply(WingDirective.Attack(target));
            if (report && !IsPanicking)
                WingComms.Say(this, WingComms.Call.Engaging, target.unitName);
        }
        /// <summary>
        /// Order this member to expend on a target.
        ///
        /// Deliberately separate from <see cref="AttackTarget"/> rather than a parameter on
        /// it: the two orders read differently on the roster, on the map and on the radio,
        /// and a player who asked for one should never be shown the other.
        /// </summary>
        public void FireForEffect(Unit target, bool report = true)
        {
            if (target == null || !Alive) return;
            Apply(WingDirective.AtTarget(WingOrder.FireForEffect, target));
            if (report && !IsPanicking)
                WingComms.Say(this, WingComms.Call.FireForEffect, target.unitName);
        }

        /// <summary>
        /// Drop the designated unit, keeping the order. Goes through <see cref="SetDirective"/>
        /// so the serial bumps and the map is invalidated — assigning <c>Directive</c>
        /// directly left the tactical map drawing an attack line to a dead unit, and left a
        /// Task behaviour unaware that its payload had changed.
        /// </summary>
        public void ClearAssignedTarget() => SetDirective(Directive.WithoutTarget());

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
        internal void CompleteWaypoint(WingPilotState source)
        {
            if (source == null || !ReferenceEquals(Pilot?.currentState, source) ||
                source.OrderRevision != directiveSerial) return;
            if (waypointQueue.Count > 0) waypointQueue.RemoveAt(0);

            if (waypointQueue.Count > 0)
            {
                GlobalPosition next = waypointQueue[0];
                waypointState.SetDestination(next);

                // Called from inside the waypoint state's own update, so the next leg is
                // recorded as intent and entered on the next tick rather than switching the
                // state from within itself.
                Complete(WingDirective.AtPoint(WingOrder.MoveToPoint, next));
                return;
            }

            // A map move is temporary. Completion returns to formation for every ROE;
            // weapons-free permission is not permission to invent an Engage order.
            Complete(WingOrder.Formation);
        }

        /// <summary>
        /// Send the member home when it can no longer contribute. A wingman with no
        /// weapons or no fuel is just a liability holding station.
        /// </summary>
        public void CheckReserves()
        {
            if (!IsCommandable || !Plugin.Settings.AutoReturnOnEmpty.Value) return;
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
                case WingOrder.Maneuver:
                    return;
            }

            // A freshly spawned aircraft can be sampled before its weapon stations have
            // finished initialising, which reads as zero ammunition and would send it
            // straight home the moment it joined.
            if (Time.timeSinceLevelLoad - joinedAt < 10f) return;

            if (Fuel <= (Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel))
            {
                WingComms.Say(this, WingComms.Call.Bingo);
                Apply(WingOrder.ReturnToBase);
                return;
            }

            // A jammer with an empty rack is still doing its job. Only a fuel state sends
            // it home.
            if (Ammo <= 0 && Order != WingOrder.JamTarget)
            {
                WingComms.Say(this, WingComms.Call.Winchester);
                Apply(WingOrder.ReturnToBase);
            }
        }

        private float engageActivityAt;

        /// <summary>
        /// Sample activity for temporary regrouping during an open-ended fight.
        ///
        /// An <see cref="WingOrder.Engage"/> hands the wingman to the stock combat AI with no
        /// completion condition of its own, and an <see cref="WingOrder.Attack"/> whose
        /// target has drifted out of reach keeps circling. Both should return to
        /// formation while there is nothing left to prosecute:
        /// no live designated target we can still hurt, and no threat within engage range of
        /// us or the leader for <see cref="WingTuning.EngageIdleSeconds"/>.
        ///
        /// Runs on the same once-a-second housekeeping pass as <see cref="CheckReserves"/>;
        /// the timeout bridges brief lulls. The arbiter retains the combat directive so
        /// activity can resume it without another player order.
        /// </summary>
        internal void CheckEngageIdle()
        {
            float now = Time.timeSinceLevelLoad;

            if (!IsCommandable || (Order != WingOrder.Engage && Order != WingOrder.Attack) || IsPanicking)
            {
                engageActivityAt = now;
                return;
            }

            Aircraft leader = Leader;
            bool active =
                (Order == WingOrder.Attack &&
                 WingWeapons.CanStillEngage(Aircraft, AssignedTarget)) ||
                WingWeapons.NearestThreatTo(Aircraft, WingTuning.FreeEngageRange) != null ||
                (leader != null &&
                 WingWeapons.NearestThreatTo(leader, WingTuning.FreeEngageRange) != null);

            if (active)
            {
                engageActivityAt = now;
                return;
            }

            // The idle-combat reflex regroups temporarily. Keep the actual target/order
            // so a newly available engagement resumes it without another player command.
        }

        /// <summary>
        /// Retire a standing order that has nothing left to do.
        ///
        /// Called when the missile break releases, which is the moment a stale order shows
        /// up: a manoeuvre interrupted by a break is not worth re-flying once the moment has
        /// passed, and a target order whose target died while we were defending has nothing
        /// left to prosecute. Everything else resumes untouched, because the arbiter reads
        /// the directive fresh on every pass rather than caching a pilot state at entry.
        /// </summary>
        internal void RetireStaleOrder()
        {
            WingPilotRoster.NoteSurvivedEngagement(Aircraft);

            bool stale = Directive.Order == WingOrder.Maneuver ||
                         (WingOrderRules.CarriesTarget(Directive.Order) &&
                          (Directive.Target == null || Directive.Target.disabled));

            if (stale) Complete(WingOrder.Formation);
        }

    }

}
