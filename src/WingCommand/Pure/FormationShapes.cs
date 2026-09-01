using System;

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
    /// One place that knows about the <see cref="FormationShape"/> enum: its values, how
    /// to name a shape for a human, and how to step through them.
    ///
    /// All three lived in triplicate before — once in the radial menu, once in the WMC
    /// screen and once in the map overlay — and the duplication is exactly why they went
    /// stale. Adding five shapes meant editing three identical switch statements, none of
    /// them were touched, and every one of the new shapes displayed as a run-together enum
    /// name in every menu that offered it.
    /// </summary>
    internal static class FormationShapes
    {
        /// <summary>
        /// Every shape, resolved once. <c>Enum.GetValues</c> allocates a fresh array on
        /// each call and was being called from UI paint paths.
        /// </summary>
        public static readonly FormationShape[] All =
            (FormationShape[])Enum.GetValues(typeof(FormationShape));

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

        /// <summary>Step to the next or previous shape, wrapping at both ends.</summary>
        public static FormationShape Cycle(FormationShape from, int direction)
        {
            int index = Array.IndexOf(All, from);
            if (index < 0) return All[0];

            int next = (index + direction) % All.Length;
            if (next < 0) next += All.Length;
            return All[next];
        }

        /// <summary>Cycle only the six distinct shapes exposed by the release UI.</summary>
        public static FormationShape CycleCore(FormationShape from, int direction)
        {
            int index = Array.IndexOf(Core, from);
            if (index < 0) return direction < 0 ? Core[Core.Length - 1] : Core[0];

            int next = (index + direction) % Core.Length;
            if (next < 0) next += Core.Length;
            return Core[next];
        }
    }
}
