using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P2: the PLAN › ELEMENTS words (spec bezel v2 §5): step rows, the cue and the plan bar, safe glyphs only.</summary>
    public class PlanWordsTests
    {
        private static WingPlan TwoLanes()
        {
            var plan = new WingPlan { Name = "STRIKE NORTH" };
            plan.Add(0, PlanEdit.NewStep(PlanTool.Route, 0f, 10000f, float.NaN, 0f));
            PlanEdit.AddPoint(plan.Steps[0][0], 0f, 20000f);
            plan.Add(0, PlanEdit.NewStep(PlanTool.Cap, 0f, 20000f, 3000f, 8000f));
            PlanStep attack = PlanEdit.Attack(7u);
            PlanEdit.AddTarget(attack, 8u);
            attack.Start = PlanStart.After;
            attack.AfterLane = 0;
            attack.AfterStep = 0;
            plan.Add(1, attack);
            plan.Add(1, new PlanStep { Kind = PlanKind.Rtb });
            return plan;
        }

        [Fact]
        public void StartsSayWhenEachStepGoes()
        {
            WingPlan plan = TwoLanes();
            Assert.Equal("EXEC", PlanWords.Start(plan, 0, 0));
            Assert.Equal("AFTER A1", PlanWords.Start(plan, 0, 1));   // the lane's own chain
            Assert.Equal("AFTER A1", PlanWords.Start(plan, 1, 0));
            Assert.Equal("AFTER B1", PlanWords.Start(plan, 1, 1));
            plan.Steps[0][0].Start = PlanStart.TPlus;
            plan.Steps[0][0].Delay = 90f;
            Assert.Equal("T+1:30", PlanWords.Start(plan, 0, 0));
            plan.Steps[1][0].Delay = 30f;
            Assert.Equal("AFTER A1 +0:30", PlanWords.Start(plan, 1, 0));
        }

        [Fact]
        public void DetailsAndEndsNameWhatTheStepDoes()
        {
            WingPlan plan = TwoLanes();
            Assert.Equal("ROUTE", PlanWords.Kind(PlanKind.Route));
            Assert.Equal("FORM UP", PlanWords.Kind(PlanKind.FormUp));
            Assert.Equal("2 PTS · 20.0 KM · 1:40", PlanWords.Detail(plan.Steps[0][0], 0f, 0f, 200f));
            Assert.Equal("R 8 KM · 3,000 M", PlanWords.Detail(plan.Steps[0][1], 0f, 20000f, 200f));
            Assert.Equal("2 TGT", PlanWords.Detail(plan.Steps[1][0], 0f, 0f, 200f));
            Assert.Equal("", PlanWords.Detail(plan.Steps[1][1], 0f, 0f, 200f));
            Assert.Equal("ARRIVE", PlanWords.End(plan.Steps[0][0]));
            Assert.Equal("BINGO", PlanWords.End(plan.Steps[0][1]));
            Assert.Equal("TGT DN", PlanWords.End(plan.Steps[1][0]));
            plan.Steps[0][1].End = PlanEnd.Time;
            plan.Steps[0][1].EndSeconds = 240f;
            Assert.Equal("4:00", PlanWords.End(plan.Steps[0][1]));
        }

        [Fact]
        public void StatesReadWaitUntilExecutedThenTheRunnersState()
        {
            Assert.Equal("WAIT", PlanWords.State(null, 0, 0));
            WingPlan plan = TwoLanes();
            var r = new PlanRunner(plan);
            r.Execute(0f);
            r.Tick(0f, new LaneFacts[WingPlan.Lanes], null);
            Assert.Equal("RUN", PlanWords.State(r, 0, 0));
            Assert.Equal("NEXT", PlanWords.State(r, 0, 1));
            r.Refused(1, "no");
            Assert.Equal("BLOCKED", PlanWords.State(r, 1, 0));
        }

        [Fact]
        public void TheCueAndTheBarSayWhatIsSelectedAndWhereThePlanIs()
        {
            WingPlan plan = TwoLanes();
            Assert.Equal("B1 ATTACK · AFTER A1 · 2 TGT · ENDS TGT DN", PlanWords.Cue(plan, 1, 0));
            Assert.Equal("STRIKE NORTH · DRAFT · 4 STEPS", PlanWords.Bar(plan, false, false));
            Assert.Equal("STRIKE NORTH · RUNNING · 4 STEPS", PlanWords.Bar(plan, true, false));
            Assert.Equal("STRIKE NORTH · DONE · 4 STEPS", PlanWords.Bar(plan, false, true));
            Assert.Equal("PLAN CAP · B VIPER · RIGHT-PRESS, DRAG FOR THE RADIUS", PlanWords.ToolCue(PlanTool.Cap, "B VIPER"));
            Assert.Equal("PLAN ROUTE · A · RIGHT-CLICK POINTS, DONE ENDS IT", PlanWords.ToolCue(PlanTool.Route, "A"));
        }

        [Fact]
        public void NoWordUsesAGlyphTheMfdFontLacks()
        {
            WingPlan plan = TwoLanes();
            foreach (string s in new[]
                     {
                         PlanWords.Cue(plan, 0, 1), PlanWords.Bar(plan, true, false), PlanWords.Detail(plan.Steps[0][0], 0f, 0f, 200f),
                         PlanWords.ToolCue(PlanTool.Attack, "B"), PlanWords.ToolCue(PlanTool.Replace, "B"),
                     })
                foreach (char ch in s)
                    Assert.False(ch == '…' || (ch >= '←' && ch <= '⇿') || (ch >= '─' && ch <= '◿'), s);
        }
    }
}
