using System;

namespace WingCommand
{
    /// <summary>
    /// One slot in formation units: lateral (+ right), back (+ astern), height (+ up).
    /// The adapter multiplies lateral/back by spacing and height by vertical stack.
    /// </summary>
    internal readonly struct SlotLayout
    {
        public readonly float Lateral;
        public readonly float Back;
        public readonly float Height;

        public SlotLayout(float lateral, float back, float height)
        {
            Lateral = lateral;
            Back = back;
            Height = height;
        }
    }

    /// <summary>
    /// Aircraft formations with stable lanes and repeatable element spacing. Fixed
    /// sweep keeps extended echelons on their assigned side; tactical shapes grow aft
    /// in elements rather than demanding ever larger outside-turn speeds. Every shape
    /// has horizontal clearance even without its stack (surface units and terrain floors).
    /// </summary>
    internal static class FormationLayout
    {
        // Shared with flight's turn deformation and checked by geometry regressions.
        internal const float TurnLateralScale = 0.72f;
        internal const float TurnBackScale = 1.12f;
        internal const float MinimumPlanarSeparation = 0.75f;

        public static SlotLayout Slot(FormationShape shape, int slot)
        {
            if (slot <= 0) return new SlotLayout(0f, 0f, 0f);

            int rank = (slot - 1) / 2 + 1;
            float side = slot % 2 == 1 ? 1f : -1f;
            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    return new SlotLayout(-0.90f * slot, 0.80f * slot, StepDown(slot));
                case FormationShape.EchelonRight:
                default:
                    return new SlotLayout(0.90f * slot, 0.80f * slot, StepDown(slot));

                case FormationShape.LineAbreast:
                    // All wingmen remain abeam instead of curving progressively into trail.
                    return new SlotLayout(side * 1.10f * rank, 0f, -0.15f);
                case FormationShape.Trail:
                    return new SlotLayout(0f, 1.10f * slot, StepDown(slot));
                case FormationShape.Vic:
                    return new SlotLayout(side * 0.95f * rank, 0.80f * rank, StepDown(rank));

                case FormationShape.CombatSpread:
                    return CombatSpread(slot);
                case FormationShape.FingerFour:
                    return FingerFour(slot);
                case FormationShape.Diamond:
                    return Diamond(slot);

                case FormationShape.Ladder:
                    // A deliberate climbing trail; unlike other shapes, altitude is its identity.
                    return new SlotLayout(0f, 1.10f * slot, 1.55f * slot);
                case FormationShape.Wall:
                    return new SlotLayout(side * 1.55f * rank, 0f, Math.Min(0.75f * rank, 3f));
            }
        }

        // Keep a large wing near its leader's altitude; a terrain clamp must not erase
        // the only separation between slots or make the last aircraft chase a deep staircase.
        private static float StepDown(int rank) => -0.25f * Math.Min(rank, 4);

        /// <summary>
        /// Two-aircraft elements in an offset box. Element wingmen hold the same wide
        /// lateral interval; later elements sit aft and slightly high. The offset leaves
        /// the rear element a view past the lead pair without an unbounded lateral arm.
        /// </summary>
        private static SlotLayout CombatSpread(int slot)
        {
            int element = slot / 2;
            bool wingman = slot % 2 == 1;
            float offset = element % 2 == 1 ? -0.55f : 0f;
            return new SlotLayout(offset + (wingman ? 2.20f : 0f),
                element * 2.20f + (wingman ? 0.15f : 0f),
                Math.Min(element, 2) * 0.65f + (wingman ? 0.35f : 0f));
        }

        /// <summary>
        /// Strong-right finger four: lead's wingman left, element lead right, its
        /// wingman farther right and aft by the same interval. Extra four-ships repeat
        /// behind with enough gap for the aft member of the preceding group.
        /// </summary>
        private static SlotLayout FingerFour(int slot)
        {
            int group = slot / 4;
            float back = group * 3f;
            float height = -0.35f * Math.Min(group, 2);
            switch (slot % 4)
            {
                case 0: return new SlotLayout(0f, back, height);
                case 1: return new SlotLayout(-0.95f, back + 0.80f, height - 0.25f);
                case 2: return new SlotLayout(1.10f, back + 0.60f, height - 0.15f);
                default: return new SlotLayout(2.05f, back + 1.40f, height - 0.40f);
            }
        }

        /// <summary>Equal-sided diamonds sharing each preceding tail as the next lead.</summary>
        private static SlotLayout Diamond(int slot)
        {
            int group = (slot - 1) / 3;
            float back = group * 1.60f;
            float height = -0.40f * Math.Min(group, 2);
            switch ((slot - 1) % 3)
            {
                case 0: return new SlotLayout(1f, back + 0.80f, height - 0.20f);
                case 1: return new SlotLayout(-1f, back + 0.80f, height - 0.20f);
                default: return new SlotLayout(0f, back + 1.60f, height - 0.40f);
            }
        }
    }
}
