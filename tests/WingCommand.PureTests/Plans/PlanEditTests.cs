using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P2: editing a wing plan from the map's tools and the step editor (spec bezel v2 §5 PLAN › ELEMENTS, §6).</summary>
    public class PlanEditTests
    {
        private static PlanStep Move(float x) => PlanEdit.NewStep(PlanTool.Move, x, 0f, float.NaN, 0f);

        [Fact]
        public void EachToolMakesItsStepWithAnEndThatCanHappen()
        {
            Assert.Equal(PlanEnd.Arrive, PlanEdit.NewStep(PlanTool.Move, 1f, 2f, float.NaN, 0f).End);
            Assert.Equal(PlanEnd.Arrive, PlanEdit.NewStep(PlanTool.Route, 1f, 2f, float.NaN, 0f).End);
            PlanStep orbit = PlanEdit.NewStep(PlanTool.Orbit, 1f, 2f, 3000f, 0f);
            Assert.Equal(PlanEnd.Time, orbit.End);
            Assert.True(orbit.EndSeconds > 0f);
            Assert.Equal(3000f, orbit.Points[0].Altitude);
            PlanStep cap = PlanEdit.NewStep(PlanTool.Cap, 1f, 2f, float.NaN, 10000f);
            Assert.Equal(PlanKind.Cap, cap.Kind);
            Assert.Equal(PlanEnd.Bingo, cap.End);
            Assert.Equal(10000f, cap.Radius);
            Assert.Equal(PlanEnd.Bingo, PlanEdit.NewStep(PlanTool.Sweep, 1f, 2f, float.NaN, 12000f).End);
            Assert.Equal(ArrivalAction.None, PlanEdit.NewStep(PlanTool.Land, 1f, 2f, float.NaN, 0f).Points[0].Action);
            Assert.Equal(PlanKind.Land, PlanEdit.NewStep(PlanTool.Land, 1f, 2f, float.NaN, 0f).Kind);
            Assert.Equal(PlanKind.Cargo, PlanEdit.NewStep(PlanTool.Cargo, 1f, 2f, float.NaN, 0f).Kind);
            PlanStep attack = PlanEdit.Attack(7u);
            Assert.Equal(PlanEnd.TargetsDown, attack.End);
            Assert.Equal(new uint[] { 7u }, attack.Targets);
            Assert.Empty(PlanRules.Validate(Plan(orbit, cap, attack)));
        }

        private static WingPlan Plan(params PlanStep[] steps)
        {
            var plan = new WingPlan();
            for (int i = 0; i < steps.Length; i++) plan.Add(i % WingPlan.Lanes, steps[i]);
            return plan;
        }

        [Fact]
        public void InsertingAfterAStepKeepsTheOtherLanesAfterLinksOnTheirSteps()
        {
            var plan = new WingPlan();
            plan.Add(0, Move(1f));
            plan.Add(0, Move(2f));
            PlanStep waits = Move(9f);
            waits.Start = PlanStart.After;
            waits.AfterLane = 0;
            waits.AfterStep = 1;
            plan.Add(1, waits);
            Assert.Equal(1, PlanEdit.Insert(plan, 0, 0, Move(5f)));
            Assert.Equal(5f, plan.Steps[0][1].Points[0].X);
            Assert.Equal(2, waits.AfterStep);                     // still waits for the step at x = 2
            Assert.Equal(3, PlanEdit.Insert(plan, 0, -1, Move(7f)));   // -1: at the lane's end
        }

        [Fact]
        public void AFullLaneTakesNoMore()
        {
            var plan = new WingPlan();
            for (int i = 0; i < WingPlan.MaxSteps; i++) Assert.True(PlanEdit.Insert(plan, 2, -1, Move(i)) >= 0);
            Assert.Equal(-1, PlanEdit.Insert(plan, 2, -1, Move(99f)));
        }

        [Fact]
        public void RemovingAStepFreesWhatWaitedForItAndShiftsTheRest()
        {
            var plan = new WingPlan();
            plan.Add(0, Move(1f));
            plan.Add(0, Move(2f));
            PlanStep onFirst = Move(8f), onSecond = Move(9f);
            onFirst.Start = onSecond.Start = PlanStart.After;
            onFirst.AfterLane = onSecond.AfterLane = 0;
            onFirst.AfterStep = 0;
            onSecond.AfterStep = 1;
            plan.Add(1, onFirst);
            plan.Add(2, onSecond);
            PlanEdit.Remove(plan, 0, 0);
            Assert.Single(plan.Steps[0]);
            Assert.Equal(PlanStart.Exec, onFirst.Start);
            Assert.Equal(-1, onFirst.AfterLane);
            Assert.Equal(0, onSecond.AfterStep);
        }

        [Fact]
        public void MovingAStepUpOrDownCarriesItsLinks()
        {
            var plan = new WingPlan();
            plan.Add(0, Move(1f));
            plan.Add(0, Move(2f));
            PlanStep waits = Move(9f);
            waits.Start = PlanStart.After;
            waits.AfterLane = 0;
            waits.AfterStep = 1;
            plan.Add(1, waits);
            Assert.True(PlanEdit.MoveStep(plan, 0, 1, -1));
            Assert.Equal(2f, plan.Steps[0][0].Points[0].X);
            Assert.Equal(0, waits.AfterStep);
            Assert.False(PlanEdit.MoveStep(plan, 0, 0, -1));
            Assert.False(PlanEdit.MoveStep(plan, 0, 1, 1));
        }

        [Fact]
        public void AfterCyclesThroughTheOtherLanesStepsSkippingOnesThatWouldWaitForItself()
        {
            var plan = new WingPlan();
            plan.Add(0, Move(1f));
            plan.Add(0, Move(2f));
            plan.Add(1, Move(3f));
            PlanStep b1 = plan.Steps[1][0];
            Assert.True(PlanEdit.NextAfter(plan, 1, 0));
            Assert.Equal((0, 0), (b1.AfterLane, b1.AfterStep));
            Assert.Equal(PlanStart.After, b1.Start);
            Assert.True(PlanEdit.NextAfter(plan, 1, 0));
            Assert.Equal((0, 1), (b1.AfterLane, b1.AfterStep));
            Assert.True(PlanEdit.NextAfter(plan, 1, 0));   // wraps
            Assert.Equal((0, 0), (b1.AfterLane, b1.AfterStep));
            // A2 waiting for B1 would close the loop A2 -> B1 -> A1? No: B1 waits for A1 only, so A2 may wait for B1.
            Assert.True(PlanEdit.NextAfter(plan, 0, 1));
            Assert.Equal((1, 0), (plan.Steps[0][1].AfterLane, plan.Steps[0][1].AfterStep));
            // A1 may not wait for B1 (B1 waits for A1).
            Assert.False(PlanEdit.NextAfter(plan, 0, 0));
            Assert.Empty(PlanRules.Validate(plan));
        }

        [Fact]
        public void ReplacingAStepsPointKeepsItsAltitudeAndAddingARoutePointGrowsIt()
        {
            PlanStep orbit = PlanEdit.NewStep(PlanTool.Orbit, 1f, 2f, 3000f, 0f);
            PlanEdit.Replace(orbit, 10f, 20f);
            Assert.Equal(10f, orbit.Points[0].X);
            Assert.Equal(3000f, orbit.Points[0].Altitude);
            PlanStep route = PlanEdit.NewStep(PlanTool.Route, 1f, 2f, float.NaN, 0f);
            Assert.True(PlanEdit.AddPoint(route, 3f, 4f));
            Assert.Equal(2, route.Points.Length);
            PlanStep attack = PlanEdit.Attack(1u);
            Assert.True(PlanEdit.AddTarget(attack, 2u));
            Assert.False(PlanEdit.AddTarget(attack, 2u));
            Assert.Equal(2, attack.Targets.Length);
        }

        [Fact]
        public void TheEndPointIsTheStepsLastPoint()
        {
            PlanStep route = PlanEdit.NewStep(PlanTool.Route, 1f, 2f, float.NaN, 0f);
            PlanEdit.AddPoint(route, 3f, 4f);
            Assert.True(PlanEdit.EndPoint(route, out float x, out float z));
            Assert.Equal((3f, 4f), (x, z));
            Assert.False(PlanEdit.EndPoint(new PlanStep { Kind = PlanKind.Rtb }, out _, out _));
        }
    }
}
