namespace WingCommand
{
 /// <summary>Shared order labels and capability rules.</summary>
    internal static class WingOrderCatalog
    {
        public static string Label(WingOrder order)
        {
            // Apply host-specific order names centrally so every UI surface uses the same meaning.
            string host = WingHost.Current.LabelFor(order);
            if (host != null) return host;

            switch (order)
            {
                case WingOrder.Formation:    return "Form Up";
                case WingOrder.Attack:       return "Attack Target";
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
                case WingOrder.SeekAndDestroy: return "Seek and Destroy";
                case WingOrder.StandDown:    return "Stand Down";
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
                case WingOrder.Attack:       return "ATK TGT";
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
                case WingOrder.SeekAndDestroy: return "S&D";
                case WingOrder.StandDown:    return "WAIT";
                default:                     return order.ToString().ToUpperInvariant();
            }
        }

     /// <summary>Orders requiring a map point. Cargo accepts one but can use native route search
     /// without it.</summary>
        public static bool NeedsPoint(WingOrder order) =>
            order == WingOrder.OrbitHere || order == WingOrder.LandHere ||
            order == WingOrder.SeekAndDestroy;

     /// <summary>Orders that can arm a map coordinate placement.</summary>
        public static bool TakesPoint(WingOrder order) => MapOrderPolicy.PlacesPoint(order);

        public static bool CanApply(WingMember member, WingOrder order)
        {
            if (member == null || !member.Alive) return false;
            if (WingHost.Current.IsHidden(order)) return false;
            // Retain queueable standing orders during taxi for activation after takeoff.
            if (member.DeliveryPending && !WingOrderRules.CanQueueWhilePending(order)) return false;
            if (order == WingOrder.DeliverCargo) return member.CanDeliverCargo;
            if (order == WingOrder.LandHere) return member.CanLandInPlace;
            if (order == WingOrder.SeekAndDestroy && member.IsSurface) return false;
            if (order == WingOrder.JamTarget)
                return WingFidelity.Jamming && member.CanJam;
            if (order == WingOrder.Maneuver) return WingFidelity.Manoeuvres && !member.IsPanicking;
            return true;
        }

     /// <summary>Whether the host exposes this order before a member selection exists; radial
     /// construction uses this scope-independent check.</summary>
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
            if (order == WingOrder.SeekAndDestroy) return "Seek and Destroy is available to aircraft only";
            if (order == WingOrder.JamTarget)
                return WingFidelity.Jamming
                    ? "No selected wingman has a jammer pod"
                    : "Jamming is off in Performance mode";
            if (order == WingOrder.Maneuver)
                return WingFidelity.Manoeuvres
                    ? "No selected wingman can manoeuvre right now"
                    : "Manoeuvres are off in Performance mode";
            return "No selected wingman can carry out that order";
        }
    }
}
