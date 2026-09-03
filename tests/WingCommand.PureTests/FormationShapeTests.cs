using System;
using System.Linq;
using Xunit;

namespace WingCommand.Tests
{
    public class FormationShapeTests
    {
        [Fact]
        public void AllCoversEveryEnumValue()
        {
            var values = (FormationShape[])Enum.GetValues(typeof(FormationShape));
            Assert.Equal(values.Length, FormationShapes.All.Length);
            foreach (FormationShape v in values)
                Assert.Contains(v, FormationShapes.All);
        }

        [Fact]
        public void CoreIsADistinctSubsetOfAll()
        {
            Assert.NotEmpty(FormationShapes.Core);
            Assert.Equal(FormationShapes.Core.Length,
                         new System.Collections.Generic.HashSet<FormationShape>(
                             FormationShapes.Core).Count);

            foreach (FormationShape shape in FormationShapes.Core)
                Assert.Contains(shape, FormationShapes.All);
        }

        // The whole reason this file exists: three copies of a shape switch went stale and
        // five new shapes displayed as run-together enum names in every menu that offered
        // them. The bug is specifically a *multi-word* enum shown unsplit — Trail and Vic
        // are one word and are supposed to render as themselves, which is what the first
        // version of this test got wrong. So the assertion is on the CamelCase names only.
        [Fact]
        public void EveryMultiWordShapeHasASpacedDisplayName()
        {
            foreach (FormationShape shape in FormationShapes.All)
            {
                string name = shape.ToString();
                string pretty = FormationShapes.Pretty(shape);

                Assert.False(string.IsNullOrWhiteSpace(pretty));

                // An interior capital means the enum spelling runs two words together.
                bool multiWord = name.Skip(1).Any(char.IsUpper);
                if (multiWord) Assert.Contains(" ", pretty);
                Assert.Equal(name, pretty.Replace(" ", string.Empty));
            }
        }

        [Fact]
        public void CycleVisitsEveryShapeAndReturnsToTheStart()
        {
            FormationShape at = FormationShapes.All[0];
            var seen = new System.Collections.Generic.HashSet<FormationShape>();

            for (int i = 0; i < FormationShapes.All.Length; i++)
            {
                seen.Add(at);
                at = FormationShapes.Cycle(at, 1);
            }

            Assert.Equal(FormationShapes.All.Length, seen.Count);
            Assert.Equal(FormationShapes.All[0], at);
        }

        [Fact]
        public void CycleWrapsBackwardsPastTheFirstShape()
        {
            FormationShape last = FormationShapes.All[FormationShapes.All.Length - 1];
            Assert.Equal(last, FormationShapes.Cycle(FormationShapes.All[0], -1));
        }

        [Fact]
        public void CycleCoreStaysWithinCore()
        {
            FormationShape at = FormationShapes.Core[0];
            for (int i = 0; i < FormationShapes.Core.Length * 2 + 1; i++)
            {
                at = FormationShapes.CycleCore(at, 1);
                Assert.Contains(at, FormationShapes.Core);
            }
        }

        // A shape configured by hand, or left over from an older release, is not in Core.
        // Cycling from it has to land somewhere rather than throwing or returning the input.
        [Fact]
        public void CycleCoreRecoversFromAShapeOutsideCore()
        {
            FormationShape outside = FormationShape.Diamond;
            Assert.DoesNotContain(outside, FormationShapes.Core);

            Assert.Equal(FormationShapes.Core[0], FormationShapes.CycleCore(outside, 1));
            Assert.Equal(FormationShapes.Core[FormationShapes.Core.Length - 1],
                         FormationShapes.CycleCore(outside, -1));
        }

        [Fact]
        public void DiamondRhombusHasEqualSideLengths()
        {
            // A true diamond (rhombus) requires that distance from Lead to Wing
            // equals the distance from Wing to Slot.
            const double sweepDeg = 45.0;
            double rad = sweepDeg * (Math.PI / 180.0);
            double wingX = Math.Cos(rad);
            double wingZ = Math.Sin(rad);
            double leadToWing = Math.Sqrt(wingX * wingX + wingZ * wingZ);

            double tailZ = 2.0 * Math.Sin(rad);
            double deltaX = 0.0 - wingX;
            double deltaZ = tailZ - wingZ;
            double wingToTail = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

            Assert.Equal(1.0, leadToWing, precision: 6);
            Assert.Equal(1.0, wingToTail, precision: 6);
            Assert.Equal(leadToWing, wingToTail, precision: 6);
        }

        [Fact]
        public void FingerFourFingertipLayoutMatchesHand()
        {
            // Right-hand finger four: Slot 1 is left (-1), Slot 2 is right (+1), Slot 3 is outer right (+1)
            double sweepRad = 40.0 * (Math.PI / 180.0);
            double slot1X = -1.0 * Math.Cos(sweepRad);
            double slot2X = 1.15 * Math.Cos(sweepRad);
            double slot3X = 2.15 * Math.Cos(sweepRad);

            Assert.True(slot1X < 0.0, "Slot 1 (flight wingman) should be on port/left");
            Assert.True(slot2X > 0.0, "Slot 2 (element lead) should be on starboard/right");
            Assert.True(slot3X > slot2X, "Slot 3 (element wingman) should be wider to the right of element lead");

            // Spacing between Slot 2 and Slot 3 along the echelon arm must be exactly 1.0 spacing arm
            double deltaX = slot3X - slot2X;
            double deltaZ = (2.15 - 1.15) * Math.Sin(sweepRad);
            double elementDistance = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            Assert.Equal(1.0, elementDistance, precision: 6);
        }
    }
}
