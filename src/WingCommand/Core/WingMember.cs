using System;
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
        private readonly FallBackState fallBackState;
        private readonly OrbitState orbitState;
        private readonly LandInPlaceState landState;
        private readonly CargoRunState cargoRunState;
        private readonly WaypointTaskState waypointState;
        private readonly AttackRunState attackState;
        private readonly DefensiveManeuverState defensiveState;
        private readonly ManeuverState maneuverState;

        /// <summary>
        /// Drives this aircraft's radar jammer while a Jam Target order is standing. Held
        /// on the member because the order is flown from inside <see cref="FormationFlyState"/>,
        /// which owns no per-aircraft state of its own.
        /// </summary>
        internal RadarJammerPulser Jammer { get; } = new RadarJammerPulser();

        private bool? canJam;

        /// <summary>
        /// What the arbiter last decided, and when it took effect. Together with
        /// <see cref="Directive"/> these are the only two pieces of "what is this wingman
        /// doing" state — intent and behaviour. Every temporary override used to add a third
        /// (a bool, a set, a duplicated directive); now they are all reflexes and this is
        /// the single record of which one is winning.
        /// </summary>
        private WingResolution resolution;
        private float behaviourEnteredAt;

        /// <summary>
        /// Bumped whenever the standing directive changes. The Task behaviour has to be
        /// re-entered when the order under it changes even though the winning reflex has
        /// not, and comparing serials is how that is noticed.
        /// </summary>
        private int directiveSerial;
        private int enteredSerial = -1;

        /// <summary>
        /// Set by a state that finished its own task. Resolution is deferred to the next
        /// tick rather than run inline, because these calls arrive from inside
        /// <c>FixedUpdateState</c> and switching a pilot state from within its own update is
        /// the re-entrancy hazard <see cref="ManeuverState"/> already had to guard against
        /// by hand.
        /// </summary>
        private bool resolvePending;

        private float nextResolve;
        private float lastMissileWarnAt = float.NegativeInfinity;
        private bool everWarned;

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

        /// <summary>Whether the airframe has cleared the delivery launch threshold.</summary>
        public bool IsAirborne => Aircraft != null && Aircraft.radarAlt >= 25f;

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
            fallBackState = new FallBackState(this);
            orbitState = new OrbitState(this);
            landState = new LandInPlaceState(this);
            cargoRunState = new CargoRunState(this);
            waypointState = new WaypointTaskState(this);
            attackState = new AttackRunState(this);
            defensiveState = new DefensiveManeuverState(this);
            maneuverState = new ManeuverState(this);
            joinedAt = Time.timeSinceLevelLoad;
            Directive = WingDirective.Simple(WingOrder.Formation);
            lastIntegrity = Integrity;
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
            // taxi/launch state must own it until it is airborne. Dispatcher and automation
            // filters also enforce this; keeping the guard here protects every call site.
            if (deliveryPending) return;

            // A scripted manoeuvre is transient and cannot usefully wait behind a missile
            // break - by the time the break clears the moment has passed. Drop it rather
            // than overwriting a real standing order with one that would be discarded.
            if (directive.Order == WingOrder.Maneuver && IsPanicking) return;

            TacticalCoordinator.Release(Aircraft);

            if (directive.Order != WingOrder.MoveToPoint)
                waypointQueue.Clear();

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
        internal void Complete(WingDirective directive)
        {
            if (deliveryPending) return;

            TacticalCoordinator.Release(Aircraft);
            if (directive.Order != WingOrder.MoveToPoint) waypointQueue.Clear();

            SetDirective(directive);
            resolvePending = true;
        }

        /// <summary>Finish the current task and fall back to holding the slot.</summary>
        internal void Complete(WingOrder order) => Complete(WingDirective.Simple(order));

        /// <summary>
        /// Record a new standing intent, and do nothing at all if it is the intent already
        /// standing. The serial is what tells <see cref="Resolve"/> to re-enter the Task
        /// behaviour, so bumping it for an identical order is what made a re-issued Form Up
        /// restart the formation state and fire the rejoin boost.
        /// </summary>
        private void SetDirective(WingDirective directive)
        {
            if (Directive.SameIntentAs(in directive)) return;

            Directive = directive;
            directiveSerial++;
            TacticalMapOverlay.Invalidate();
        }

        // ------------------------------------------------------------------- arbitration

        /// <summary>
        /// The one place this wingman decides what to fly. Called once per frame from the
        /// wing's update.
        ///
        /// Everything that used to reach in and switch a pilot state on its own - the
        /// missile check, the leash check, the leader-on-deck sweep, the delivery lockout -
        /// now arrives as a reflex score and is compared against the others in one pass.
        /// </summary>
        public void Tick()
        {
            if (!Alive) return;
            Resolve(force: false);
        }

        private void Resolve(bool force)
        {
            if (Pilot == null || Aircraft == null) return;

            bool warned = MissileWarned;
            float now = Time.timeSinceLevelLoad;

            if (warned)
            {
                lastMissileWarnAt = now;
                everWarned = true;
            }

            // Performance mode coarsens arbitration, but never while something is shooting
            // at us: the survival band is exempt from the stride for the same reason the
            // defensive state's own threat refresh is exempt from WingBrain.Interval.
            if (!force && !resolvePending && !warned && !IsPanicking)
            {
                if (now < nextResolve) return;
            }
            nextResolve = now + (WingBrain.Full ? 0f : WingBrain.Interval(0.25f));
            resolvePending = false;

            // With verbose logging on, ask for the whole ladder rather than just the winner.
            // A behaviour system whose decisions cannot be inspected is one that gets
            // debugged by guessing, and this one is meant to be extended.
            List<WingReflexTrace> trace = Plugin.Settings.VerboseLogging.Value
                ? traceBuffer ??= new List<WingReflexTrace>()
                : null;

            WingSituation situation = Sample(warned, now);
            WingResolution next = WingArbiter.Resolve(
                in situation, resolution.ReflexId, WingBrain.Full, WingAi.Reflexes, trace);

            bool behaviourChanged = !next.SameAs(in resolution);
            bool taskNeedsReentry = next.BehaviourId == WingBehaviours.Task &&
                                    enteredSerial != directiveSerial;

            if (!behaviourChanged && !taskNeedsReentry) return;

            // Coming out of a missile break. Handled here rather than in the defensive
            // state's LeaveState for two reasons: LeaveState ran one step too late, after
            // EnterTask had already read the stale directive and entered it, so an
            // interrupted manoeuvre was started for a tick - radio call and all - before
            // being pulled back; and LeaveState also fires on teardown, so a wingman being
            // released or sent home announced itself clear of a missile on the way out.
            if (behaviourChanged && resolution.BehaviourId == WingBehaviours.MissileBreak)
            {
                WingComms.Say(this, WingComms.Call.DefensiveClear);
                RetireStaleOrder();
            }

            if (behaviourChanged) behaviourEnteredAt = now;
            resolution = next;
            enteredSerial = directiveSerial;

            EnterBehaviour(next.BehaviourId);

            if (trace != null && behaviourChanged)
                Plugin.Logger.LogInfo($"[Wing] {Name} {next}  |  {Ladder(trace)}");
        }

        private List<WingReflexTrace> traceBuffer;

        /// <summary>
        /// The whole ladder on one line: who scored what, and who won. Reads as
        /// <c>survival:missile-break=0.90* safety:deck-hold=0.00 task:standing-task=1.00</c>.
        /// </summary>
        private static string Ladder(List<WingReflexTrace> trace)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < trace.Count; i++)
            {
                WingReflexTrace t = trace[i];
                if (i > 0) sb.Append(' ');
                sb.Append(t.Band).Append(':').Append(Short(t.Id))
                  .Append('=').Append(t.Score.ToString("0.00"));
                if (t.Won) sb.Append('*');
            }
            return sb.ToString();
        }

        /// <summary>Drop the owning plugin's prefix; the log line already says whose wing it is.</summary>
        private static string Short(string id)
        {
            int dot = id.LastIndexOf('.');
            return dot >= 0 && dot < id.Length - 1 ? id.Substring(dot + 1) : id;
        }

        private WingSituation Sample(bool warned, float now)
        {
            Aircraft leader = Leader;
            float leaderDistance = -1f;
            if (leader != null && !leader.disabled)
            {
                leaderDistance = Mathf.Sqrt(
                    FastMath.SquareDistance(Aircraft.GlobalPosition(), leader.GlobalPosition()));
            }

            RefreshSlowSamples(now);

            return new WingSituation(
                order: Order,
                roe: RoeRules.Current,
                deliveryPending: deliveryPending,
                missileWarned: warned,
                secondsSinceMissileWarning: everWarned ? now - lastMissileWarnAt : 999f,
                leaderOnDeck: owner != null && owner.LeaderOnDeck,
                leaderPresent: leaderDistance >= 0f,
                targetAlive: AssignedTarget != null && !AssignedTarget.disabled,
                leaderDistance: leaderDistance,
                leashRadius: WingTuning.LeashRadius,
                radarAlt: Aircraft.radarAlt,
                fuel: sampledFuel,
                ammo: sampledAmmo,
                integrity: sampledIntegrity,
                secondsInBehaviour: now - behaviourEnteredAt);
        }

        private int sampledAmmo = 1;
        private float sampledIntegrity = 1f;
        private float sampledFuel = 1f;
        private float nextSlowSample;

        /// <summary>
        /// The three expensive fields of the situation, refreshed on a slow timer.
        ///
        /// Each of them walks a collection: ammunition every weapon station, condition every
        /// airframe part, and fuel every tank twice over — <c>Aircraft.GetFuelLevel</c> sums
        /// capacity and level across the lot on every call. None of the three moves fast
        /// enough to be worth that per member per frame.
        ///
        /// No built-in reflex reads any of them. They are sampled anyway because a
        /// third-party one reasonably might, and an extension point that offers only the
        /// cheap fields is a worse extension point.
        /// </summary>
        private void RefreshSlowSamples(float now)
        {
            if (now < nextSlowSample) return;
            nextSlowSample = now + WingBrain.Interval(1f);

            sampledAmmo = Ammo;
            sampledIntegrity = Integrity;
            sampledFuel = Fuel;
        }

        /// <summary>True when a missile is airborne and this aircraft is its target.</summary>
        private bool MissileWarned
        {
            get
            {
                MissileWarning warning = Aircraft != null
                    ? Aircraft.GetMissileWarningSystem()
                    : null;
                return warning != null && warning.IsWarning();
            }
        }

        /// <summary>
        /// Put the resolved behaviour on the aircraft. The only caller of
        /// <c>Pilot.SwitchState</c> for a commandable wingman, which is what makes the
        /// behaviour graph describable at all.
        /// </summary>
        private void EnterBehaviour(string behaviourId)
        {
            switch (behaviourId)
            {
                case WingBehaviours.Held:
                    EnterHeld();
                    return;

                case WingBehaviours.MissileBreak:
                    TacticalCoordinator.Release(Aircraft);
                    Pilot.SwitchState(defensiveState);
                    return;

                case WingBehaviours.DeckHold:
                    EnterDeckHold();
                    return;

                case WingBehaviours.Rejoin:
                    WingComms.Say(this, WingComms.Call.Rejoining);
                    formationState.BoostRejoin(0f);
                    Pilot.SwitchState(formationState);
                    return;

                case WingBehaviours.Task:
                    EnterTask();
                    return;

                default:
                    // A third-party behaviour id. Registered states are looked up here; an
                    // unknown one falls back to the standing order rather than leaving the
                    // aircraft in whatever state it happened to be flying.
                    if (WingBehaviourCatalog.TryEnter(this, behaviourId)) return;
                    Plugin.Logger.LogWarning(
                        $"[Wing] {Name}: no behaviour registered for '{behaviourId}'; flying the order instead.");
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

            orbitState.SetAnchor(anchor, WingTuning.OrbitRadius);
            Pilot.SwitchState(orbitState);
        }

        /// <summary>Fly the standing order. The old Apply switch, unchanged in substance.</summary>
        private void EnterTask()
        {
            switch (Directive.Order)
            {
                case WingOrder.Formation:
                    formationState.BoostRejoin(Slot * WingTuning.RejoinStagger);
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
                    EnterOrbit(Directive);
                    break;

                case WingOrder.DeliverCargo:
                    EnterCargoRun(Directive);
                    break;

                case WingOrder.LandHere:
                    EnterLanding(Directive);
                    break;

                case WingOrder.MoveToPoint:
                    EnterWaypoint(Directive);
                    break;

                case WingOrder.Attack:
                    EnterAttack(Directive);
                    break;

                // Both are flown from the formation slot rather than as a break-away run,
                // so the wingman is already where it needs to be; FormationFlyState reads
                // SlotTask to know which one it is working.
                case WingOrder.FireForEffect:
                case WingOrder.JamTarget:
                    EnterSlotTask();
                    break;

                case WingOrder.Maneuver:
                    maneuverState.SetManeuver(Directive.Maneuver);
                    Pilot.SwitchState(maneuverState);
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
                if (Order == WingOrder.FireForEffect) return SlotTask.Splash;
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
            Pilot.SwitchState(orbitState);
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
                Pilot.SwitchState(cargoRunState);
                return;
            }

            if (Pilot.AIHeloTransportState != null)
            {
                Pilot.SwitchState(Pilot.AIHeloTransportState);
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
            Pilot.SwitchState(landState);
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
            Pilot.SwitchState(waypointState);
        }

        /// <summary>
        /// Reached only if something re-applies a standing attack order. AttackTarget is the
        /// normal entry point and sets the target first.
        /// </summary>
        private void EnterAttack(WingDirective directive)
        {
            if (AssignedTarget != null && !AssignedTarget.disabled)
            {
                Pilot.SwitchState(attackState);
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
        /// Splash 'Em and Jam Target: hold the slot and work the designated unit from where
        /// we are, rather than breaking off into an attack run. FormationFlyState owns the
        /// shooting and the jamming; it reads <see cref="SlotTask"/> to know which.
        ///
        /// No rejoin boost. The wingman is already in its slot — these orders never take it
        /// out of formation, so hurrying it back to a place it has not left only produced a
        /// visible surge every time a target was designated.
        /// </summary>
        private void EnterSlotTask()
        {
            if (AssignedTarget != null && !AssignedTarget.disabled)
            {
                Pilot.SwitchState(formationState);
            }
            else
            {
                Complete(WingOrder.Formation);
            }
        }

        private Dictionary<string, PilotBaseState> extraBehaviours;

        /// <summary>
        /// The pilot state for a third-party behaviour on this wingman, built on first use
        /// and cached for the life of the member — the same lifetime the built-in states get
        /// from the constructor.
        /// </summary>
        internal PilotBaseState CachedBehaviour(string behaviourId,
                                                Func<Aircraft, PilotBaseState> factory)
        {
            extraBehaviours ??= new Dictionary<string, PilotBaseState>(StringComparer.Ordinal);

            if (!extraBehaviours.TryGetValue(behaviourId, out PilotBaseState state))
            {
                state = factory(Aircraft);
                extraBehaviours[behaviourId] = state;
            }
            return state;
        }

        /// <summary>Release the stock launch state once a pending delivery is airborne.</summary>
        internal bool ActivateWhenAirborne()
        {
            if (!deliveryPending || !IsAirborne) return false;

            deliveryPending = false;
            Apply(WingOrder.Formation);
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
        /// True when this airframe carries a radar jammer it can be told to run against a
        /// designated target. Resolved once the aircraft's countermeasure manager exists,
        /// then cached.
        /// </summary>
        public bool CanJam
        {
            get
            {
                if (canJam.HasValue) return canJam.Value;
                if (Aircraft == null || Aircraft.countermeasureManager == null) return false;
                canJam = Jammer.HasJammer(Aircraft);
                return canJam.Value;
            }
        }

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
        /// Give control back to the stock combat AI. Used both for an explicit Engage
        /// order and for automatic breaks (leader lost, mutual support).
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
            resolution.BehaviourId == WingBehaviours.MissileBreak;

        /// <summary>Which reflex is in control, for the panel and the debug overlay.</summary>
        internal WingResolution Behaviour => resolution;

        /// <summary>
        /// What this wingman may shoot at, given what it is actually doing rather than what
        /// it was last told to do. The two differ whenever a reflex has the controls.
        /// </summary>
        internal OrderEngagementAuthority EngagementAuthority =>
            OrderRoePolicy.AuthorityFor(resolution.BehaviourId, Order);

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
            if (target == null || !IsCommandable) return;
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
            if (target == null || !IsCommandable) return;
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
            if (!IsCommandable) return;
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

            if (Fuel <= WingTuning.BingoFuel)
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

}
