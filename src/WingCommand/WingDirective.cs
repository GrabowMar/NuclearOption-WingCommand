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
        public readonly float IssuedAt;

        private WingDirective(WingOrder order, Unit target, GlobalPosition point, bool hasPoint)
        {
            Order = order;
            Target = target;
            Point = point;
            HasPoint = hasPoint;
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

        public WingDirective WithoutTarget() =>
            new WingDirective(Order, null, Point, HasPoint);
    }
}
