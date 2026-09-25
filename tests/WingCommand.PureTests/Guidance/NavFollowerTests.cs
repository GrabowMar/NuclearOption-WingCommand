using Xunit;

namespace WingCommand.PureTests
{
    public class NavFollowerTests
    {
        private static Waypoint P(float x, float z, float alt = float.NaN, float speed = float.NaN)
        {
            Waypoint w = Waypoint.At(x, z);
            w.Altitude = alt;
            w.Speed = speed;
            return w;
        }

        [Fact]
        public void HeadsForTheActivePoint()
        {
            var nav = new NavFollower();
            nav.Load(new[] { P(10000f, 0f), P(10000f, 10000f) }, 2);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            Assert.True(nav.Step(new Vec3(0f, 1000f, 0f), 200f, ref spec));
            Assert.Equal(90f, spec.HeadingDeg, 1);   // east
            Assert.Equal(0, nav.Index);
        }

        [Fact]
        public void InsideTheCaptureRadiusTheNextPointIsActive()
        {
            var nav = new NavFollower();
            nav.Load(new[] { P(10000f, 0f), P(10000f, 10000f) }, 2);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            nav.Step(new Vec3(10000f, 1000f, -500f), 40f, ref spec);   // 500 m < the 600 m floor
            Assert.Equal(1, nav.Index);
            Assert.Equal(0f, spec.HeadingDeg, 1);   // then north
        }

        [Fact]
        public void APointsAltitudeAndSpeedBecomeTheHeldTargets()
        {
            var nav = new NavFollower();
            nav.Load(new[] { P(0f, 20000f, 3000f, 150f), P(0f, 40000f) }, 2);
            var spec = new HoldSpec { Lateral = LateralHold.Nav, AltitudeM = 1200f, SpeedMps = 180f };
            nav.Step(Vec3.Zero, 200f, ref spec);
            Assert.Equal(3000f, spec.AltitudeM);
            Assert.Equal(150f, spec.SpeedMps);
            nav.Step(new Vec3(0f, 3000f, 19000f), 200f, ref spec);   // captured (12 s × 200 m/s = 2.4 km)
            Assert.Equal(1, nav.Index);
            Assert.Equal(3000f, spec.AltitudeM);   // NaN keeps the target
            Assert.Equal(150f, spec.SpeedMps);
        }

        [Fact]
        public void TheLastPointLeavesAHeadingHold()
        {
            var nav = new NavFollower();
            nav.Load(new[] { P(0f, 1000f) }, 1);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            Assert.False(nav.Step(new Vec3(0f, 0f, 900f), 100f, ref spec));
            Assert.False(nav.Active);
            Assert.Equal(LateralHold.Heading, spec.Lateral);
            Assert.Equal(0f, spec.HeadingDeg, 1);
        }

        [Fact]
        public void APointPassedAlongTheLegIsCapturedOutsideTheRadius()
        {
            // Review R2 I1: a point inside the 30-degree turn circle was orbited forever (WingPlanner.Reached's rule).
            var nav = new NavFollower();
            nav.Load(new[] { P(10000f, 0f), P(10000f, 10000f) }, 2);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            nav.Step(Vec3.Zero, 40f, ref spec);                           // the leg starts here, heading east
            nav.Step(new Vec3(10500f, 1000f, 3000f), 40f, ref spec);      // 3 km off the point, but past it along the leg
            Assert.Equal(1, nav.Index);
        }

        [Fact]
        public void TheLastLegsBearingIsHeldAfterTheRoute()
        {
            // Review R2 m: after a short last leg the hold keeps that leg's bearing, not the bearing at capture.
            var nav = new NavFollower();
            nav.Load(new[] { P(0f, 20000f), P(2000f, 20000f) }, 2);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            nav.Step(Vec3.Zero, 250f, ref spec);
            nav.Step(new Vec3(0f, 1000f, 17500f), 250f, ref spec);        // first point captured (3 km at 250 m/s)
            Assert.False(nav.Step(new Vec3(100f, 1000f, 18500f), 250f, ref spec));
            Assert.Equal(90f, spec.HeadingDeg, 1);
        }

        [Fact]
        public void APointBelowTheTerrainFloorIsFlownAtTheFloorPlusClearance()
        {
            // Review R2 I3: NAV flew a drawn 150 m point into a 600 m ridge.
            var nav = new NavFollower();
            nav.Load(new[] { P(0f, 20000f, 100f) }, 1);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            nav.Step(Vec3.Zero, 200f, 500f, 0f, ref spec);
            Assert.Equal(500f + NavFollower.Clearance, spec.AltitudeM);
        }

        [Fact]
        public void APointSpeedBelowTheSafeMinimumIsRaised()
        {
            // Review R2 (re-graded): a 60 m/s point dropped the whole autopilot as "too slow" mid-route.
            var nav = new NavFollower();
            nav.Load(new[] { P(0f, 20000f, float.NaN, 60f) }, 1);
            var spec = new HoldSpec { Lateral = LateralHold.Nav };
            nav.Step(Vec3.Zero, 200f, float.NaN, 90f, ref spec);
            Assert.Equal(90f, spec.SpeedMps);
        }

        [Fact]
        public void NothingLoadedIsNotActive()
        {
            var nav = new NavFollower();
            nav.Load(new Waypoint[0], 0);
            Assert.False(nav.Active);
            var spec = new HoldSpec { Lateral = LateralHold.Nav, HeadingDeg = 123f };
            Assert.False(nav.Step(Vec3.Zero, 100f, ref spec));
            Assert.Equal(123f, spec.HeadingDeg);
        }

        [Fact]
        public void NavAnnunciatesItsLeg()
        {
            var h = new HoldSpec { Lateral = LateralHold.Nav, HeadingDeg = 45f, Vertical = VerticalHold.Altitude, AltitudeM = 3000f };
            Assert.Equal("AP NAV 2/3  ALT 3000", WingHudText.Autopilot(h, false, false, 1, 3));
            Assert.Equal("AP NAV  ALT 3000", WingHudText.Autopilot(h, false, false));
        }
    }
}
