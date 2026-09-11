namespace WingCommand
{
    /// <summary>Public standing movement/task order, separate from weapons policy and available to
    /// extension reflexes through WingSituation.</summary>
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

        /// <summary>Hold the slot while jamming a designated unit with a pod.</summary>
        JamTarget,

        /// <summary>Run one transient manoeuvre, then rejoin.</summary>
        Maneuver,

        /// <summary>Move to the map point, then enter autonomous combat. Append enum values to preserve
        /// host masks and label indices.</summary>
        SeekAndDestroy,

        /// <summary>Cancel the task and loiter near friendly territory. Keep the appended enum value
        /// stable for host masks.</summary>
        StandDown,
    }

    /// <summary>Scripted manoeuvres available as commands.</summary>
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

    /// <summary>Public wing weapons policy included in reflex telemetry.</summary>
    public enum WingRoe
    {
        Hold,
        Tight,
        Free,
    }

    internal enum OrderEngagementAuthority
    {
        /// <summary>Use standing ROE for incidental target selection and fire.</summary>
        StandingRoe,

        /// <summary>Use the explicit target directive for weapons authority.</summary>
        ExplicitTarget,

        /// <summary>Allow self-preservation only.</summary>
        DefensiveOnly,

        /// <summary>Delegate both flight and combat to native AI.</summary>
        AutonomousCombat,
    }

    /// <summary>Station-keeping weapons action for the current pass.</summary>
    internal enum StationFireMode
    {
        None,
        MissileDefence,
        DesignatedTarget,
        ProtectWing,
        Opportunity,
    }

    /// <summary>Order metadata for targeting, pursuit bounds, and delivery queuing.</summary>
    internal static class WingOrderRules
    {
        /// <summary>Hunting orders requiring a pursuit leash; Jam remains in formation.</summary>
        public static bool SendsWingmanHunting(WingOrder order) =>
            order == WingOrder.Engage || order == WingOrder.Attack || order == WingOrder.FireForEffect;

        /// <summary>Whether this order carries a unit designation, including slot jobs.</summary>
        public static bool CarriesTarget(WingOrder order) =>
            order == WingOrder.Attack ||
            order == WingOrder.FireForEffect ||
            order == WingOrder.JamTarget;

        /// <summary>Orders directed at the leader's formation slot.</summary>
        public static bool UsesFormationSlot(WingOrder order) =>
            order == WingOrder.Formation || order == WingOrder.JamTarget;

        /// <summary>Detect completed designations even while another behaviour suspends their flight
        /// state.</summary>
        public static bool TargetTaskComplete(WingOrder order, bool targetAlive, bool deliveryPending) =>
            !deliveryPending && CarriesTarget(order) && !targetAlive;

        /// <summary>Allow standing orders during pending delivery; reject manoeuvres that would expire
        /// during taxi.</summary>
        public static bool CanQueueWhilePending(WingOrder order) =>
            order != WingOrder.Maneuver;

        /// <summary>Terminal order after a point task: Move reforms, Seek and Destroy enters
        /// combat.</summary>
        public static WingOrder PointTaskCompletion(WingOrder order) =>
            order == WingOrder.SeekAndDestroy ? WingOrder.Engage : WingOrder.Formation;
    }

    /// <summary>Wing-command classification of the map pointer target.</summary>
    internal enum MapPointerKind
    {
        Empty,
        Enemy,
        Other,
    }

    /// <summary>Resolved action for a map right-click.</summary>
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

    /// <summary>Resolved action for a WMC order press.</summary>
    internal enum MapOrderButtonIntent
    {
        Arm,
        Disarm,
        ExecuteAndArm,
        CargoFallback,
        Ignore,
    }

    /// <summary>Left-click arms a WMC command; right-click applies it. Unarmed right-click defaults to
    /// Move.</summary>
    internal static class MapOrderPolicy
    {
        /// <summary>Orders available to arm for the next map right-click.</summary>
        public static bool ArmsOnMap(WingOrder order) =>
            PlacesPoint(order) || PicksTarget(order);

        /// <summary>Armed orders requiring a coordinate.</summary>
        public static bool PlacesPoint(WingOrder order) =>
            order == WingOrder.OrbitHere ||
            order == WingOrder.LandHere ||
            order == WingOrder.SeekAndDestroy ||
            order == WingOrder.DeliverCargo;

        /// <summary>Armed orders requiring a hostile cursor target.</summary>
        public static bool PicksTarget(WingOrder order) =>
            order == WingOrder.Attack;

        /// <summary>Orders eligible for queued follow-ons, including Hold once its orbit is
        /// reached.</summary>
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

    /// <summary>Engine-free weapons-authority precedence shared by runtime and tests.</summary>
    internal static class OrderRoePolicy
    {
        /// <summary>Resolve firing authority independently of retained targets; rejoin can retain Attack
        /// payload without attack permission. Missile defence precedes optional opportunity
        /// scans.</summary>
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

        /// <summary>Prefer active behaviour over standing order so recalled Engage members obey
        /// station-keeping ROE.</summary>
        public static OrderEngagementAuthority AuthorityFor(string behaviourId, WingOrder order, bool patrol = false)
        {
            if (patrol && order == WingOrder.MoveToPoint && behaviourId == WingBehaviours.Task)
                return OrderEngagementAuthority.StandingRoe;
            switch (behaviourId)
            {
                // Rejoin and overhead hold use standing ROE regardless of suspended task.
                case WingBehaviours.Rejoin:
                case WingBehaviours.DeckHold:
                    return OrderEngagementAuthority.StandingRoe;

                // Missile escape or native ownership permits defensive handling only.
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
