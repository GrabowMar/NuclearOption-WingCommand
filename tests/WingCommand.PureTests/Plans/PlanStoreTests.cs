using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P3: saved plans (spec WMC rebuild §PLAN PlanStore; bezel v2 §5 PLANS ›): per theatre, targets never saved, loaded as a
    /// copy, a bad file or step skipped with a reason.</summary>
    public class PlanStoreTests
    {
        private static WingPlan Strike()
        {
            var plan = new WingPlan { Name = "STRIKE" };
            PlanStep route = PlanEdit.NewStep(PlanTool.Route, 100f, 200f, 3000f, 0f);
            PlanEdit.AddPoint(route, 300f, 400f);
            plan.Add(0, route);
            plan.Add(0, PlanEdit.NewStep(PlanTool.Cap, 300f, 400f, float.NaN, 10000f));
            PlanStep attack = PlanEdit.Attack(77u);
            attack.Start = PlanStart.After;
            attack.AfterLane = 0;
            attack.AfterStep = 0;
            attack.Delay = 30f;
            plan.Add(1, attack);
            plan.Add(1, new PlanStep { Kind = PlanKind.Rtb, Start = PlanStart.TPlus, Delay = 600f });
            return plan;
        }

        [Fact]
        public void APlanSurvivesTheFileButItsTargetsDoNot()
        {
            var store = new PlanStore();
            Assert.True(store.Save(Strike(), "Heartland", out string name));
            Assert.Equal("STRIKE", name);
            var errors = new List<string>();
            PlanStore back = PlanStore.FromJson(store.ToJson(), errors);
            Assert.Empty(errors);
            Assert.Single(back.Plans);
            WingPlan p = back.Load(0);
            Assert.Equal("STRIKE", p.Name);
            Assert.Equal(2, p.Steps[0].Count);
            Assert.Equal(PlanKind.Route, p.Steps[0][0].Kind);
            Assert.Equal(2, p.Steps[0][0].Points.Length);
            Assert.Equal(3000f, p.Steps[0][0].Points[1].Altitude);
            Assert.Equal(10000f, p.Steps[0][1].Radius);
            Assert.True(float.IsNaN(p.Steps[0][1].Points[0].Altitude));
            PlanStep attack = p.Steps[1][0];
            Assert.Equal(PlanStart.After, attack.Start);
            Assert.Equal((0, 0, 30f), (attack.AfterLane, attack.AfterStep, attack.Delay));
            Assert.Equal(PlanEnd.TargetsDown, attack.End);
            Assert.Empty(attack.Targets);                     // picked again after loading
            Assert.Equal((PlanStart.TPlus, 600f), (p.Steps[1][1].Start, p.Steps[1][1].Delay));
        }

        [Fact]
        public void ALoadedPlanIsACopy()
        {
            var store = new PlanStore();
            store.Save(Strike(), "Heartland", out _);
            WingPlan a = store.Load(0);
            a.Steps[0][0].Points[0].X = 999f;
            a.Steps[0].Clear();
            Assert.Equal(100f, store.Load(0).Steps[0][0].Points[0].X);
        }

        [Fact]
        public void OnlyThisTheatresPlansAreListedAndSavingAgainReplacesByName()
        {
            var store = new PlanStore();
            store.Save(Strike(), "Heartland", out _);
            store.Save(new WingPlan { Name = "PATROL" }, "Ignus", out _);
            WingPlan again = Strike();
            again.Steps[0].RemoveAt(1);
            store.Save(again, "Heartland", out _);
            List<int> here = store.For("Heartland");
            Assert.Single(here);
            Assert.Single(store.Load(here[0]).Steps[0]);
            Assert.Single(store.For("Ignus"));
            Assert.Equal(2, store.Plans.Count);
        }

        [Fact]
        public void AnUnnamedPlanGetsAFreeNameAndAFullStoreSaysNo()
        {
            var store = new PlanStore();
            Assert.True(store.Save(new WingPlan { Name = "" }, "T", out string first));
            Assert.Equal("PLAN 1", first);
            for (int i = 1; i < PlanStore.Max; i++) Assert.True(store.Save(new WingPlan { Name = "P" + i }, "T", out _));
            Assert.False(store.Save(new WingPlan { Name = "ONE TOO MANY" }, "T", out _));
            Assert.True(store.Remove(0));
            Assert.Equal(PlanStore.Max - 1, store.Plans.Count);
        }

        [Fact]
        public void ABrokenFileOrStepIsSkippedWithAReason()
        {
            var errors = new List<string>();
            Assert.Empty(PlanStore.FromJson("not json", errors).Plans);
            Assert.Single(errors);
            errors.Clear();
            string json = "{\"plans\":[{\"name\":\"X\",\"theatre\":\"T\",\"lanes\":[[" +
                          "{\"kind\":\"Warp\",\"points\":[{\"x\":1,\"z\":2}]}," +
                          "{\"kind\":\"Move\",\"points\":[{\"x\":1e9,\"z\":2}]}," +
                          "{\"kind\":\"Orbit\",\"points\":[{\"x\":1,\"z\":2}],\"end\":\"Time\",\"endSeconds\":120," +
                          "\"start\":\"After\",\"afterLane\":3,\"afterStep\":5}]]}]}";
            PlanStore s = PlanStore.FromJson(json, errors);
            Assert.Equal(2, errors.Count);
            WingPlan p = s.Load(0);
            Assert.Single(p.Steps[0]);
            Assert.Equal(PlanStart.Exec, p.Steps[0][0].Start);   // an AFTER to a step that is not there starts on EXECUTE
            Assert.Equal(120f, p.Steps[0][0].EndSeconds);
            Assert.Empty(PlanStore.FromJson(null, errors).Plans);
        }
    }
}
