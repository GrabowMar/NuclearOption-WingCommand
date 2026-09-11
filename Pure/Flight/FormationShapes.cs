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
    }
}
