using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P1: the wing plan's model, compilation, validation and runner (spec WMC rebuild §PLAN; bezel v2 §6).</summary>
    public class PlanRunnerTests
    {
        private static readonly Waypoint P = new Waypoint { X = 1000f, Z = 2000f, Altitude = 3000f, Speed = float.NaN };
        private static readonly Waypoint Q = new Waypoint { X = 5000f, Z = 2000f, Altitude = 3000f, Speed = float.NaN };

        private static PlanStep Step(PlanKind kind, PlanStart start = PlanStart.Exec, PlanEnd end = PlanEnd.Arrive) =>
            new PlanStep { Kind = kind, Points = new[] { P }, Start = start, End = end };

        private static LaneFacts[] Facts() => new LaneFacts[WingPlan.Lanes];

        private static List<PlanEmit> Tick(PlanRunner r, float t, LaneFacts[] f)
        {
            var into = new List<PlanEmit>();
            r.Tick(t, f, into);
            return into;
        }

        [Fact]
        public void NothingIsSentBeforeExecuteThenEachLanesFirstStepGoes()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[1].Add(Step(PlanKind.Orbit));
            var r = new PlanRunner(plan);
            Assert.Empty(Tick(r, 0f, Facts()));
            r.Execute(1f);
            List<PlanEmit> sent = Tick(r, 1f, Facts());
            Assert.Equal(2, sent.Count);
            Assert.Equal(0, sent[0].Lane);
            Assert.Equal(OrderKind.Task, sent[0].Order.Kind);
            Assert.Equal(TaskKind.Move, sent[0].Order.Task.Kind);
            Assert.Equal(OrderSource.Plan, sent[0].Order.Source);
            Assert.Equal(1, sent[1].Order.Scope.Element);
            Assert.Equal(StepState.Running, r.State(0, 0));
            Assert.Empty(Tick(r, 1.5f, Facts()));
        }

        [Fact]
        public void ArriveEndsAStepAndTheNextInTheLaneGoes()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[0].Add(Step(PlanKind.Orbit));
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            LaneFacts[] f = Facts();
            f[0].TaskDone = true;
            List<PlanEmit> sent = Tick(r, 10f, f);
            Assert.Equal(StepState.Done, r.State(0, 0));
            Assert.Single(sent);
            Assert.Equal(TaskKind.Orbit, sent[0].Order.Task.Kind);
            Assert.Equal(1, r.Current(0));
        }

        [Fact]
        public void TimeBingoWinchesterAndTargetsDownEndTheirSteps()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(new PlanStep { Kind = PlanKind.Orbit, Points = new[] { P }, End = PlanEnd.Time, EndSeconds = 30f });
            plan.Steps[1].Add(Step(PlanKind.Cap, PlanStart.Exec, PlanEnd.Bingo));
            plan.Steps[2].Add(Step(PlanKind.Sweep, PlanStart.Exec, PlanEnd.Winchester));
            plan.Steps[3].Add(new PlanStep { Kind = PlanKind.Attack, Targets = new uint[] { 7 }, End = PlanEnd.TargetsDown });
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            Tick(r, 29f, Facts());
            Assert.Equal(StepState.Running, r.State(0, 0));
            LaneFacts[] f = Facts();
            f[1].Bingo = f[2].Winchester = f[3].TargetsDown = true;
            Tick(r, 31f, f);
            for (int lane = 0; lane < 4; lane++) Assert.Equal(StepState.Done, r.State(lane, 0));
        }

        [Fact]
        public void TPlusWaitsItsSecondsAfterExecute()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(new PlanStep { Kind = PlanKind.Move, Points = new[] { P }, Start = PlanStart.TPlus, Delay = 60f });
            var r = new PlanRunner(plan);
            r.Execute(100f);
            Assert.Empty(Tick(r, 150f, Facts()));
            Assert.Single(Tick(r, 160f, Facts()));
        }

        [Fact]
        public void AfterWaitsForAnotherLanesStepAndItsDelay()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[1].Add(new PlanStep { Kind = PlanKind.Attack, Targets = new uint[] { 9 }, Start = PlanStart.After, AfterLane = 0, AfterStep = 0, Delay = 20f });
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Assert.Single(Tick(r, 0f, Facts()));
            LaneFacts[] f = Facts();
            f[0].TaskDone = true;
            Assert.Empty(Tick(r, 50f, f));
            Assert.Empty(Tick(r, 60f, Facts()));
            List<PlanEmit> sent = Tick(r, 71f, Facts());
            Assert.Single(sent);
            Assert.Equal(OrderKind.Attack, sent[0].Order.Kind);
            Assert.Equal(new uint[] { 9 }, sent[0].Order.Units);
        }

        [Fact]
        public void APlayerOrderHoldsTheLaneAndResumeSendsItsStepAgain()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Orbit));
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            LaneFacts[] f = Facts();
            f[0].PlayerOrdered = true;
            Assert.Empty(Tick(r, 5f, f));
            Assert.Equal(StepState.Held, r.State(0, 0));
            f[0].TaskDone = true;
            Assert.Empty(Tick(r, 6f, f));
            Assert.Equal(StepState.Held, r.State(0, 0));
            r.Resume(0);
            List<PlanEmit> sent = Tick(r, 7f, Facts());
            Assert.Single(sent);
            Assert.Equal(StepState.Running, r.State(0, 0));
        }

        [Fact]
        public void ARefusalBlocksTheLaneUntilRetryOrSkip()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[0].Add(Step(PlanKind.Orbit));
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            r.Refused(0, "Nobody can go");
            Assert.Equal(StepState.Blocked, r.State(0, 0));
            Assert.Equal("Nobody can go", r.Why(0));
            Assert.Empty(Tick(r, 1f, Facts()));
            r.Retry(0);
            Assert.Single(Tick(r, 2f, Facts()));
            r.Refused(0, "Still no");
            r.Skip(0, 3f);
            Assert.Equal(StepState.Skipped, r.State(0, 0));
            List<PlanEmit> sent = Tick(r, 3f, Facts());
            Assert.Single(sent);
            Assert.Equal(TaskKind.Orbit, sent[0].Order.Task.Kind);
        }

        [Fact]
        public void AResumedStepThatNeverWentOutStillWaitsForItsStart()
        {
            // Review (plans): a step held while still waiting for its T+ went out on RESUME, eight minutes early.
            var plan = new WingPlan();
            plan.Steps[1].Add(new PlanStep { Kind = PlanKind.Move, Points = new[] { P }, Start = PlanStart.TPlus, Delay = 600f });
            var r = new PlanRunner(plan);
            r.Execute(0f);
            LaneFacts[] f = Facts();
            f[1].PlayerOrdered = true;
            Assert.Empty(Tick(r, 60f, f));
            Assert.Equal(StepState.Held, r.State(1, 0));
            r.Resume(1);
            Assert.Empty(Tick(r, 121f, Facts()));
            Assert.Single(Tick(r, 601f, Facts()));
        }

        [Fact]
        public void SkippingAfterARetryDoesNotSendTheNextStepBeforeItsStart()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[0].Add(new PlanStep { Kind = PlanKind.Orbit, Points = new[] { P }, Start = PlanStart.TPlus, Delay = 600f, End = PlanEnd.Time, EndSeconds = 60f });
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            r.Refused(0, "no");
            r.Retry(0);
            r.Skip(0, 5f);
            Assert.Empty(Tick(r, 10f, Facts()));
            Assert.Single(Tick(r, 600f, Facts()));
        }

        [Fact]
        public void AbortStopsEveryLane()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[0].Add(Step(PlanKind.Orbit));
            var r = new PlanRunner(plan);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            r.Abort();
            LaneFacts[] f = Facts();
            f[0].TaskDone = true;
            Assert.Empty(Tick(r, 5f, f));
            Assert.False(r.Running);
        }

        [Fact]
        public void EachKindCompilesToItsOrder()
        {
            Assert.Equal(TaskKind.Route, PlanCompile.Order(new PlanStep { Kind = PlanKind.Route, Points = new[] { P, Q } }, 1).Task.Kind);
            WingOrder cap = PlanCompile.Order(new PlanStep { Kind = PlanKind.Cap, Points = new[] { P }, Radius = 10000f }, 2);
            Assert.Equal(10000f, cap.Task.GuardRadius);
            Assert.Equal(2, cap.Scope.Element);
            Assert.True(PlanCompile.Order(new PlanStep { Kind = PlanKind.Sweep, Points = new[] { P }, Radius = 12000f }, 1).Task.GuardRadius > 0f);
            Assert.Equal(ArrivalAction.Land, PlanCompile.Order(new PlanStep { Kind = PlanKind.Land, Points = new[] { P } }, 1).Task.Points[0].Action);
            Assert.Equal(ArrivalAction.Cargo, PlanCompile.Order(new PlanStep { Kind = PlanKind.Cargo, Points = new[] { P } }, 1).Task.Points[0].Action);
            Assert.Equal(OrderKind.Rtb, PlanCompile.Order(new PlanStep { Kind = PlanKind.Rtb }, 1).Kind);
            Assert.Equal(OrderKind.Refit, PlanCompile.Order(new PlanStep { Kind = PlanKind.Refit }, 1).Kind);
            Assert.Equal(OrderKind.FormUp, PlanCompile.Order(new PlanStep { Kind = PlanKind.FormUp }, 1).Kind);
            // Lane A is element A, never the wing: a wing-scoped task merges every other element back into A.
            WingOrder a = PlanCompile.Order(new PlanStep { Kind = PlanKind.Orbit, Points = new[] { P } }, 0);
            Assert.Equal(ScopeKind.Element, a.Scope.Kind);
            Assert.Equal(0, a.Scope.Element);
        }

        [Fact]
        public void OnlyOrdersThatChangeWhatAnElementDoesHoldItsLane()
        {
            foreach (OrderKind k in new[] { OrderKind.Task, OrderKind.FormUp, OrderKind.Rtb, OrderKind.Refit, OrderKind.Engage,
                         OrderKind.Attack, OrderKind.Splash, OrderKind.BreakOff, OrderKind.LandHere, OrderKind.TakeOff,
                         OrderKind.DeliverCargo, OrderKind.EscortMe, OrderKind.EscortTarget })
                Assert.True(PlanRules.Holds(k), k.ToString());
            foreach (OrderKind k in new[] { OrderKind.Ecm, OrderKind.Maneuver, OrderKind.SetOverride, OrderKind.SetDoctrine,
                         OrderKind.SetSpacing, OrderKind.NextShape, OrderKind.Stack, OrderKind.Afterburner, OrderKind.SkipLeg,
                         OrderKind.RenameElement, OrderKind.Call, OrderKind.BogeyDope, OrderKind.Eject, OrderKind.Release,
                         // Review (plans): AIR SAR sends one helicopter whatever the scope; it holds no lane.
                         OrderKind.Rescue })
                Assert.False(PlanRules.Holds(k), k.ToString());
        }

        [Fact]
        public void AnOrbitCapOrSweepThatEndsOnArrivalIsRefusedAndTargetsDownNeedsAnAttack()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Orbit));
            plan.Steps[1].Add(Step(PlanKind.Cap));
            plan.Steps[2].Add(Step(PlanKind.Move, PlanStart.Exec, PlanEnd.TargetsDown));
            List<string> errors = PlanRules.Validate(plan);
            Assert.Contains(errors, e => e.StartsWith("A1") && e.Contains("never arrives"));
            Assert.Contains(errors, e => e.StartsWith("B1") && e.Contains("never arrives"));
            Assert.Contains(errors, e => e.StartsWith("C1") && e.Contains("no targets"));
        }

        [Fact]
        public void APlanIsFinishedOnceEveryLaneIsThrough()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(Step(PlanKind.Move));
            plan.Steps[1].Add(Step(PlanKind.Move));
            var r = new PlanRunner(plan);
            Assert.False(r.Finished);
            r.Execute(0f);
            Tick(r, 0f, Facts());
            LaneFacts[] f = Facts();
            f[0].TaskDone = true;
            Tick(r, 5f, f);
            Assert.False(r.Finished);
            r.Skip(1, 6f);
            Assert.True(r.Finished);
        }

        [Fact]
        public void ValidationNamesEachProblem()
        {
            var plan = new WingPlan();
            plan.Steps[0].Add(new PlanStep { Kind = PlanKind.Route, Points = new[] { P } });
            plan.Steps[1].Add(new PlanStep { Kind = PlanKind.Attack });
            plan.Steps[2].Add(new PlanStep { Kind = PlanKind.Move, Points = new[] { P }, Start = PlanStart.After, AfterLane = 3, AfterStep = 0 });
            plan.Steps[3].Add(new PlanStep { Kind = PlanKind.Move, Points = new[] { P }, Start = PlanStart.After, AfterLane = 2, AfterStep = 0 });
            List<string> errors = PlanRules.Validate(plan);
            Assert.Contains(errors, e => e.StartsWith("A1") && e.Contains("two points"));
            Assert.Contains(errors, e => e.StartsWith("B1") && e.Contains("target"));
            Assert.Contains(errors, e => e.Contains("waits for itself"));
            var ok = new WingPlan();
            ok.Steps[0].Add(Step(PlanKind.Move));
            ok.Steps[1].Add(new PlanStep { Kind = PlanKind.Orbit, Points = new[] { P }, Start = PlanStart.After, AfterLane = 0, AfterStep = 0, End = PlanEnd.Time, EndSeconds = 120f });
            Assert.Empty(PlanRules.Validate(ok));
        }

        [Fact]
        public void APlanHoldsFourLanesOfTwelveSteps()
        {
            var plan = new WingPlan();
            Assert.Equal(4, plan.Steps.Length);
            for (int i = 0; i < WingPlan.MaxSteps; i++) Assert.True(plan.Add(0, Step(PlanKind.Orbit)));
            Assert.False(plan.Add(0, Step(PlanKind.Orbit)));
            Assert.Equal("A3", PlanRules.Name(0, 2));
        }
    }
}
