using UnityEngine;

namespace WingCommand
{
    /// <summary>Standing order and payload kept together so intent survives defensive interruptions and
    /// scoped commands.</summary>
    internal readonly struct WingDirective
    {
        public readonly WingOrder Order;
        public readonly Unit Target;
        public readonly System.Collections.Generic.IReadOnlyList<Unit> Targets;
        public readonly GlobalPosition Point;
        public readonly bool HasPoint;

        /// <summary>Scripted manoeuvre, used only when Order is Maneuver.</summary>
        public readonly ManeuverKind Maneuver;

        private WingDirective(WingOrder order, Unit target, GlobalPosition point, bool hasPoint,
                              ManeuverKind maneuver = ManeuverKind.WingWaggle,
                              System.Collections.Generic.IReadOnlyList<Unit> targets = null)
        {
            Order = order;
            Target = target;
            Targets = targets;
            Point = point;
            HasPoint = hasPoint;
            Maneuver = maneuver;
        }

        public static WingDirective Simple(WingOrder order) =>
            new WingDirective(order, null, default(GlobalPosition), false);

        public static WingDirective Attack(Unit target) => AtTarget(WingOrder.Attack, target);

        /// <summary>Create a unit-target directive shared by Attack and Splash orders.</summary>
        public static WingDirective AtTarget(WingOrder order, Unit target) =>
            order == WingOrder.FireForEffect ? Splash(new[] { target }, target)
                : new WingDirective(order, target, default(GlobalPosition), false);

        public static WingDirective Splash(System.Collections.Generic.IReadOnlyList<Unit> targets, Unit first)
        {
            // The HUD reuses its target list; the order must own its snapshot.
            var snapshot = new System.Collections.Generic.List<Unit>();
            foreach (Unit target in targets)
                if (target != null && !target.disabled && !snapshot.Contains(target)) snapshot.Add(target);
            return new WingDirective(WingOrder.FireForEffect, first, default, false,
                targets: snapshot.AsReadOnly());
        }

        public WingDirective Retarget(Unit target) =>
            new WingDirective(Order, target, Point, HasPoint, Maneuver, Targets);

        public static WingDirective AtPoint(WingOrder order, GlobalPosition point) =>
            new WingDirective(order, null, point, true);

        /// <summary>Create a point task that enters autonomous combat on arrival instead of
        /// reforming.</summary>
        public static WingDirective SeekAndDestroy(GlobalPosition point) =>
            AtPoint(WingOrder.SeekAndDestroy, point);

        /// <summary>Create a single manoeuvre directive without a target or point.</summary>
        public static WingDirective RunManeuver(ManeuverKind kind) =>
            new WingDirective(WingOrder.Maneuver, null, default(GlobalPosition), false, kind);

        /// <summary>Compare intent so repeated orders do not re-enter states, reset leader filters, or
        /// restart rejoin boost.</summary>
        public bool SameIntentAs(in WingDirective other) =>
            Order == other.Order &&
            ReferenceEquals(Target, other.Target) &&
            ReferenceEquals(Targets, other.Targets) &&
            HasPoint == other.HasPoint &&
            Maneuver == other.Maneuver &&
            (!HasPoint || SamePoint(Point, other.Point));

        /// <summary>Compare map points with tolerance so nearby clicks represent the same
        /// instruction.</summary>
        private static bool SamePoint(GlobalPosition a, GlobalPosition b) =>
            FastMath.SquareDistance(a, b) < WingTuning.SamePointMetres * WingTuning.SamePointMetres;
    }
}
