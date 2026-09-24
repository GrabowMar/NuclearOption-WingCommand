using Xunit;

namespace WingCommand.PureTests
{
    public class ElementSettingsTests
    {
        [Fact]
        public void ElementZeroFollowsTheWing()
        {
            var s = new ElementSettings();
            s.SetShape(0, "vic");
            s.SetDoctrine(0, WingDoctrine.Sweep);
            Assert.Equal("trail", s.ShapeOf(0, "trail"));
            Assert.Equal(WingDoctrine.Reserve, s.DoctrineOf(0, WingDoctrine.Reserve));
        }

        [Fact]
        public void AnElementKeepsItsOwnShapeAndDoctrine()
        {
            var s = new ElementSettings();
            s.SetShape(2, "vic");
            s.SetDoctrine(2, WingDoctrine.Sweep);
            Assert.Equal("vic", s.ShapeOf(2, "trail"));
            Assert.Equal("trail", s.ShapeOf(1, "trail"));
            Assert.Equal(WingDoctrine.Sweep, s.DoctrineOf(2, WingDoctrine.Reserve));
            Assert.Equal(WingDoctrine.Reserve, s.DoctrineOf(1, WingDoctrine.Reserve));
        }

        [Fact]
        public void MergeForgetsTheElementsSettings()
        {
            // Review focus 2: a merged element's settings go; A's stay the wing's.
            var s = new ElementSettings();
            s.SetShape(1, "vic");
            s.SetDoctrine(1, WingDoctrine.Sweep);
            s.Forget(1);
            Assert.Equal("trail", s.ShapeOf(1, "trail"));
            Assert.Equal(WingDoctrine.Escort, s.DoctrineOf(1, WingDoctrine.Escort));
            s.SetShape(3, "box");
            s.Clear();
            Assert.Equal("trail", s.ShapeOf(3, "trail"));
        }
    }
}
