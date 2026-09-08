using System;
using Xunit;

// Exercise production firing policy, cadence, payload retention, and routing; replace only native
// target and shot boundaries.
namespace WingCommand
{
    internal sealed partial class WingMember
    {
        public string BehaviourId = WingBehaviours.Task;
        internal OrderEngagementAuthority EngagementAuthority =>
            OrderRoePolicy.AuthorityFor(BehaviourId, Order);
    }

    internal static class WingWeapons
    {
        internal enum Allow { None, MissilesOnly, AirOnly, AirAndGround, GroundOnly }
        public static Unit ShotTarget;
        public static Allow ShotAllow;
        public static int Shots;
        public static float FireInterval(Aircraft aircraft) => 5f;
        public static bool Engage(Aircraft aircraft, Pilot pilot, Allow allow, float range)
        {
            ShotAllow = allow;
            Shots++;
            return true;
        }
        public static bool EngageSpecific(Aircraft aircraft, Pilot pilot, Unit target, float range)
        {
            ShotTarget = target;
            Shots++;
            return true;
        }
    }

    internal static class RoeRules
    {
        public static WingRoe Current;
        public static bool MissileDefenceAvailable;
        public static Unit Threat;
        public static int PrioritySearches;
        public static WingWeapons.Allow WeaponsFree(WingRoe roe, Aircraft aircraft) =>
            MissileDefenceAvailable ? WingWeapons.Allow.MissilesOnly :
            roe == WingRoe.Free ? WingWeapons.Allow.AirAndGround :
            roe == WingRoe.Tight ? WingWeapons.Allow.AirOnly : WingWeapons.Allow.None;
        public static float ExplicitOrderRange() => 20000f;
        public static float EngageRange(WingRoe roe) => 10000f;
        public static Unit PriorityTarget(WingRoe roe, Aircraft aircraft, Aircraft leader, float range)
        {
            PrioritySearches++;
            return Threat;
        }
    }

    internal static class WingComms
    {
        internal enum Call { Defending, Covering }
        public static void Say(WingMember member, Call call) { }
    }
}

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class SlotEngagementTests : IDisposable
    {
        public SlotEngagementTests()
        {
            WingFidelity.Begin(WingMode.Smart);
            UnityEngine.Time.timeSinceLevelLoad = 20f;
            RoeRules.Current = WingRoe.Hold;
            RoeRules.MissileDefenceAvailable = false;
            RoeRules.Threat = null;
            RoeRules.PrioritySearches = 0;
            WingWeapons.ShotTarget = null;
            WingWeapons.ShotAllow = WingWeapons.Allow.None;
            WingWeapons.Shots = 0;
        }

        public void Dispose() => WingFidelity.Begin(WingMode.Smart);

        private static bool Run(WingMember member) =>
            new SlotEngagement(0.5f).Run(member, new Aircraft(), new Pilot(), new Aircraft());

        [Theory]
        [InlineData(WingOrder.Attack, WingBehaviours.Rejoin)]
        [InlineData(WingOrder.FireForEffect, WingBehaviours.Rejoin)]
        [InlineData(WingOrder.Attack, WingBehaviours.DeckHold)]
        public void HoldNeverFiresTheTargetRetainedByASuspendedAttack(WingOrder order, string behaviour)
        {
            var target = new Unit();
            var member = new WingMember { Order = order, BehaviourId = behaviour, AssignedTarget = target };
            Assert.False(Run(member));
            Assert.Equal(0, WingWeapons.Shots);
            Assert.Same(target, member.AssignedTarget);
            Assert.Equal(order, member.Order);
        }

        [Fact]
        public void TightSelectsAProtectiveThreatInsteadOfTheSuspendedGroundDesignation()
        {
            RoeRules.Current = WingRoe.Tight;
            RoeRules.Threat = new Unit();
            var assigned = new Unit();
            var member = new WingMember { BehaviourId = WingBehaviours.Rejoin, AssignedTarget = assigned };
            Assert.True(Run(member));
            Assert.Same(RoeRules.Threat, WingWeapons.ShotTarget);
            Assert.Same(assigned, member.AssignedTarget);
        }

        [Fact]
        public void TightHasNoOpportunityFallbackWhenThereIsNoProtectiveThreat()
        {
            RoeRules.Current = WingRoe.Tight;
            var member = new WingMember { BehaviourId = WingBehaviours.Rejoin, AssignedTarget = new Unit() };
            Assert.False(Run(member));
            Assert.Equal(0, WingWeapons.Shots);
        }

        [Fact]
        public void FreeReconsidersOpportunityTargetsDuringRecallWithoutUsingExplicitAuthority()
        {
            RoeRules.Current = WingRoe.Free;
            var member = new WingMember { BehaviourId = WingBehaviours.Rejoin, AssignedTarget = new Unit() };
            Assert.True(Run(member));
            Assert.Null(WingWeapons.ShotTarget);
            Assert.Equal(WingWeapons.Allow.AirAndGround, WingWeapons.ShotAllow);
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        public void AnActiveExplicitOrderRetainsItsAuthorizationUnderHold(WingOrder order)
        {
            WingFidelity.Begin(WingMode.Performance);
            var member = new WingMember { Order = order, AssignedTarget = new Unit() };
            Assert.True(Run(member));
            Assert.Same(member.AssignedTarget, WingWeapons.ShotTarget);
        }

        [Fact]
        public void ExpiredDesignationCannotTurnIntoAnOpportunityOrder()
        {
            RoeRules.Current = WingRoe.Free;
            var member = new WingMember { AssignedTarget = new Unit { disabled = true } };
            Assert.False(Run(member));
            Assert.Equal(0, WingWeapons.Shots);
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.Engage)]
        [InlineData(WingOrder.JamTarget)]
        [InlineData(WingOrder.Formation)]
        public void MissileDefencePreemptsEveryStationWeaponsTaskEvenInPerformanceMode(WingOrder order)
        {
            WingFidelity.Begin(WingMode.Performance);
            RoeRules.Current = WingRoe.Free;
            RoeRules.MissileDefenceAvailable = true;
            var member = new WingMember { Order = order, AssignedTarget = new Unit() };
            Assert.True(Run(member));
            Assert.Null(WingWeapons.ShotTarget);
            Assert.Equal(WingWeapons.Allow.MissilesOnly, WingWeapons.ShotAllow);
            Assert.Equal(0, RoeRules.PrioritySearches);
        }

        [Theory]
        [InlineData(WingRoe.Tight)]
        [InlineData(WingRoe.Free)]
        public void PerformanceModeSuppressesDiscretionaryFireDuringRecall(WingRoe roe)
        {
            WingFidelity.Begin(WingMode.Performance);
            RoeRules.Current = roe;
            RoeRules.Threat = new Unit();
            var member = new WingMember { BehaviourId = WingBehaviours.Rejoin, AssignedTarget = new Unit() };
            Assert.False(Run(member));
            Assert.Equal(0, WingWeapons.Shots);
            Assert.Equal(0, RoeRules.PrioritySearches);
        }

        [Fact]
        public void MissileInterceptionKeepsItsShortCadenceAfterAnOffensiveShot()
        {
            RoeRules.Current = WingRoe.Free;
            var member = new WingMember { Order = WingOrder.Formation };
            var engagement = new SlotEngagement(0.5f);
            var aircraft = new Aircraft();
            var pilot = new Pilot();
            Assert.True(engagement.Run(member, aircraft, pilot, aircraft));
            UnityEngine.Time.timeSinceLevelLoad += 1f;
            Assert.False(engagement.Run(member, aircraft, pilot, aircraft));
            RoeRules.MissileDefenceAvailable = true;
            UnityEngine.Time.timeSinceLevelLoad += 0.5f;
            Assert.True(engagement.Run(member, aircraft, pilot, aircraft));
            Assert.Equal(WingWeapons.Allow.MissilesOnly, WingWeapons.ShotAllow);
        }
    }
}
