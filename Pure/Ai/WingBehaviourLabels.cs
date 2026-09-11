namespace WingCommand
{
    /// <summary>Override-behaviour labels for HUD and roster; null tells callers to display the standing
    /// order.</summary>
    internal static class WingBehaviourLabels
    {
        /// <summary>Compact HUD behaviour code, or null for the standing task.</summary>
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

        /// <summary>Full roster behaviour label, or null for the standing task.</summary>
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
