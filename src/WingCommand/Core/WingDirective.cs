using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The complete standing intent for one wingman. The old model stored only a
    /// <see cref="WingOrder"/> and kept a target in a second field while point orders
    /// reconstructed their destination from the leader. Keeping the payload beside the
    /// order is what lets a directive survive defensive interruptions and scoped tasking.
    /// </summary>
    internal readonly struct WingDirective
    {
        public readonly WingOrder Order;
        public readonly Unit Target;
        public readonly GlobalPosition Point;
        public readonly bool HasPoint;

        /// <summary>Which manoeuvre to fly. Only meaningful when <see cref="Order"/> is Maneuver.</summary>
        public readonly ManeuverKind Maneuver;

        public readonly float IssuedAt;

        private WingDirective(WingOrder order, Unit target, GlobalPosition point, bool hasPoint,
                              ManeuverKind maneuver = ManeuverKind.WingWaggle)
        {
            Order = order;
            Target = target;
            Point = point;
            HasPoint = hasPoint;
            Maneuver = maneuver;
            IssuedAt = Time.timeSinceLevelLoad;
        }

        public static WingDirective Simple(WingOrder order) =>
            new WingDirective(order, null, default(GlobalPosition), false);

        public static WingDirective Attack(Unit target) => AtTarget(WingOrder.Attack, target);

        /// <summary>
        /// Any order that prosecutes a specific unit. Attack and Splash 'Em differ in
        /// how hard they press, not in what they carry, so they share this payload.
        /// </summary>
        public static WingDirective AtTarget(WingOrder order, Unit target) =>
            new WingDirective(order, target, default(GlobalPosition), false);

        public static WingDirective AtPoint(WingOrder order, GlobalPosition point) =>
            new WingDirective(order, null, point, true);

        /// <summary>Fly one scripted manoeuvre. Carries no target or point.</summary>
        public static WingDirective RunManeuver(ManeuverKind kind) =>
            new WingDirective(WingOrder.Maneuver, null, default(GlobalPosition), false, kind);

        public WingDirective WithoutTarget() =>
            new WingDirective(Order, null, Point, HasPoint, Maneuver);

        /// <summary>
        /// Whether this asks for the same thing as another directive. <see cref="IssuedAt"/>
        /// is deliberately excluded — it records when the order was given, not what it was.
        ///
        /// Exists so re-issuing an order a wingman is already carrying out is free. The wing
        /// re-applies Formation to every member on several paths (a partial attack order, a
        /// leader restored after a takeover), and without this each one re-enters the
        /// formation state: leader filters reset, and the rejoin boost fires. Ordering an
        /// attack to half a four-ship made the other half surge and re-settle for no reason.
        /// </summary>
        public bool SameIntentAs(in WingDirective other) =>
            Order == other.Order &&
            ReferenceEquals(Target, other.Target) &&
            HasPoint == other.HasPoint &&
            Maneuver == other.Maneuver &&
            (!HasPoint || SamePoint(Point, other.Point));

        /// <summary>
        /// Map points are compared with a tolerance rather than exactly: a click a metre
        /// from the last one is the same instruction, and a float comparison on a world
        /// coordinate would say otherwise.
        /// </summary>
        private static bool SamePoint(GlobalPosition a, GlobalPosition b) =>
            FastMath.SquareDistance(a, b) < WingTuning.SamePointMetres * WingTuning.SamePointMetres;
    }
}
