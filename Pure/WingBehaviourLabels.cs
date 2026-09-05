namespace WingCommand
{
    /// <summary>
    /// What to call a wingman that is not flying its order.
    ///
    /// The roster and the HUD both used to answer "what is this aircraft doing" by reading
    /// the standing order, with two special cases bolted on the front for a pending delivery
    /// and a missile break. Everything the arbiter added since — holding overhead while the
    /// leader lands, holding because there is no leader at all, rejoining from past the leash
    /// — was therefore invisible: the panel showed an order the wingman demonstrably was not
    /// carrying out.
    ///
    /// Returning null for the standing task is the point. A wingman flying what it was told
    /// to fly has nothing to add here, and the caller falls through to naming the order.
    /// </summary>
    internal static class WingBehaviourLabels
    {
        /// <summary>Four-or-so characters, for the HUD strip. Null when flying the order.</summary>
        public static string ShortCode(string behaviourId)
        {
            switch (behaviourId)
            {
                case WingBehaviours.Held:         return "DEPT";
                case WingBehaviours.MissileBreak: return "DEF";
                case WingBehaviours.DeckHold:     return WingHost.Current.DeckHoldShortCode ?? "HOLD";
                case WingBehaviours.TerrainAbort: return "PULL";
                case WingBehaviours.Rejoin:       return "RJN";
                default:                          return null;
            }
        }

        /// <summary>The roomier form, for the roster column. Null when flying the order.</summary>
        public static string Label(string behaviourId)
        {
            switch (behaviourId)
            {
                case WingBehaviours.Held:         return "DEPT";
                case WingBehaviours.MissileBreak: return "DEFENSIVE";
                case WingBehaviours.DeckHold:     return WingHost.Current.DeckHoldLabel ?? "HOLDING";
                case WingBehaviours.TerrainAbort: return "PULL UP";
                case WingBehaviours.Rejoin:       return "REJOIN";
                default:                          return null;
            }
        }
    }
}
