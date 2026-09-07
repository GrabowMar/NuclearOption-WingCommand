using System;
using Xunit;

// Only native initialization/flight sensing is stubbed. The delayed recruitment
// queue under test is the same source that runs in the plugin.
namespace WingCommand
{
    // WingPilot and Pilot are shared, and live in GameTypeStubs.cs.
    internal partial class Aircraft
    {
        public bool LocalSim = true;
        public string unitName = "Test departure";
        public bool Airborne;
    }

    internal sealed partial class WingMember
    {
        public int Slot;
        public int ActivationCalls;

        public bool ActivateWhenAirborne()
        {
            ActivationCalls++;
            if (!DeliveryPending || !Aircraft.Airborne) return false;
            DeliveryPending = false;
            return true;
        }
    }

    internal sealed partial class WingRegistry
    {
        public WingMember Find(Aircraft aircraft) => Members.Find(m => m.Aircraft == aircraft);
        public static bool HasRoom(int count) => count < 8;
        public static Pilot PrimaryPilot(Aircraft aircraft) => new Pilot();

        public WingMember Add(Aircraft aircraft, bool deferCommand, WingPilot preferredPilot)
        {
            var member = new WingMember { Aircraft = aircraft, DeliveryPending = deferCommand, Slot = Count + 1 };
            Members.Add(member);
            return member;
        }
    }

    internal partial class WingCommandManager
    {
        public void FlushRecruitsForTest() => FlushRecruitQueue();
        public int QueuedRecruitsForTest => recruitQueue.Count;
    }
}

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class RecruitLifecycleTests : IDisposable
    {
        private readonly WingCommandManager manager;

        public RecruitLifecycleTests()
        {
            UnityEngine.Time.timeSinceLevelLoad = 0f;
            manager = new WingCommandManager { Wing = new WingRegistry() };
        }

        public void Dispose() => UnityEngine.Time.timeSinceLevelLoad = 0f;

        [Fact]
        public void StagedDepartureRetainsItsAssignmentAfterTheOldDeadlineAndActivatesOnLiftoff()
        {
            var aircraft = new Aircraft();
            manager.QueueRecruit(aircraft);
            WingMember member = manager.Wing.Find(aircraft);
            UnityEngine.Time.timeSinceLevelLoad = 900f;
            manager.FlushRecruitsForTest();
            Assert.True(manager.Wing.Contains(member));
            Assert.True(member.DeliveryPending);
            Assert.Equal(1, manager.QueuedRecruitsForTest);

            aircraft.Airborne = true;
            manager.FlushRecruitsForTest();
            Assert.False(member.DeliveryPending);
            Assert.True(manager.Wing.Contains(member));
            Assert.Equal(0, manager.QueuedRecruitsForTest);
        }

        [Fact]
        public void MemberAlreadyActivatedByItsOwnTickLeavesQueueWithoutTimingOut()
        {
            var aircraft = new Aircraft();
            manager.QueueRecruit(aircraft);
            WingMember member = manager.Wing.Find(aircraft);
            member.DeliveryPending = false;
            UnityEngine.Time.timeSinceLevelLoad = 900f;
            manager.FlushRecruitsForTest();
            Assert.True(manager.Wing.Contains(member));
            Assert.Equal(0, manager.QueuedRecruitsForTest);
            Assert.Equal(0, member.ActivationCalls);
        }

        [Fact]
        public void ExplicitlyReleasedDepartureIsNotRecruitedAgain()
        {
            var aircraft = new Aircraft();
            manager.QueueRecruit(aircraft);
            manager.Wing.Members.Clear();
            UnityEngine.Time.timeSinceLevelLoad = 900f;
            manager.FlushRecruitsForTest();
            Assert.Empty(manager.Wing.Members);
            Assert.Equal(0, manager.QueuedRecruitsForTest);
        }

        [Fact]
        public void NewAirframeGetsItsInitializationGraceBeforeHandoff()
        {
            var aircraft = new Aircraft { Airborne = true };
            manager.QueueRecruit(aircraft);
            manager.FlushRecruitsForTest();
            WingMember member = manager.Wing.Find(aircraft);
            Assert.True(member.DeliveryPending);
            Assert.Equal(0, member.ActivationCalls);
            UnityEngine.Time.timeSinceLevelLoad = 0.3f;
            manager.FlushRecruitsForTest();
            Assert.False(member.DeliveryPending);
        }
    }
}
