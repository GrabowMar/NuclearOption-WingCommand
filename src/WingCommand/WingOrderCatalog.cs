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
                case WingOrder.JamTarget:    return "Jam";
                case WingOrder.Maneuver:     return "Manoeuvre";
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
                case WingOrder.JamTarget:    return "JAM";
                case WingOrder.Maneuver:     return "MNVR";
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

        /// <summary>
        /// True when the order prosecutes a designated unit with weapons. Drives the
        /// leash and the attack-resume path, so Jam Target is deliberately excluded: it
        /// carries a target but is flown as station-keeping, not pursuit.
        /// </summary>
        public static bool IsTargetOrder(WingOrder order) =>
            order == WingOrder.Attack || order == WingOrder.FireForEffect;

        /// <summary>True when the directive holds a designated unit at all, weapons or not.</summary>
        public static bool CarriesTarget(WingOrder order) =>
            IsTargetOrder(order) || order == WingOrder.JamTarget;

        public static bool CanApply(WingMember member, WingOrder order)
        {
            if (member == null || !member.IsCommandable) return false;
            if (order == WingOrder.DeliverCargo) return member.CanDeliverCargo;
            if (order == WingOrder.LandHere) return member.CanLandInPlace;
            if (order == WingOrder.JamTarget)
                return WingBrain.Jamming && Plugin.Config2.JammingEnabled.Value && member.CanJam;
            if (order == WingOrder.Maneuver) return WingBrain.Manoeuvres;
            return true;
        }

        public static string UnavailableReason(WingOrder order)
        {
            if (order == WingOrder.DeliverCargo) return "No selected wingman is carrying cargo";
            if (order == WingOrder.FireForEffect) return "No selected wingman can prosecute that target";
            if (order == WingOrder.LandHere) return "Land is available to rotary aircraft only";
            if (order == WingOrder.JamTarget)
                return WingBrain.Jamming
                    ? "No selected wingman has a radar jammer"
                    : "Jamming is off in Performance mode";
            if (order == WingOrder.Maneuver) return "Manoeuvres are off in Performance mode";
            return "No selected wingman can carry out that order";
        }
    }
}
