namespace WingCommand
{
    /// <summary>The formation geometries a wing can be told to fly.</summary>
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

    /// <summary>
    /// Shared formation display names and the subset offered by the release UI.
    /// </summary>
    internal static class FormationShapes
    {
        public static readonly FormationShape[] All =
            (FormationShape[])System.Enum.GetValues(typeof(FormationShape));

        /// <summary>The compact release-facing set. Legacy shapes remain supported by the solver.</summary>
        public static readonly FormationShape[] Core =
        {
            FormationShape.EchelonRight,
            FormationShape.LineAbreast,
            FormationShape.Trail,
            FormationShape.CombatSpread,
            FormationShape.FingerFour,
            FormationShape.Vic,
        };

        /// <summary>Display name. Every shape gets one — that is the point of this file.</summary>
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
