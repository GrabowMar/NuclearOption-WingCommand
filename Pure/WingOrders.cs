namespace WingCommand
{
    /// <summary>
    /// Standing movement/task order, separate from weapons policy.
    /// Public because third-party reflexes inspect it through <see cref="WingSituation"/>.
    /// </summary>
    public enum WingOrder
    {
        Formation,
        Engage,
        ReturnToBase,
        FallBack,
        OrbitHere,
        DeliverCargo,
        LandHere,
        Attack,
        FireForEffect,
        MoveToPoint,

        /// <summary>Hold the formation slot, but run the jammer pod against a designated unit.</summary>
        JamTarget,

        /// <summary>Fly one scripted manoeuvre, then rejoin. Transient: never a resting state.</summary>
        Maneuver,

        /// <summary>
        /// Fly to a player-marked map point, then hand the aircraft to autonomous combat.
        ///
        /// Appended deliberately: host profiles address orders by their enum value, so
        /// inserting this among the existing orders would reinterpret their masks and
        /// label tables.
        /// </summary>
        SeekAndDestroy,

        /// <summary>
        /// Cancel the standing task and loiter near friendly territory. Appended so host
        /// profiles that address orders by enum value keep their existing masks.
        /// </summary>
        StandDown,
    }

    /// <summary>The scripted manoeuvres a wingman can be told to fly on command.</summary>
    internal enum ManeuverKind
    {
        BreakLeft,
        BreakRight,
        SplitS,
        Immelmann,
        BarrelRoll,
        AileronRoll,
        Loop,
        WingWaggle,
        NotchThreat,
        MaskTerrain,
    }

    /// <summary>
    /// Standing weapons policy for the wing. Public for the same reason as
    /// <see cref="WingOrder"/>: it is part of the situation a reflex scores against.
    /// </summary>
    public enum WingRoe
    {
        Hold,
        Tight,
        Free,
    }

    internal enum OrderEngagementAuthority
    {
        /// <summary>The standing ROE may select and fire on incidental targets.</summary>
        StandingRoe,

        /// <summary>The player-designated target and order own weapons employment.</summary>
        ExplicitTarget,

        /// <summary>The task allows self-preservation only, not opportunity fire.</summary>
        DefensiveOnly,

        /// <summary>The order hands flying and fighting to the combat AI.</summary>
        AutonomousCombat,
    }

    /// <summary>One weapons task selected for a station-keeping controller this pass.</summary>
    internal enum StationFireMode
    {
        None,
        MissileDefence,
        DesignatedTarget,
        ProtectWing,
        Opportunity,
    }

    /// <summary>
    /// Shared order metadata for targeting, pursuit leashes, and pending deliveries.
    /// </summary>
    internal static class WingOrderRules
    {
        /// <summary>
        /// Orders that leave formation and need a pursuit leash. Only Jam stays in its slot.
        /// </summary>
        public static bool SendsWingmanHunting(WingOrder order) =>
            order == WingOrder.Engage || order == WingOrder.Attack || order == WingOrder.FireForEffect;

        /// <summary>
        /// Whether the directive carries a designated unit, including slot-based orders.
        /// </summary>
        public static bool CarriesTarget(WingOrder order) =>
            order == WingOrder.Attack ||
            order == WingOrder.FireForEffect ||
            order == WingOrder.JamTarget;

        /// <summary>Orders whose destination is the leader's formation slot.</summary>
        public static bool UsesFormationSlot(WingOrder order) =>
            order == WingOrder.Formation || order == WingOrder.JamTarget;

        /// <summary>A completed designation must not wait for a suspended task to regain flight.</summary>
        public static bool TargetTaskComplete(WingOrder order, bool targetAlive, bool deliveryPending) =>
            !deliveryPending && CarriesTarget(order) && !targetAlive;

        /// <summary>
        /// Pending deliveries retain standing orders until airborne. Transient manoeuvres
        /// cannot be queued because they would expire during taxi.
        /// </summary>
        public static bool CanQueueWhilePending(WingOrder order) =>
            order != WingOrder.Maneuver;

        /// <summary>
        /// The standing order to take after a one-point ingress ends. Ordinary map moves
        /// are temporary and reform; Seek and Destroy is deliberately a move followed by
        /// autonomous combat.
        /// </summary>
        public static WingOrder PointTaskCompletion(WingOrder order) =>
            order == WingOrder.SeekAndDestroy ? WingOrder.Engage : WingOrder.Formation;
    }

    /// <summary>
    /// What a map pointer is sitting on, from Wing Command's point of view.
    /// </summary>
    internal enum MapPointerKind
    {
        Empty,
        Enemy,
        Other,
    }

    /// <summary>What a tactical-map right-click should do.</summary>
    internal enum MapClickIntent
    {
        Move,
        QueueMove,
        PlacePoint,
        QueuePlacePoint,
        AttackTarget,
        QueueAttackTarget,
        NeedTarget,
        Ignore,
    }

    /// <summary>What a WMC order-button press should do.</summary>
    internal enum MapOrderButtonIntent
    {
        Arm,
        Disarm,
        ExecuteAndArm,
        CargoFallback,
        Ignore,
    }

    /// <summary>
    /// Tactical map order UX: left-click arms a highlighted WMC order, right-click
    /// applies it. With nothing armed, right-click is a move.
    /// </summary>
    internal static class MapOrderPolicy
    {
        /// <summary>Orders the WMC grid can arm as the next map right-click.</summary>
        public static bool ArmsOnMap(WingOrder order) =>
            PlacesPoint(order) || PicksTarget(order);

        /// <summary>Armed orders that consume a map coordinate.</summary>
        public static bool PlacesPoint(WingOrder order) =>
            order == WingOrder.OrbitHere ||
            order == WingOrder.LandHere ||
            order == WingOrder.SeekAndDestroy ||
            order == WingOrder.DeliverCargo;

        /// <summary>Armed orders that consume a hostile under the cursor.</summary>
        public static bool PicksTarget(WingOrder order) =>
            order == WingOrder.Attack;

        /// <summary>
        /// Standing orders a Shift-queued map task can wait behind. Open-ended holds still
        /// qualify: arriving at the orbit with more to do advances the queue rather than
        /// CAP-ing forever.
        /// </summary>
        public static bool CanFollowOn(WingOrder order) =>
            order == WingOrder.MoveToPoint ||
            order == WingOrder.SeekAndDestroy ||
            order == WingOrder.OrbitHere ||
            order == WingOrder.LandHere ||
            order == WingOrder.DeliverCargo ||
            order == WingOrder.Attack ||
            order == WingOrder.FireForEffect ||
            order == WingOrder.JamTarget;

        public static MapClickIntent ResolveRightClick(bool orderArmed, WingOrder armedOrder,
                                                       MapPointerKind pointer, bool shift)
        {
            if (!orderArmed)
                return shift ? MapClickIntent.QueueMove : MapClickIntent.Move;

            if (PicksTarget(armedOrder))
            {
                if (pointer != MapPointerKind.Enemy) return MapClickIntent.NeedTarget;
                return shift ? MapClickIntent.QueueAttackTarget : MapClickIntent.AttackTarget;
            }

            if (PlacesPoint(armedOrder))
                return shift ? MapClickIntent.QueuePlacePoint : MapClickIntent.PlacePoint;

            return MapClickIntent.Ignore;
        }

        public static float DefaultMoveAltitude(bool rotary) =>
            rotary ? WingTuning.MoveAltitudeRotary : WingTuning.MoveAltitudeFixed;

        public static float StepMoveAltitude(float currentOrZero, int sign, bool rotary)
        {
            float current = currentOrZero > 0f ? currentOrZero : DefaultMoveAltitude(rotary);
            if (sign == 0) return current;

            float step = rotary ? WingTuning.MoveAltitudeStepRotary : WingTuning.MoveAltitudeStepFixed;
            float min = rotary ? WingTuning.MoveAltitudeMinRotary : WingTuning.MoveAltitudeMinFixed;
            float max = rotary ? WingTuning.MoveAltitudeMaxRotary : WingTuning.MoveAltitudeMaxFixed;
            float next = current + sign * step;
            if (next < min) return min;
            if (next > max) return max;
            return next;
        }

        public static float DefaultMoveSpeed() => WingTuning.MoveSpeedDefault;

        public static float StepMoveSpeed(float currentOrZero, int sign)
        {
            float current = currentOrZero > 0f ? currentOrZero : DefaultMoveSpeed();
            if (sign == 0) return current;
            float next = current + sign * WingTuning.MoveSpeedStep;
            if (next < WingTuning.MoveSpeedMin) return WingTuning.MoveSpeedMin;
            if (next > WingTuning.MoveSpeedMax) return WingTuning.MoveSpeedMax;
            return next;
        }

        public static MapOrderButtonIntent ResolveButton(WingOrder order, bool alreadyArmedWithThis,
                                                         bool hasPlayerTargets)
        {
            if (!ArmsOnMap(order)) return MapOrderButtonIntent.Ignore;

            if (alreadyArmedWithThis)
            {
                return order == WingOrder.DeliverCargo
                    ? MapOrderButtonIntent.CargoFallback
                    : MapOrderButtonIntent.Disarm;
            }

            if (order == WingOrder.Attack && hasPlayerTargets)
                return MapOrderButtonIntent.ExecuteAndArm;

            return MapOrderButtonIntent.Arm;
        }

        public static string ArmPrompt(WingOrder order)
        {
            if (order == WingOrder.Attack)
                return "ATTACK TARGET ARMED · RIGHT-CLICK A HOSTILE · SHIFT QUEUES";
            if (order == WingOrder.DeliverCargo)
                return "DELIVER CARGO ARMED · RIGHT-CLICK MAP · SHIFT QUEUES, OR PRESS AGAIN FOR THE STANDARD ROUTE";
            if (order == WingOrder.OrbitHere)
                return "HOLD HERE ARMED · RIGHT-CLICK MAP · SHIFT QUEUES";
            if (order == WingOrder.SeekAndDestroy)
                return "SEEK & DESTROY ARMED · RIGHT-CLICK MAP · SHIFT QUEUES";
            if (order == WingOrder.LandHere)
                return "LAND ARMED · RIGHT-CLICK MAP · SHIFT QUEUES";
            return "ARMED · RIGHT-CLICK MAP · SHIFT QUEUES";
        }
    }

    /// <summary>Pure precedence table shared by runtime code and tests.</summary>
    internal static class OrderRoePolicy
    {
        /// <summary>
        /// Resolve weapons intent independently of any retained target payload. A temporary
        /// rejoin can keep an Attack target without inheriting permission to fire at it.
        /// Missile defence takes priority even when optional opportunity scans are disabled.
        /// </summary>
        public static StationFireMode StationFire(OrderEngagementAuthority authority,
            WingRoe roe, bool missileDefenceAvailable, bool opportunityFireEnabled)
        {
            if (missileDefenceAvailable) return StationFireMode.MissileDefence;
            if (authority == OrderEngagementAuthority.ExplicitTarget)
                return StationFireMode.DesignatedTarget;
            if (authority == OrderEngagementAuthority.AutonomousCombat)
                return StationFireMode.Opportunity;
            if (authority != OrderEngagementAuthority.StandingRoe || !opportunityFireEnabled)
                return StationFireMode.None;

            if (roe == WingRoe.Tight) return StationFireMode.ProtectWing;
            if (roe == WingRoe.Free) return StationFireMode.Opportunity;
            return StationFireMode.None;
        }

        public static OrderEngagementAuthority Authority(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation:
                case WingOrder.OrbitHere:
                case WingOrder.StandDown:
                    return OrderEngagementAuthority.StandingRoe;

                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                    return OrderEngagementAuthority.ExplicitTarget;

                case WingOrder.Engage:
                    return OrderEngagementAuthority.AutonomousCombat;

                default:
                    return OrderEngagementAuthority.DefensiveOnly;
            }
        }

        /// <summary>
        /// Uses the active behaviour before the standing order. A recalled Engage wingman
        /// must obey station-keeping ROE while rejoining, rather than retain autonomous fire.
        /// </summary>
        public static OrderEngagementAuthority AuthorityFor(string behaviourId, WingOrder order)
        {
            switch (behaviourId)
            {
                // Rejoining and holding overhead are both station-keeping, whatever the
                // order underneath them says. The standing ROE governs, as it does for any
                // other wingman flying its slot.
                case WingBehaviours.Rejoin:
                case WingBehaviours.DeckHold:
                    return OrderEngagementAuthority.StandingRoe;

                // Running from a missile, or not ours to fly at all.
                case WingBehaviours.MissileBreak:
                case WingBehaviours.TerrainAbort:
                case WingBehaviours.Held:
                    return OrderEngagementAuthority.DefensiveOnly;

                default:
                    return Authority(order);
            }
        }
    }
}
