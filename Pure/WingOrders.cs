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
