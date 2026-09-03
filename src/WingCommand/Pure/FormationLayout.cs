namespace WingCommand
{
    /// <summary>
    /// One slot in formation units: lateral (+ right of the leader), back (+ astern) and
    /// height (+ up, in vertical-stack units). The flight code multiplies the first two
    /// by slot spacing and the third by stack height; this type never sees metres.
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
    /// Display-team slot geometry. Pure numbers, no engine types.
    ///
    /// The previous layout was a tactical diagram: a straight arm at a fixed sweep, wingmen
    /// stacked <i>up</i> so each rank sat above the one ahead. It read as a briefing slide.
    /// This one is built the way a formation is photographed — slightly tight, slightly
    /// down, and with the line allowed to curve — so a three-ship reads as one aircraft
    /// from a chase cam rather than three occupying nearby pieces of sky.
    ///
    /// Every regular shape still goes through <see cref="Place"/>: an arm in spacing units
    /// and a sweep from beam (0°) to astern (90°). Sweep grows with rank on the parade
    /// shapes, which is what turns a ruler into a scimitar. Finger Four and Diamond place
    /// each of the first three slots by hand because their asymmetry is the whole point,
    /// then repeat as a second element astern.
    /// </summary>
    internal static class FormationLayout
    {
        /// <summary>Vertical stacks each Ladder rung climbs. The climb <i>is</i> the shape.</summary>
        private const float LadderRise = 1.55f;

        /// <summary>How the shape steps its slots off the leader's altitude.</summary>
        private enum Stack
        {
            /// <summary>Each rank a little lower. The parade look: lead silhouetted against sky.</summary>
            Down,

            /// <summary>Small down-weave plus a slow drop, so a long trail is not a staircase.</summary>
            Weave,

            /// <summary>Each rank a full stack higher. Wall and ladder.</summary>
            Up,

            /// <summary>A large fixed climb per slot — ladder's defining feature.</summary>
            Ladder,
        }

        /// <summary>
        /// One regular formation. <see cref="BaseArm"/> is the innermost slot in spacing
        /// units; <see cref="SweepDeg"/> is that slot's angle off the beam; <see cref="SweepGrow"/>
        /// adds degrees of sweep per rank so the line curves aft as it widens.
        /// </summary>
        private readonly struct Spec
        {
            public readonly float BaseArm;
            public readonly float SweepDeg;
            public readonly float SweepGrow;
            public readonly bool Symmetric;
            public readonly float Side;
            public readonly float StackStep;
            public readonly Stack Stack;

            public Spec(float baseArm, float sweepDeg, float sweepGrow, bool symmetric,
                        float side, float stackStep, Stack stack)
            {
                BaseArm = baseArm;
                SweepDeg = sweepDeg;
                SweepGrow = sweepGrow;
                Symmetric = symmetric;
                Side = side;
                StackStep = stackStep;
                Stack = stack;
            }
        }

        public static SlotLayout Slot(FormationShape shape, int slot)
        {
            if (slot <= 0) return new SlotLayout(0f, 0f, 0f);

            switch (shape)
            {
                case FormationShape.FingerFour: return FingerFour(slot);
                case FormationShape.Diamond:    return Diamond(slot);
                default:                        return FromSpec(SpecFor(shape), slot);
            }
        }

        /// <summary>
        /// Parade numbers. Tight inner arms, a scimitar of extra sweep per rank, and a
        /// step-down so the lead sits highest. Combat Spread is the exception: it stays
        /// wide on purpose. Wall and Ladder climb because that is what those shapes are.
        /// </summary>
        private static Spec SpecFor(FormationShape shape)
        {
            switch (shape)
            {
                case FormationShape.EchelonLeft:
                    return new Spec(0.90f, 36f, 5f, symmetric: false, side: -1f, 0.32f, Stack.Down);

                case FormationShape.LineAbreast:
                    // A few degrees of sweep is a crescent, not a ruler. Outer aircraft sit
                    // a body-length aft so the line reads as a formation from head-on.
                    return new Spec(1.02f, 8f, 3f, symmetric: true, side: 0f, 0.20f, Stack.Down);

                case FormationShape.Trail:
                    return new Spec(0.86f, 90f, 0f, symmetric: false, side: 0f, 0.22f, Stack.Weave);

                case FormationShape.CombatSpread:
                    return new Spec(1.85f, 14f, 2f, symmetric: true, side: 0f, 0.22f, Stack.Down);

                case FormationShape.Vic:
                    return new Spec(0.98f, 32f, 4f, symmetric: true, side: 0f, 0.28f, Stack.Down);

                case FormationShape.Wall:
                    return new Spec(1.08f, 6f, 2f, symmetric: true, side: 0f, 1.10f, Stack.Up);

                case FormationShape.Ladder:
                    return new Spec(0.88f, 88f, 0f, symmetric: false, side: 0f, 0f, Stack.Ladder);

                case FormationShape.EchelonRight:
                default:
                    return new Spec(0.90f, 36f, 5f, symmetric: false, side: 1f, 0.32f, Stack.Down);
            }
        }

        private static SlotLayout FromSpec(Spec spec, int slot)
        {
            int pair = (slot + 1) / 2;
            int rank = spec.Symmetric ? pair : slot;
            float side = spec.Symmetric ? (slot % 2 == 1 ? 1f : -1f) : spec.Side;
            float arm = spec.BaseArm * rank;
            float sweep = spec.SweepDeg + spec.SweepGrow * (rank - 1);

            float height;
            switch (spec.Stack)
            {
                case Stack.Weave:
                    // Off the wake of the one ahead, overall dropping so a long trail still
                    // photographs as a descending line rather than a column of equals.
                    height = (slot % 2 == 1 ? -spec.StackStep : -spec.StackStep * 1.7f)
                             - (slot - 1) * 0.07f;
                    break;

                case Stack.Ladder:
                    height = LadderRise * slot;
                    break;

                case Stack.Up:
                    height = spec.StackStep * (rank - 1);
                    break;

                default:
                    height = -spec.StackStep * rank;
                    break;
            }

            return Place(arm, sweep, side, height);
        }

        /// <summary>
        /// The one placement primitive. <paramref name="arm"/> is distance from the leader
        /// along the formation line; <paramref name="sweepDeg"/> rotates that line from the
        /// beam (0°) to dead astern (90°); <paramref name="side"/> is +1 right, −1 left, 0
        /// on the centreline.
        /// </summary>
        internal static SlotLayout Place(float arm, float sweepDeg, float side, float height)
        {
            float sweep = sweepDeg * (float)(System.Math.PI / 180.0);
            float cos = (float)System.Math.Cos(sweep);
            float sin = (float)System.Math.Sin(sweep);
            return new SlotLayout(side * arm * cos, arm * sin, height);
        }

        /// <summary>
        /// Classic right-hand finger-four: close wingman to port, element lead to
        /// starboard, element wingman a full arm further out and slightly more swept so
        /// the four fingertips of an outstretched hand are visible from above. Extra
        /// slots form a second finger astern and a little low.
        /// </summary>
        private static SlotLayout FingerFour(int slot)
        {
            const float sweep = 36f;

            SlotLayout lead;
            switch (slot)
            {
                case 1: return Place(0.88f, sweep, -1f, -0.20f);
                case 2: return Place(1.08f, sweep, 1f, -0.12f);
                case 3: return Place(2.08f, sweep, 1f, -0.28f);
            }

            int extra = slot - 4;
            int group = extra / 4 + 1;
            int within = extra % 4;
            float astern = group * 2.45f;
            float drop = -group * 0.45f;

            switch (within)
            {
                case 0:  lead = Place(0f, 90f, 0f, drop); break;
                case 1:  lead = Place(0.88f, sweep, -1f, drop - 0.20f); break;
                case 2:  lead = Place(1.08f, sweep, 1f, drop - 0.12f); break;
                default: lead = Place(2.08f, sweep, 1f, drop - 0.28f); break;
            }

            return new SlotLayout(lead.Lateral, lead.Back + astern, lead.Height);
        }

        /// <summary>
        /// A pointed rhombus: wings at 40°, tail on the centreline at the distance that
        /// keeps the four horizontal edges equal, and the tail a half-stack lower so the
        /// diamond has thickness when seen from abeam.
        /// </summary>
        private static SlotLayout Diamond(int slot)
        {
            const float arm = 0.95f;
            const float sweep = 40f;
            float tailBack = 2f * arm * (float)System.Math.Sin(sweep * (System.Math.PI / 180.0));

            int group = (slot - 1) / 3;
            int within = (slot - 1) % 3;
            float astern = group * (tailBack + 0.55f);
            float drop = -group * 0.40f;

            SlotLayout point;
            switch (within)
            {
                case 0:  point = Place(arm, sweep, 1f, drop - 0.16f); break;
                case 1:  point = Place(arm, sweep, -1f, drop - 0.16f); break;
                default: point = Place(tailBack, 90f, 0f, drop - 0.48f); break;
            }

            return new SlotLayout(point.Lateral, point.Back + astern, point.Height);
        }
    }
}
