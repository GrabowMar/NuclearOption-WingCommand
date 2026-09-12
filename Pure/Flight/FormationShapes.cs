namespace WingCommand
{
    /// <summary>Commandable formation geometries.</summary>
    internal enum FormationShape
    {
        EchelonRight,
        EchelonLeft,
        LineAbreast,
        Trail,
        CombatSpread,
        FingerFour,
        Vic,
        Diamond,
        Ladder,
        Wall,
    }

    /// <summary>Shared shape names and release-menu selection.</summary>
    internal static class FormationShapes
    {
        public static readonly FormationShape[] All =
            (FormationShape[])System.Enum.GetValues(typeof(FormationShape));

        /// <summary>Compact release-menu shapes; the solver still accepts legacy values.</summary>
        public static readonly FormationShape[] Core =
        {
            FormationShape.EchelonRight,
            FormationShape.LineAbreast,
            FormationShape.Trail,
            FormationShape.CombatSpread,
            FormationShape.FingerFour,
            FormationShape.Vic,
        };

        /// <summary>Display label for every supported shape.</summary>
        public static string Pretty(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "Echelon Right";
                case FormationShape.EchelonLeft:  return "Echelon Left";
                case FormationShape.LineAbreast:  return "Line Abreast";
                case FormationShape.Trail:        return "Trail";
                case FormationShape.CombatSpread: return "Combat Spread";
                case FormationShape.FingerFour:   return "Finger Four";
                case FormationShape.Vic:          return "Vic";
                case FormationShape.Diamond:      return "Diamond";
                case FormationShape.Ladder:       return "Ladder";
                case FormationShape.Wall:         return "Wall";
                default:                          return shape.ToString();
            }
        }

        /// <summary>Brief tactical doctrine role for every shape.</summary>
        public static string Role(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonRight: return "Off-axis radar & free egress right";
                case FormationShape.EchelonLeft:  return "Off-axis radar & free egress left";
                case FormationShape.LineAbreast:  return "Frontal sweep & simultaneous lock";
                case FormationShape.Trail:        return "Narrow corridor & terrain masking";
                case FormationShape.CombatSpread: return "Mutual missile defense & wide scan";
                case FormationShape.FingerFour:   return "Two-element paired combat sweep";
                case FormationShape.Vic:          return "Flight symmetry & leader cohesion";
                case FormationShape.Diamond:      return "Tight perimeter & concentrated mass";
                case FormationShape.Ladder:       return "Layered altitude & stepped escort";
                case FormationShape.Wall:         return "Full-aspect barrier combat sweep";
                default:                          return "Standard tactical formation";
            }
        }
    }
}
