using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class AnchorTrailTests
    {
        /// <summary>An anchor that flew 1000 m north, then 1000 m east, recorded every 10 m.</summary>
        private static AnchorTrail LShaped()
        {
            var trail = new AnchorTrail();
            for (int i = 0; i <= 100; i++) trail.Push(new Vec3(0f, 500f, i * 10f));
            for (int i = 1; i <= 100; i++) trail.Push(new Vec3(i * 10f, 500f, 1000f));
            return trail;
        }

        [Fact]
        public void PointsFollowTheRouteTheAnchorFlew()
        {
            AnchorTrail trail = LShaped();
            Assert.Equal(2000f, trail.Head, 0);
            Vec3 a = trail.PointAt(500f, out Vec3 northbound);
            Assert.True((a - new Vec3(0f, 500f, 500f)).Length < 1f, $"{a}");
            Assert.True((northbound - Vec3.Forward).Length < 1e-3f, $"{northbound}");
            Vec3 b = trail.PointAt(1500f, out Vec3 eastbound);
            Assert.True((b - new Vec3(500f, 500f, 1000f)).Length < 1f, $"{b}");
            Assert.True((eastbound - Vec3.Right).Length < 1e-3f, $"{eastbound}");
        }

        [Fact]
        public void NearestFindsWhereAPointSitsAlongTheRoute()
        {
            AnchorTrail trail = LShaped();
            Assert.Equal(300f, trail.Nearest(new Vec3(40f, 400f, 300f)), 0);
            Assert.Equal(1700f, trail.Nearest(new Vec3(700f, 500f, 960f)), 0);
        }

        [Fact]
        public void QueriesOutsideTheRecordedRouteClampToItsEnds()
        {
            AnchorTrail trail = LShaped();
            Assert.True((trail.PointAt(-50f, out _) - new Vec3(0f, 500f, 0f)).Length < 1f);
            Assert.True((trail.PointAt(5000f, out _) - new Vec3(1000f, 500f, 1000f)).Length < 1f);
        }

        [Fact]
        public void AnEmptyTrailAnswersWithoutThrowing()
        {
            var trail = new AnchorTrail();
            Assert.Equal(0f, trail.Head);
            Assert.Equal(0f, trail.Nearest(new Vec3(1f, 2f, 3f)));
            Vec3 p = trail.PointAt(10f, out Vec3 d);
            Assert.False(float.IsNaN(p.X) || float.IsNaN(d.X));
        }
    }
}
