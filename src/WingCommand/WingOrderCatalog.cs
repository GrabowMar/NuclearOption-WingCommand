namespace WingCommand
{
    /// <summary>One source of player-facing order names and capability metadata.</summary>
    internal static class WingOrderCatalog
    {
        public static string Label(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation:    return "Form Up";
                case WingOrder.Attack:       return "Attack";
                case WingOrder.FireForEffect: return "Splash 'Em";
                case WingOrder.Engage:       return "Engage";
                case WingOrder.OrbitHere:    return "Hold";
                case WingOrder.FallBack:     return "Disengage";
                case WingOrder.ReturnToBase: return "RTB";
                case WingOrder.DeliverCargo: return "Deliver Cargo";
                case WingOrder.LandHere:     return "Land";
                case WingOrder.MoveToPoint:  return "Move";
                default:                     return order.ToString();
            }
        }

        public static string ShortLabel(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation:    return "FORM";
                case WingOrder.Attack:       return "ATTACK";
                case WingOrder.FireForEffect: return "SPLASH";
                case WingOrder.Engage:       return "ENGAGE";
                case WingOrder.OrbitHere:    return "HOLD";
                case WingOrder.FallBack:     return "DISENG";
                case WingOrder.ReturnToBase: return "RTB";
                case WingOrder.DeliverCargo: return "CARGO";
                case WingOrder.LandHere:     return "LAND";
                case WingOrder.MoveToPoint:  return "MOVE";
                default:                     return order.ToString().ToUpperInvariant();
            }
        }

        /// <summary>
        /// Orders that cannot be issued without a map point at all.
        ///
        /// Deliberately narrower than <see cref="TakesPoint"/>: a cargo run may be given a
        /// drop point, and is still a complete order without one, because the stock
        /// transport behaviour will go and find somewhere itself.
        /// </summary>
        public static bool NeedsPoint(WingOrder order) =>
            order == WingOrder.OrbitHere || order == WingOrder.LandHere;

        /// <summary>Orders the map cursor may be armed for.</summary>
        public static bool TakesPoint(WingOrder order) =>
            NeedsPoint(order) || order == WingOrder.DeliverCargo;

        /// <summary>True when the order prosecutes a designated unit rather than a place.</summary>
        public static bool IsTargetOrder(WingOrder order) =>
            order == WingOrder.Attack || order == WingOrder.FireForEffect;

        public static bool CanApply(WingMember member, WingOrder order)
        {
            if (member == null || !member.IsCommandable) return false;
            if (order == WingOrder.DeliverCargo) return member.CanDeliverCargo;
            if (order == WingOrder.LandHere) return member.CanLandInPlace;
            return true;
        }

        public static string UnavailableReason(WingOrder order)
        {
            if (order == WingOrder.DeliverCargo) return "No selected wingman is carrying cargo";
            if (order == WingOrder.FireForEffect) return "No selected wingman can prosecute that target";
            if (order == WingOrder.LandHere) return "Land is available to rotary aircraft only";
            return "No selected wingman can carry out that order";
        }
    }
}
