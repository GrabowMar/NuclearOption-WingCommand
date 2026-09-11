using System;

namespace WingCommand
{
    /// <summary>Formation-unit slot: lateral positive right, back positive aft, height positive up. Scale
    /// planar coordinates by spacing and height by stack.</summary>
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

    /// <summary>Stable formation lanes with horizontal separation even at zero stack. Tactical shapes
    /// extend aft in elements to bound outer-turn speed demands.</summary>
    internal static class FormationLayout
    {
        // Shared turn-deformation factor covered by geometry checks.
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
                    // Keep line-abreast members abeam at every rank.
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
                    // Ladder uses a climbing trail as its defining geometry.
                    return new SlotLayout(0f, 1.10f * slot, 1.55f * slot);
                case FormationShape.Wall:
                    return new SlotLayout(side * 1.55f * rank, 0f, Math.Min(0.75f * rank, 3f));
            }
        }

        // Cap step-down so large wings stay near leader altitude and retain horizontal separation when
        // terrain flattens slots.
        private static float StepDown(int rank) => -0.25f * Math.Min(rank, 4);

        /// <summary>Offset two-aircraft boxes with constant lateral spacing; later elements sit aft and
        /// higher for visibility without unbounded turn arms.</summary>
        private static SlotLayout CombatSpread(int slot)
        {
            int element = slot / 2;
            bool wingman = slot % 2 == 1;
            float offset = element % 2 == 1 ? -0.55f : 0f;
            return new SlotLayout(offset + (wingman ? 2.20f : 0f),
                element * 2.20f + (wingman ? 0.15f : 0f),
                Math.Min(element, 2) * 0.65f + (wingman ? 0.35f : 0f));
        }

        /// <summary>Strong-right finger four: lead wingman left, second element right and aft. Repeat
        /// four-ships behind with clearance for the preceding tail.</summary>
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

        /// <summary>Linked equal-sided diamonds using each previous tail as the next lead.</summary>
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
