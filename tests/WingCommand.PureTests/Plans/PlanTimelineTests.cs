using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P3: the TIMELINE's planned times (spec WMC rebuild §PLAN timeline): each step's start and end in seconds after EXECUTE,
    /// from distances, speeds, TIME ends and AFTERs; an end that cannot be known (bingo, Winchester, targets down) is NaN, and so is
    /// everything that waits for it.</summary>
    public class PlanTimelineTests
    {
        private static readonly float[] Xs = new float[WingPlan.Lanes], Zs = new float[WingPlan.Lanes];
        private static readonly float[] Speeds = { 200f, 100f, 200f, 200f };

        private static (float[,] start, float[,] end) Run(WingPlan plan)
        {
            var start = new float[WingPlan.Lanes, WingPlan.MaxSteps];
            var end = new float[WingPlan.Lanes, WingPlan.MaxSteps];
            PlanTimeline.Planned(plan, Xs, Zs, Speeds, start, end);
            return (start, end);
        }

        [Fact]
        public void ALaneChainsItsStepsByDistanceSpeedAndTime()
        {
            var plan = new WingPlan();
            plan.Add(0, PlanEdit.NewStep(PlanTool.Move, 0f, 20000f, float.NaN, 0f));     // 20 km at 200 m/s: 100 s
            PlanStep orbit = PlanEdit.NewStep(PlanTool.Orbit, 0f, 20000f, float.NaN, 0f);
            orbit.EndSeconds = 60f;
            plan.Add(0, orbit);                                                            // no transit, 60 s
            plan.Add(0, new PlanStep { Kind = PlanKind.FormUp });
            (float[,] s, float[,] e) = Run(plan);
            Assert.Equal(0f, s[0, 0]);
            Assert.Equal(100f, e[0, 0], 1);
            Assert.Equal(100f, s[0, 1], 1);
            Assert.Equal(160f, e[0, 1], 1);
            Assert.Equal(160f, s[0, 2], 1);
            Assert.Equal(160f, e[0, 2], 1);
        }

        [Fact]
        public void TPlusAndAfterWaitAndAnOpenEndLeavesWhatFollowsUnknown()
        {
            var plan = new WingPlan();
            plan.Add(0, PlanEdit.NewStep(PlanTool.Move, 0f, 20000f, float.NaN, 0f));    // A1: 0-100
            PlanStep b1 = PlanEdit.NewStep(PlanTool.Move, 0f, 5000f, float.NaN, 0f);       // 5 km at 100 m/s: 50 s
            b1.Start = PlanStart.After;
            b1.AfterLane = 0;
            b1.AfterStep = 0;
            b1.Delay = 30f;
            plan.Add(1, b1);                                                               // B1: 130-180
            PlanStep c1 = PlanEdit.NewStep(PlanTool.Cap, 0f, 0f, float.NaN, 0f);           // ends at bingo: open
            c1.Start = PlanStart.TPlus;
            c1.Delay = 45f;
            plan.Add(2, c1);
            plan.Add(2, new PlanStep { Kind = PlanKind.Rtb });
            (float[,] s, float[,] e) = Run(plan);
            Assert.Equal(130f, s[1, 0], 1);
            Assert.Equal(180f, e[1, 0], 1);
            Assert.Equal(45f, s[2, 0], 1);
            Assert.True(float.IsNaN(e[2, 0]));
            Assert.True(float.IsNaN(s[2, 1]));
        }

        [Fact]
        public void AnAfterOnALaterLaneIsResolvedWhateverTheOrder()
        {
            var plan = new WingPlan();
            PlanStep a1 = PlanEdit.NewStep(PlanTool.Move, 0f, 10000f, float.NaN, 0f);     // waits for D1, then 50 s
            a1.Start = PlanStart.After;
            a1.AfterLane = 3;
            a1.AfterStep = 0;
            plan.Add(0, a1);
            plan.Add(3, PlanEdit.NewStep(PlanTool.Move, 0f, 20000f, float.NaN, 0f));      // D1: 0-100
            (float[,] s, float[,] e) = Run(plan);
            Assert.Equal(100f, s[0, 0], 1);
            Assert.Equal(150f, e[0, 0], 1);
        }
    }
}
