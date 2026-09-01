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
    }
}
