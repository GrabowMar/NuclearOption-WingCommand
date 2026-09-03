using System;
using Xunit;

namespace WingCommand.Tests
{
    /// <summary>
    /// Display-team slot geometry. These are the numbers a chase camera would argue with,
    /// so they live as tests rather than comments: a future tweak that puts two aircraft
    /// on top of each other, or stacks an echelon *up* so the lead disappears behind a
    /// wingman, should fail here rather than in flight.
    /// </summary>
    public class FormationLayoutTests
    {
        private const int MaxSlots = 3;

        [Fact]
        public void EveryShapePlacesFiniteNonOverlappingSlots()
        {
            foreach (FormationShape shape in FormationShapes.All)
            {
                var seen = new SlotLayout[MaxSlots];
                for (int slot = 1; slot <= MaxSlots; slot++)
                {
                    SlotLayout s = FormationLayout.Slot(shape, slot);
                    Assert.False(float.IsNaN(s.Lateral) || float.IsNaN(s.Back) || float.IsNaN(s.Height),
                                 shape + " slot " + slot + " is not finite");
                    Assert.False(float.IsInfinity(s.Lateral) || float.IsInfinity(s.Back) ||
                                 float.IsInfinity(s.Height),
                                 shape + " slot " + slot + " is not finite");

                    for (int previous = 0; previous < slot - 1; previous++)
                    {
                        float dx = seen[previous].Lateral - s.Lateral;
                        float dy = seen[previous].Height - s.Height;
                        float dz = seen[previous].Back - s.Back;
                        Assert.True(dx * dx + dy * dy + dz * dz >= 0.2f * 0.2f,
                                    shape + " slots " + (previous + 1) + " and " + slot + " overlap");
                    }

                    seen[slot - 1] = s;
                }
            }
        }

        [Fact]
        public void ParadeShapesSitBelowTheLeader()
        {
            // The photograph: lead silhouetted against sky, wingmen stepped down. Wall and
            // Ladder climb because that is the shape; everything else drops.
            foreach (FormationShape shape in new[]
            {
                FormationShape.EchelonRight, FormationShape.EchelonLeft,
                FormationShape.LineAbreast, FormationShape.Trail,
                FormationShape.CombatSpread, FormationShape.Vic,
                FormationShape.FingerFour, FormationShape.Diamond,
            })
            {
                for (int slot = 1; slot <= MaxSlots; slot++)
                    Assert.True(FormationLayout.Slot(shape, slot).Height < 0f,
                                shape + " slot " + slot + " should sit below the leader");
            }
        }

        [Fact]
        public void WallAndLadderClimb()
        {
            Assert.True(FormationLayout.Slot(FormationShape.Ladder, 2).Height >
                        FormationLayout.Slot(FormationShape.Ladder, 1).Height);
            Assert.True(FormationLayout.Slot(FormationShape.Wall, 3).Height >
                        FormationLayout.Slot(FormationShape.Wall, 1).Height);
        }

        [Fact]
        public void EchelonIsAScimitarOnOneSide()
        {
            SlotLayout a = FormationLayout.Slot(FormationShape.EchelonRight, 1);
            SlotLayout b = FormationLayout.Slot(FormationShape.EchelonRight, 2);
            SlotLayout c = FormationLayout.Slot(FormationShape.EchelonRight, 3);

            Assert.True(a.Lateral > 0f && b.Lateral > a.Lateral && c.Lateral > b.Lateral);
            Assert.True(a.Back > 0f && b.Back > a.Back && c.Back > b.Back);

            // Sweep grows with rank: each aircraft sits further aft per metre of span
            // than the one inside it. That curve is the scimitar.
            float innerSweep = a.Back / a.Lateral;
            float outerSweep = c.Back / c.Lateral;
            Assert.True(outerSweep > innerSweep);

            SlotLayout left = FormationLayout.Slot(FormationShape.EchelonLeft, 1);
            Assert.True(left.Lateral < 0f);
            Assert.Equal(a.Back, left.Back, 5);
        }

        [Fact]
        public void LineAbreastIsACrescentNotARuler()
        {
            SlotLayout right = FormationLayout.Slot(FormationShape.LineAbreast, 1);
            SlotLayout left = FormationLayout.Slot(FormationShape.LineAbreast, 2);

            Assert.True(right.Lateral > 0f);
            Assert.True(left.Lateral < 0f);
            Assert.Equal(right.Lateral, -left.Lateral, 5);
            Assert.Equal(right.Back, left.Back, 5);
            // A few degrees of sweep: off the beam, not in trail.
            Assert.InRange(right.Back, 0.05f, 0.40f);
        }

        [Fact]
        public void VicOpensAftAndSplitsTheLead()
        {
            SlotLayout right = FormationLayout.Slot(FormationShape.Vic, 1);
            SlotLayout left = FormationLayout.Slot(FormationShape.Vic, 2);
            SlotLayout outer = FormationLayout.Slot(FormationShape.Vic, 3);

            Assert.True(right.Lateral > 0f);
            Assert.True(left.Lateral < 0f);
            Assert.Equal(right.Back, left.Back, 5);
            Assert.True(outer.Back > right.Back);
            Assert.True(Math.Abs(outer.Lateral) > Math.Abs(right.Lateral));
        }

        [Fact]
        public void TrailStaysOnTheCentrelineAndStepsAft()
        {
            SlotLayout first = FormationLayout.Slot(FormationShape.Trail, 1);
            SlotLayout second = FormationLayout.Slot(FormationShape.Trail, 2);

            Assert.InRange(first.Lateral, -0.2f, 0.2f);
            Assert.True(second.Back > first.Back);
        }

        [Fact]
        public void DiamondIsAHorizontalRhombusWithTheTailLow()
        {
            SlotLayout right = FormationLayout.Slot(FormationShape.Diamond, 1);
            SlotLayout left = FormationLayout.Slot(FormationShape.Diamond, 2);
            SlotLayout tail = FormationLayout.Slot(FormationShape.Diamond, 3);

            Assert.True(right.Lateral > 0f);
            Assert.True(left.Lateral < 0f);
            Assert.Equal(right.Lateral, -left.Lateral, 5);
            Assert.Equal(right.Back, left.Back, 5);
            Assert.InRange(tail.Lateral, -0.02f, 0.02f);
            Assert.True(tail.Back > right.Back);
            Assert.True(tail.Height < right.Height);

            double leadToWing = Math.Sqrt(right.Lateral * right.Lateral + right.Back * right.Back);
            double wingToTail = Math.Sqrt(
                (tail.Lateral - right.Lateral) * (tail.Lateral - right.Lateral) +
                (tail.Back - right.Back) * (tail.Back - right.Back));
            Assert.Equal(leadToWing, wingToTail, 5);
        }

        [Fact]
        public void FingerFourMatchesAnOutstretchedHand()
        {
            SlotLayout port = FormationLayout.Slot(FormationShape.FingerFour, 1);
            SlotLayout starboard = FormationLayout.Slot(FormationShape.FingerFour, 2);
            SlotLayout outer = FormationLayout.Slot(FormationShape.FingerFour, 3);

            Assert.True(port.Lateral < 0f, "flight wingman is port");
            Assert.True(starboard.Lateral > 0f, "element lead is starboard");
            Assert.True(outer.Lateral > starboard.Lateral, "element wingman is further starboard");
            Assert.True(outer.Back > starboard.Back, "element wingman is stepped aft");

            // Same sweep, one arm further out: the element pair is one spacing-unit apart.
            double dx = outer.Lateral - starboard.Lateral;
            double dz = outer.Back - starboard.Back;
            Assert.Equal(1.0, Math.Sqrt(dx * dx + dz * dz), 5);
        }

        [Fact]
        public void CombatSpreadIsWiderThanParadeEchelon()
        {
            float parade = Math.Abs(FormationLayout.Slot(FormationShape.EchelonRight, 1).Lateral);
            float spread = Math.Abs(FormationLayout.Slot(FormationShape.CombatSpread, 1).Lateral);
            Assert.True(spread > parade * 1.4f);
        }
    }
}
