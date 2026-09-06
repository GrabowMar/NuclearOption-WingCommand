using System;
using Xunit;

// Minimal engine/roster boundary for the linked production reservation coordinator.
namespace UnityEngine
{
    public static partial class Time
    {
        public static float timeSinceLevelLoad;
        public static int frameCount;
    }
}

namespace WingCommand
{
    internal partial class Unit
    {
        public bool disabled;
    }

    internal partial class Aircraft : Unit { }

    internal sealed partial class WingMember
    {
        public Aircraft Aircraft;
        public Unit AssignedTarget;
        public WingOrder Order = WingOrder.Attack;
    }

    internal partial class WingCommandManager
    {
        public static WingCommandManager Instance;
        public WingRegistry Wing;
    }
}

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class TacticalCoordinatorTests : IDisposable
    {
        public TacticalCoordinatorTests()
        {
            TacticalCoordinator.Reset();
            UnityEngine.Time.timeSinceLevelLoad = 10f;
            UnityEngine.Time.frameCount = 1;
            WingCommandManager.Instance = new WingCommandManager { Wing = new WingRegistry() };
        }

        public void Dispose()
        {
            TacticalCoordinator.Reset();
            WingCommandManager.Instance = null;
        }

        [Fact]
        public void SharedAttackAssignments_DoNotPreventTheFirstShot_AndStillCapConcurrentFire()
        {
            var target = new Unit();
            var first = new Aircraft();
            var second = new Aircraft();
            WingCommandManager.Instance.Wing.Members.AddRange(new[]
            {
                new WingMember { Aircraft = first, AssignedTarget = target },
                new WingMember { Aircraft = second, AssignedTarget = target },
            });

            Assert.Equal(1, TacticalCoordinator.CountCommitments(target, first));
            Assert.Equal(0, TacticalCoordinator.CountClaims(target, first));
            Assert.True(TacticalCoordinator.TryClaim(target, first, 1, 3f));
            Assert.False(TacticalCoordinator.TryClaim(target, second, 1, 3f));

            // Assignment plus a real reservation from the same pilot is one commitment.
            Assert.Equal(2, TacticalCoordinator.CountCommitments(target));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target, second));

            UnityEngine.Time.timeSinceLevelLoad = 13f;
            Assert.True(TacticalCoordinator.TryClaim(target, second, 1, 3f));
            Assert.False(TacticalCoordinator.TryClaim(target, first, 1, 3f));
        }

        [Fact]
        public void NativeSelection_AddsPressureButCannotBlockOrBypassTheFiringCap()
        {
            var target = new Unit();
            var nativePilot = new Aircraft();
            var wingman = new Aircraft();
            TacticalCoordinator.NoteSelection(target, nativePilot, 7f);

            Assert.Equal(1, TacticalCoordinator.CountCommitments(target, wingman));
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
            Assert.True(TacticalCoordinator.TryClaim(target, wingman, 1, 3f));
            Assert.False(TacticalCoordinator.TryClaim(target, nativePilot, 1, 3f));

            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
            UnityEngine.Time.timeSinceLevelLoad += 4f;
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void NativeSelectionRenewal_DoesNotRenewTheSamePilotsFiringReservation()
        {
            var target = new Unit();
            var owner = new Aircraft();
            TacticalCoordinator.NoteSelection(target, owner, 7f);
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));

            UnityEngine.Time.timeSinceLevelLoad += 2f;
            TacticalCoordinator.NoteSelection(target, owner, 7f);
            UnityEngine.Time.timeSinceLevelLoad += 1f;
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
            Assert.True(TacticalCoordinator.TryClaim(target, new Aircraft(), 1, 3f));
            TacticalCoordinator.Release(owner);
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void JamAssignment_DoesNotReserveOffensiveCapacity()
        {
            var target = new Unit();
            WingCommandManager.Instance.Wing.Members.Add(new WingMember
            {
                Aircraft = new Aircraft(), AssignedTarget = target, Order = WingOrder.JamTarget,
            });
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void ExpiredOrDisabledOwners_FreeCapacityEvenAfterThisFramesPrune()
        {
            var target = new Unit();
            var first = new Aircraft();
            var second = new Aircraft();
            Assert.True(TacticalCoordinator.TryClaim(target, first, 1, 3f));
            Assert.Equal(1, TacticalCoordinator.CountClaims(target));

            first.disabled = true;
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
            Assert.True(TacticalCoordinator.TryClaim(target, second, 1, 3f));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));

            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void ActiveOwnerCanRenew_AndReleaseMakesRoomImmediately()
        {
            var target = new Unit();
            var owner = new Aircraft();
            var other = new Aircraft();
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));
            UnityEngine.Time.timeSinceLevelLoad += 2f;
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));
            UnityEngine.Time.timeSinceLevelLoad += 2f;
            Assert.False(TacticalCoordinator.TryClaim(target, other, 1, 3f));
            TacticalCoordinator.Release(owner);
            Assert.True(TacticalCoordinator.TryClaim(target, other, 1, 3f));
        }

        [Fact]
        public void NextFramePrune_DoesNotDetachNewReservations_AndResetDropsAllClaims()
        {
            var target = new Unit();
            var owner = new Aircraft();
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 1f));
            UnityEngine.Time.timeSinceLevelLoad += 1f;
            UnityEngine.Time.frameCount++;
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));
            Assert.Equal(1, TacticalCoordinator.CountClaims(target));
            TacticalCoordinator.Reset();
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
        }
    }
}
