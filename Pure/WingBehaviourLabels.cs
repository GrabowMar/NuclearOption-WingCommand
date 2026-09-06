namespace WingCommand
{
    /// <summary>
    /// HUD and roster labels for behaviours that override the standing task.
    /// Returns null when the caller should display the order instead.
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
