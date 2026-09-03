namespace WingCommand
{
    /// <summary>One source of player-facing order names and capability metadata.</summary>
    internal static class WingOrderCatalog
    {
        public static string Label(WingOrder order)
        {
            // A companion plugin describing a non-aircraft host renames what an order means
            // from its seat - "Form Up" is not a thing a jet can do on a moving warship.
            // Checked here rather than at each surface because this is the one place every
            // surface reads an order's name from.
            string host = WingHost.Current.LabelFor(order);
            if (host != null) return host;

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
            string host = WingHost.Current.ShortLabelFor(order);
            if (host != null) return host;

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

        public static bool CanApply(WingMember member, WingOrder order)
        {
            if (member == null || !member.Alive) return false;
            if (WingHost.Current.IsHidden(order)) return false;
            // A taxiing delivery can accept a standing order; it flies it once airborne.
            if (member.DeliveryPending && !WingOrderRules.CanQueueWhilePending(order)) return false;
            if (order == WingOrder.DeliverCargo) return member.CanDeliverCargo;
            if (order == WingOrder.LandHere) return member.CanLandInPlace;
            if (order == WingOrder.JamTarget)
                return WingBrain.Jamming && member.CanJam;
            if (order == WingOrder.Maneuver) return WingBrain.Manoeuvres;
            return true;
        }

        /// <summary>
        /// Whether an order may be offered at all, with no wingman in hand.
        ///
        /// The radial wheel builds its slices before there is a selection to test, so it
        /// cannot ask <see cref="CanApply"/>. This answers the half of that question which
        /// does not depend on who is being ordered.
        /// </summary>
        public static bool IsOfferable(WingOrder order) => !WingHost.Current.IsHidden(order);

        public static string UnavailableReason(WingOrder order)
        {
            if (WingHost.Current.IsHidden(order))
            {
                return WingHost.Current.HiddenReason ??
                       "That order does not apply from your current vehicle";
            }

            if (order == WingOrder.DeliverCargo) return "No selected wingman is carrying cargo";
            if (order == WingOrder.FireForEffect) return "No selected wingman can prosecute that target";
            if (order == WingOrder.LandHere) return "Land is available to rotary aircraft only";
            if (order == WingOrder.JamTarget)
                return WingBrain.Jamming
                    ? "No selected wingman has a jammer pod"
                    : "Jamming is off in Performance mode";
            if (order == WingOrder.Maneuver) return "Manoeuvres are off in Performance mode";
            return "No selected wingman can carry out that order";
        }
    }
}
