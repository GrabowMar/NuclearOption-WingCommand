using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Commanded AI airframe and its formation slot.</summary>
    internal partial class WingMember
    {
        public readonly Aircraft Aircraft;
        public readonly Pilot Pilot;
        public int Slot;

        /// <summary>Diagnostic distance from the assigned slot, in metres.</summary>
        public float SlotError;
        public WingFlightProfile FlightProfile => brain.Flight;

        private readonly StandingOrder<WingDirective> standingOrder = new StandingOrder<WingDirective>(
            WingDirective.Simple(WingOrder.Formation), (a, b) => a.SameIntentAs(in b));
        public WingDirective Directive => standingOrder.Current;
        public WingOrder Order => Directive.Order;

        /// <summary>Per-member weapon preference, read on every station choice so mixed flights can favour
        /// different stores.</summary>
        public WingWeaponPreference WeaponPreference { get; set; } = WingWeaponPreference.Auto;

        /// <summary>Recorded loadout configured by this mod.</summary>
        public WingLoadoutChoice Loadout => WingLoadoutBook.AboardOf(Aircraft);

        /// <summary>Whether the mod knows this loadout; recruited mission aircraft may carry an unknown
        /// fit.</summary>
        public bool LoadoutKnown => WingLoadoutBook.IsKnown(Aircraft);

        /// <summary>Squadron pilot record, or null before assignment. Pilot is the separate native state
        /// machine.</summary>
        public WingPilot Crew => PersonnelFacade.Roster.Of(Aircraft);

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
        private readonly TaskRoute<WingDirective> taskQueue = new TaskRoute<WingDirective>();
        private bool applyKeepsQueue;
        private float moveAltitude;
        private float moveSpeed;
        private bool deliveryPending;

        private readonly CargoProgressTracker cargoProgress = new CargoProgressTracker();
        private float lastIntegrity;
        private bool damageReported;
        private bool criticalDamageReported;

        /// <summary>Whether a hangar delivery still awaits native takeoff.</summary>
        public bool DeliveryPending => deliveryPending;
        public bool RefitPending { get; private set; }

        /// <summary>Request RTB, replenishment, and relaunch while retaining aircraft and pilot.
        /// WingRecovery calls CompleteRefit instead of settling this return into stock.</summary>
        public void RequestRefit()
        {
            if (!IsCommandable || IsSurface || RefitPending) return;
            taskQueue.Suspend(Directive);
            applyKeepsQueue = true;
            try { Apply(WingOrder.ReturnToBase); }
            finally { applyKeepsQueue = false; }
            RefitPending = true;
        }

        /// <summary>Cancel refit so recovery can settle normal RTB, including after native pilot
        /// ejection.</summary>
        internal void AbandonRefit()
        {
            RefitPending = false;
            taskQueue.CancelSuspension();
        }

        /// <summary>Replenish at the current parked pose, then queue native taxi through the departure
        /// lane.</summary>
        public void CompleteRefit()
        {
            if (!RefitPending || Pilot == null || Pilot.dead || Pilot.ejected) return;

            foreach (FuelTank tank in Aircraft.GetFuelTanks())
                if (tank != null) tank.Refuel(1f);
            Aircraft.NetworkfuelLevel = Aircraft.GetFuelLevel();

            // RpcRearm indexes by station, so include zero entries for every unarmed station.
            var ammunition = new int[Aircraft.weaponStations.Count];
            for (int i = 0; i < ammunition.Length; i++)
            {
                WeaponStation station = Aircraft.weaponStations[i];
                ammunition[i] = Mathf.Max(0, station.FullAmmo - station.GetAmmoTotal());
            }
            Aircraft.RpcRearm(new RearmEventArgs { Rearmer = Aircraft, Stations = ammunition });

            PersonnelFacade.Roster.NoteSortie(Aircraft);
            WingDirective resume = taskQueue.Restore(CanResumeAfterRefit,
                WingDirective.Simple(WingOrder.Formation));
            SetDirective(resume);
            if (resume.Order == WingOrder.Engage || resume.Order == WingOrder.Attack)
                engageActivityAt = Time.timeSinceLevelLoad;
            RefitPending = false;
            BeginRefitDeparture();
        }

        /// <summary>Restart departure with fresh native taxi at the current pose. Direct takeoff
        /// off-runway drives across the apron and can eject after its stuck timeout. Reset HasTakenOff so
        /// taxi seeks a runway rather than service.</summary>
        private void BeginRefitDeparture()
        {
            if (Pilot == null || Aircraft == null || !Alive) return;

            Airbase field = WingAirfield.FieldUnder(Aircraft);
            if (field != null) HangarDepartureLane.Reserve(field, Aircraft.transform, this);

            // Remove the completed landing claim before it blocks takeoff on the same runway.
            WingAirfield.DrainLandingList(Aircraft);

            Pilot.flightInfo.HasTakenOff = false;
            deliveryPending = true;

            if (WingRegistry.IsRotary(Aircraft))
            {
                Pilot.AIHeloTakeoffState = new AIHeloTakeoffState();
                Pilot.SwitchState(Pilot.AIHeloTakeoffState);
            }
            else
            {
                Pilot.AITaxiState = new AIPilotTaxiState();
                Pilot.SwitchState(Pilot.AITaxiState);
            }

            Plugin.LogVerbose("[Wing] " + Name + " refitted; relaunching from " +
                                  (field != null ? WingLaunchFields.DisplayName(field) : "its field"));
        }

        /// <summary>Whether the airframe lacks an autopilot and requires surface-vehicle control,
        /// regardless of its supplying mod.</summary>
        public bool IsSurface => Aircraft != null && Aircraft.autopilot == null;

        /// <summary>Whether this live member has finished delivery and can receive commands.</summary>
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

        /// <summary>Formation reference: the player's aircraft, or the assigned flight lead. The lead
        /// itself continues to form on the player.</summary>
        public Aircraft Leader
        {
            get
            {
                WingMember lead = owner?.FlightLead;
                return FlightLeadPolicy.FormationLeader(
                    ReferenceEquals(lead, this), lead?.Aircraft, owner?.Leader);
            }
        }

        /// <summary>Whether this member is the temporary flight lead.</summary>
        public bool IsFlightLead => ReferenceEquals(owner?.FlightLead, this);

        /// <summary>Wing roster used for separation steering.</summary>
        public System.Collections.Generic.IReadOnlyList<WingMember> Siblings =>
            owner != null ? owner.Members : null;

        public bool Alive =>
            Aircraft != null && !Aircraft.disabled &&
            Pilot != null && !Pilot.dead && !Pilot.ejected;

        public string Name => Aircraft != null ? Aircraft.unitName : "(gone)";

        public void Apply(WingOrder order) => Apply(WingDirective.Simple(order));

        /// <summary>Record standing intent, then ask the arbiter to resolve control immediately.</summary>
        public void Apply(WingDirective directive)
        {
            if (directive.Order == WingOrder.StandDown && !directive.HasPoint && Aircraft != null)
            {
                Vector3 away = Aircraft.transform != null
                    ? -Aircraft.transform.forward
                    : Vector3.forward;
                directive = WingDirective.AtPoint(
                    WingOrder.StandDown, FallBackState.FriendlyLoiterPoint(Aircraft, away));
            }

            // Retain orders during native taxi/launch; activate the recorded directive after takeoff.
            if (deliveryPending)
            {
                if (!WingOrderRules.CanQueueWhilePending(directive.Order)) return;
                if (!applyKeepsQueue) { taskQueue.Clear(); AbandonRefit(); }
                SetDirective(directive);
                return;
            }

            // Reject transient manoeuvres during missile defence; do not overwrite an order with one
            // that would expire before use.
            if (directive.Order == WingOrder.Maneuver && IsPanicking) return;

            CombatFacade.Tactical.ReleaseSelection(Aircraft);

            if (!applyKeepsQueue) { taskQueue.Clear(); AbandonRefit(); }

            // Restart idle-combat timing for each new open-ended fight order.
            if (directive.Order == WingOrder.Engage || directive.Order == WingOrder.Attack)
                engageActivityAt = Time.timeSinceLevelLoad;

            SetDirective(directive);

            // Retain commands issued during missile defence; the arbiter applies them when defence
            // releases.
            Resolve(force: true);
        }

        /// <summary>Complete a task without resolving inline; state-update callers must not switch state
        /// re-entrantly.</summary>
        internal void Complete(WingDirective directive) => CompleteOrder(directive, null);

        internal void CompleteFrom(WingPilotState source, WingDirective directive)
        {
            if (source == null || !ReferenceEquals(Pilot?.currentState, source)) return;
            if (TryAdvanceQueue(source.OrderRevision)) return;
            CompleteOrder(directive, source.OrderRevision);
        }

        private void CompleteOrder(WingDirective directive, int? startedRevision)
        {
            if (deliveryPending) return;
            // Validate task ownership before changing directive or queue.
            if (!SetDirective(directive, startedRevision)) return;

            CombatFacade.Tactical.ReleaseSelection(Aircraft);
            if (!applyKeepsQueue) { taskQueue.Clear(); AbandonRefit(); }
            brain.RequestEvaluation();
        }

        /// <summary>Complete with a simple order directive.</summary>
        internal void Complete(WingOrder order) => Complete(WingDirective.Simple(order));

        /// <summary>Update intent only when changed. The revision triggers task re-entry, so identical
        /// directives must not bump it.</summary>
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

        /// <summary>Transfer control once safely airborne; cruise altitude is not required.</summary>
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
            float forwardAirspeed = Vector3.Dot(airVelocity, Aircraft.transform.forward);
            float minimumAirspeed = parameters != null
                ? FormationGuidance.MinimumAirspeed(Aircraft.definition.aircraftInfo?.stallSpeed ?? 0f,
                    parameters.landingSpeed) : 0f;
            if (!IsSurface && !LaunchSafety.CanHandOff(Pilot.flightInfo.HasTakenOff, takingOff,
                WingRegistry.IsRotary(Aircraft), Aircraft.radarAlt, forwardAirspeed,
                parameters != null ? parameters.takeoffSpeed : 0f, minimumAirspeed)) return false;

            Pilot.flightInfo.HasTakenOff = true;
            deliveryPending = false;
            // Refit owns the departure lane until liftoff.
            HangarDepartureLane.Release(this);
            PersonnelFacade.DepartureChatter.Activated(this);
            // Evaluate the retained order without resetting queued-task clocks or treating takeoff as a
            // new command.
            brain.RequestEvaluation();
            Resolve(force: true);
            Plugin.LogVerbose("[Wing] " + Name + " vanilla takeoff handoff; flying " + Order);
            return true;
        }

        /// <summary>Whether any cargo station is loaded. Point deliveries support fixed-wing aircraft;
        /// only the point-free route requires native transport state.</summary>
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

        /// <summary>Total cargo ammunition across stations.</summary>
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

        /// <summary>Seconds allowed for a cargo run before abandoning it.</summary>
        private const float CargoRunTimeout = 300f;

        /// <summary>Confirm delivery by decreasing cargo ammunition. Rejoin when empty; abandon after five
        /// minutes without delivery instead of circling indefinitely.</summary>
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
                if (cargoProgress.MadeProgress) PersonnelFacade.Roster.NoteSortie(Aircraft);
                Apply(WingOrder.Formation);
                return;
            }

            if (!cargoProgress.IsStalled(Time.timeSinceLevelLoad, CargoRunTimeout)) return;

            WingComms.Say(this, WingComms.Call.NoDropOff);
            WingCommandManager.Instance?.Toast(
                Name + " found nowhere to deliver its cargo - rejoining");
            Apply(WingOrder.Formation);
        }

        /// <summary>Whether the hover controller permits vertical landing, including vectoring jets
        /// classified as fixed-wing for formation flight.</summary>
        public bool CanLandInPlace => HoverAssist.CanHover(Aircraft);

        /// <summary>Whether a jammer pod is fitted. Recheck stations so mid-mission loadout changes affect
        /// order availability.</summary>
        public bool CanJam => CombatFacade.Weapons.HasJammer(Aircraft);

        /// <summary>Airframe integrity from native part hit points, 0-1; detached parts count as fully
        /// lost.</summary>
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

        /// <summary>Report damage threshold crossings; a severe first hit emits only the critical
        /// call.</summary>
        public void CheckDamage()
        {
            // Wait for asynchronous partLookup initialisation; its temporary zero integrity is not
            // combat damage.
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
        /// <summary>Teardown-only handoff to native combat AI, used by DisbandAll. Overwrites intent and
        /// bypasses arbitration; never call for a member remaining on the roster.</summary>
        public void ReleaseToCombat(string reason)
        {
            taskQueue.Clear();
            AbandonRefit();
            HangarDepartureLane.Release(this);
            if (deliveryPending)
            {
                // Release roster ownership without interrupting pending native taxi or launch.
                deliveryPending = false;
                CombatFacade.Tactical.Release(Aircraft);
                return;
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose($"[Wing] {Name} releasing to combat AI: {reason}");

            // Bypass arbitration during teardown so temporary reflexes cannot retain a departing
            // aircraft.
            SetDirective(WingDirective.Simple(WingOrder.Engage));
            SwitchToCombat();
        }

        /// <summary>Dismiss the aircraft to native RTB, freeing squadron capacity and returning owned
        /// stock on recovery. Teardown uses ReleaseToCombat separately.</summary>
        public void SendHome(string reason)
        {
            taskQueue.Clear();
            AbandonRefit();
            HangarDepartureLane.Release(this);
            if (deliveryPending)
            {
                // Leave a pending delivery under native taxi/launch control.
                deliveryPending = false;
                CombatFacade.Tactical.Release(Aircraft);
                return;
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose($"[Wing] {Name} released and sent home: {reason}");

            CombatFacade.Tactical.Release(Aircraft);
            SetDirective(WingDirective.Simple(WingOrder.ReturnToBase));

            // Credit the sortie before releasing the pilot; later settlement no longer owns this seat.
            PersonnelFacade.Roster.NoteSortie(Aircraft);

            // Track departure before switching state so an already-landed aircraft can settle on the
            // next recovery pass.
            PersonnelFacade.Departure.Begin(this);
            WingComms.Say(this, WingComms.Call.Detached);
            SwitchToLanding();
        }


        /// <summary>Player-designated target, or null.</summary>
        public Unit AssignedTarget => Directive.Target;

        /// <summary>Whether missile defence owns flight, derived from the winning reflex.</summary>
        public bool IsPanicking =>
            brain.Current.BehaviourId == WingBehaviours.MissileBreak;

        /// <summary>Current reflex resolution for UI and diagnostics.</summary>
        internal WingResolution Behaviour => brain.Current;

        /// <summary>Weapons authority from active behaviour, which may temporarily override the standing
        /// order.</summary>
        internal OrderEngagementAuthority EngagementAuthority =>
            OrderRoePolicy.AuthorityFor(brain.Current.BehaviourId, Order, PatrolRoute);

        /// <summary>Remaining fuel fraction, 0-1.</summary>
        public float Fuel => Aircraft != null ? Aircraft.GetFuelLevel() : 0f;

        /// <summary>Total remaining ammunition across weapon stations.</summary>
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

        /// <summary>Issue a targeted attack directive so the flight controller prosecutes it; native
        /// combat AI does not consume Pilot.SetPrimaryTarget for this purpose.</summary>
        public void AttackTarget(Unit target, bool report = true)
        {
            if (target == null || !Alive) return;
            Apply(WingDirective.Attack(target));
            if (report && !IsPanicking)
                WingComms.Say(this, WingComms.Call.Engaging, target.unitName);
        }
        /// <summary>Issue Splash 'Em as a distinct expend order, preserving its own map, roster, and radio
        /// identity.</summary>
        public void FireForEffect(Unit target, bool report = true)
        {
            if (target == null || !Alive) return;
            Apply(WingDirective.AtTarget(WingOrder.FireForEffect, target));
            if (report && !IsPanicking)
                WingComms.Say(this, WingComms.Call.FireForEffect, target.unitName);
        }

        /// <summary>Retarget an active Splash run without resolving inside its state update. The attack
        /// state reads AssignedTarget each frame and need not restart.</summary>
        internal void RetargetSplash(Unit target)
        {
            if (target == null || target.disabled || Order != WingOrder.FireForEffect) return;
            SetDirective(WingDirective.AtTarget(WingOrder.FireForEffect, target));
        }

        /// <summary>Replace or append a map task. Shift appends only to a compatible current map task;
        /// Move replaces Attack or Hold.</summary>
        public void IssueMapTask(WingDirective directive, bool append)
        {
            if (!Alive) return;

            bool sameKind = append && Order == directive.Order && MapOrderPolicy.CanFollowOn(Order);
            if (!sameKind)
            {
                AbandonRefit();
                taskQueue.Clear();
                taskQueue.Add(directive);
                applyKeepsQueue = true;
                Apply(directive);
                applyKeepsQueue = false;
                return;
            }

            if (taskQueue.Count == 0)
                taskQueue.Add(Directive);

            taskQueue.Add(directive);
            TacticalMapOverlay.Invalidate();
        }

        public bool HasFollowOn => taskQueue.Count > 1;

        /// <summary>Requested Move altitude in metres AGL; zero selects the airframe default.</summary>
        public float MoveAltitude => moveAltitude;

        public float ResolvedMoveAltitude =>
            MapOrderPolicy.StepMoveAltitude(moveAltitude, 0, WingRegistry.IsRotary(Aircraft));

        public void SetMoveAltitude(float altitude)
        {
            moveAltitude = altitude;
        }

        public float MoveSpeed => moveSpeed;

        public float ResolvedMoveSpeed => MapOrderPolicy.StepMoveSpeed(moveSpeed, 0);

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }

        /// <summary>Live route with current leg first, used by the tactical map. Do not retain it across
        /// CompleteWaypoint.</summary>
        public IReadOnlyList<WingDirective> Route => taskQueue;

        /// <summary>Advance the route, then apply the completed task's terminal order.</summary>
        internal void CompleteWaypoint(WingPilotState source)
        {
            if (source == null || !ReferenceEquals(Pilot?.currentState, source) ||
                source.OrderRevision != directiveSerial) return;

            // A final Seek and Destroy enters combat; queued follow-ons take precedence.
            if (Order == WingOrder.SeekAndDestroy && !HasFollowOn)
            {
                taskQueue.Clear();
                engageActivityAt = Time.timeSinceLevelLoad;
                Complete(WingOrderRules.PointTaskCompletion(Order));
                CombatFacade.Roe.EnsureFree(owner);
                return;
            }

            if (TryAdvanceQueue(source.OrderRevision)) return;

            // Completed Move returns to formation under every ROE; weapons permission does not create
            // an Engage order.
            Complete(WingOrder.Formation);
        }

        /// <summary>Start a queued follow-on after reaching the open-ended hold's orbit.</summary>
        internal void CompleteHoldForQueue(int orbitRevision)
        {
            if (Order != WingOrder.OrbitHere || !HasFollowOn) return;
            if (orbitRevision != directiveSerial) return;
            TryAdvanceQueue(orbitRevision);
        }

        /// <summary>Remove the completed leg and activate the next; return false when empty so the caller
        /// selects its terminal order.</summary>
        internal bool TryAdvanceQueue(int startedRevision)
        {
            if (!taskQueue.Advance(startedRevision, directiveSerial, out WingDirective next)) return false;
            applyKeepsQueue = true;
            bool applied = SetDirective(next, startedRevision);
            applyKeepsQueue = false;
            if (!applied) return false;

            if (next.Order == WingOrder.Engage || next.Order == WingOrder.Attack)
                engageActivityAt = Time.timeSinceLevelLoad;

            CombatFacade.Tactical.ReleaseSelection(Aircraft);
            brain.RequestEvaluation();
            TacticalMapOverlay.Invalidate();
            return true;
        }

        /// <summary>Send bingo-fuel members home; regroup empty racks in formation instead of ending their
        /// sorties.</summary>
        public void CheckReserves()
        {
            if (!IsCommandable || !Plugin.Settings.AutoReturnOnEmpty.Value) return;
            if (IsPanicking) return;

            if (AutoRefit && !IsSurface && Order != WingOrder.ReturnToBase &&
                Order != WingOrder.LandHere && Order != WingOrder.DeliverCargo &&
                Order != WingOrder.FallBack && Order != WingOrder.StandDown &&
                Order != WingOrder.Maneuver && Time.timeSinceLevelLoad - joinedAt >= 10f &&
                (Fuel <= Plugin.Settings.BingoFuel ||
                 (CombatStoresEmpty && Order != WingOrder.JamTarget)))
            {
                RequestRefit();
                return;
            }

            // Do not interrupt deliberate landing, cargo, or retreat tasks for bingo handling.
            switch (Order)
            {
                case WingOrder.ReturnToBase:
                case WingOrder.LandHere:
                case WingOrder.DeliverCargo:
                case WingOrder.FallBack:
                case WingOrder.MoveToPoint:
                case WingOrder.SeekAndDestroy:
                case WingOrder.Maneuver:
                case WingOrder.StandDown:
                    return;
            }

            // Allow ten seconds for spawned weapon stations to initialise before treating zero
            // ammunition as empty.
            if (Time.timeSinceLevelLoad - joinedAt < 10f) return;

            if (Fuel <= (Plugin.Settings != null ? Plugin.Settings.BingoFuel : WingTuning.BingoFuel))
            {
                WingComms.Say(this, WingComms.Call.Bingo);
                Apply(WingOrder.ReturnToBase);
                return;
            }

            // Keep empty-rack jammers working and let Splash finish itself; otherwise regroup in
            // formation.
            if (Ammo <= 0 && Order != WingOrder.JamTarget && Order != WingOrder.FireForEffect &&
                Order != WingOrder.Formation)
            {
                WingComms.Say(this, WingComms.Call.OutOfAmmo);
                Apply(WingOrder.Formation);
            }
        }

        private float engageActivityAt;

        /// <summary>Sample Engage/Attack activity during housekeeping. Regroup after EngageIdleSeconds
        /// without a usable designation or nearby threat to self/leader. Retain the directive so combat
        /// can resume automatically.</summary>
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
                 CombatFacade.Weapons.CanStillEngage(Aircraft, AssignedTarget)) ||
                CombatFacade.Weapons.NearestThreatTo(Aircraft, WingTuning.FreeEngageRange) != null ||
                (leader != null &&
                 CombatFacade.Weapons.NearestThreatTo(leader, WingTuning.FreeEngageRange) != null);

            if (active)
            {
                engageActivityAt = now;
                return;
            }

            // Temporary regrouping preserves target and order for renewed combat.
        }

        /// <summary>After missile defence, discard interrupted manoeuvres and dead-target tasks. Resume
        /// all other standing intent unchanged.</summary>
        internal void RetireStaleOrder()
        {
            PersonnelFacade.Roster.NoteSurvivedEngagement(Aircraft);

            bool stale = Directive.Order == WingOrder.Maneuver ||
                         (WingOrderRules.CarriesTarget(Directive.Order) &&
                          (Directive.Target == null || Directive.Target.disabled));

            if (stale && !TryAdvanceQueue(directiveSerial))
                Complete(WingOrder.Formation);
        }

    }

}
