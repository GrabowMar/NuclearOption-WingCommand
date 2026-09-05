namespace WingCommand
{
    /// <summary>
    /// One source of player-facing manoeuvre names and the entry gates that keep a
    /// wingman from starting one it cannot finish. Engine-free so it compiles into the
    /// test project beside <see cref="FormationShapes"/>.
    /// </summary>
    internal static class ManeuverCatalog
    {
        /// <summary>Every manoeuvre, in menu order.</summary>
        public static readonly ManeuverKind[] All =
        {
            ManeuverKind.BreakLeft,
            ManeuverKind.BreakRight,
            ManeuverKind.SplitS,
            ManeuverKind.Immelmann,
            ManeuverKind.BarrelRoll,
            ManeuverKind.AileronRoll,
            ManeuverKind.Loop,
            ManeuverKind.WingWaggle,
            ManeuverKind.NotchThreat,
            ManeuverKind.MaskTerrain,
        };

        public static string Label(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.BreakLeft:   return "Break Left";
                case ManeuverKind.BreakRight:  return "Break Right";
                case ManeuverKind.SplitS:      return "Split-S";
                case ManeuverKind.Immelmann:   return "Immelmann";
                case ManeuverKind.BarrelRoll:  return "Barrel Roll";
                case ManeuverKind.AileronRoll: return "Aileron Roll";
                case ManeuverKind.Loop:        return "Loop";
                case ManeuverKind.WingWaggle:  return "Wing Waggle";
                case ManeuverKind.NotchThreat: return "Notch Threat";
                case ManeuverKind.MaskTerrain: return "Terrain Mask";
                default:                       return kind.ToString();
            }
        }

        public static string ShortLabel(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.BreakLeft:   return "BRK L";
                case ManeuverKind.BreakRight:  return "BRK R";
                case ManeuverKind.SplitS:      return "SPLIT-S";
                case ManeuverKind.Immelmann:   return "IMMEL";
                case ManeuverKind.BarrelRoll:  return "BARREL";
                case ManeuverKind.AileronRoll: return "ROLL";
                case ManeuverKind.Loop:        return "LOOP";
                case ManeuverKind.WingWaggle:  return "WAGGLE";
                case ManeuverKind.NotchThreat: return "NOTCH";
                case ManeuverKind.MaskTerrain: return "MASK";
                default:                       return kind.ToString().ToUpperInvariant();
            }
        }

        /// <summary>
        /// True when a helicopter or tiltrotor can fly this. Only the level breaks are
        /// safe on an airframe that has no energy to trade in the vertical; everything
        /// else reports "unable".
        /// </summary>
        public static bool RotaryCapable(ManeuverKind kind) =>
            kind == ManeuverKind.BreakLeft ||
            kind == ManeuverKind.BreakRight ||
            kind == ManeuverKind.WingWaggle ||
            kind == ManeuverKind.NotchThreat ||
            kind == ManeuverKind.MaskTerrain;

        /// <summary>
        /// Height above ground, in metres, a wingman must have before it will start the
        /// manoeuvre. Vertical manoeuvres that lose altitude need the most room; a level
        /// break or a waggle needs almost none.
        /// </summary>
        public static float MinEntryAltitudeAgl(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.SplitS:      return 1400f;
                case ManeuverKind.Loop:        return 900f;
                case ManeuverKind.BarrelRoll:  return 500f;
                case ManeuverKind.Immelmann:   return 400f;
                case ManeuverKind.AileronRoll: return 350f;
                case ManeuverKind.BreakLeft:
                case ManeuverKind.BreakRight:  return 120f;
                case ManeuverKind.NotchThreat: return 80f;
                case ManeuverKind.WingWaggle:  return 60f;
                case ManeuverKind.MaskTerrain: return 40f;
                default:                       return 400f;
            }
        }

        /// <summary>
        /// Airspeed, as a fraction of the airframe's own maximum, below which the
        /// manoeuvre is refused. A loop or a Split-S entered slow finishes in a stall.
        /// </summary>
        public static float MinEntrySpeedFraction(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.Loop:
                case ManeuverKind.Immelmann:  return 0.55f;
                case ManeuverKind.SplitS:
                case ManeuverKind.BarrelRoll: return 0.40f;
                case ManeuverKind.AileronRoll: return 0.30f;
                default:                       return 0.20f;
            }
        }

        /// <summary>Which way the level break turns: -1 left, +1 right, 0 for non-breaks.</summary>
        public static int BreakDirection(ManeuverKind kind)
        {
            if (kind == ManeuverKind.BreakLeft) return -1;
            if (kind == ManeuverKind.BreakRight) return 1;
            return 0;
        }
    }
}
