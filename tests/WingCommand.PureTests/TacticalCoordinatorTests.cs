using System;
using Xunit;

// Minimal engine and roster stubs for production target reservations.
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

            // Count one pilot's assignment and shot reservation as a single commitment.
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
        public void NativeTargetSwitchReleasesAbandonedSelectionsButPreservesShotsInFlight()
        {
            var first = new Unit();
            var second = new Unit();
            var third = new Unit();
            var owner = new Aircraft();
            TacticalCoordinator.NoteSelection(first, owner, 7f);
            TacticalCoordinator.NoteSelection(second, owner, 7f);
            Assert.Equal(0, TacticalCoordinator.CountCommitments(first));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(second));

            Assert.True(TacticalCoordinator.TryClaim(second, owner, 1, 3f));
            TacticalCoordinator.NoteSelection(third, owner, 7f);
            Assert.Equal(1, TacticalCoordinator.CountCommitments(second));
            Assert.Equal(1, TacticalCoordinator.CountClaims(second));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(third));
            Assert.False(TacticalCoordinator.TryClaim(second, new Aircraft(), 1, 3f));

            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(0, TacticalCoordinator.CountCommitments(second));
            Assert.Equal(1, TacticalCoordinator.CountCommitments(third));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EmptyOrDisabledSearchResultsClearOnlyThisPilotsSelection(bool disabledResult)
        {
            var target = new Unit();
            var owner = new Aircraft();
            var other = new Aircraft();
            TacticalCoordinator.NoteSelection(target, owner, 7f);
            TacticalCoordinator.NoteSelection(target, other, 7f);
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));

            TacticalCoordinator.NoteSelection(disabledResult ? new Unit { disabled = true } : null,
                                              owner, 7f);
            Assert.Equal(1, TacticalCoordinator.CountClaims(target));
            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target, other));
        }

        [Fact]
        public void BehaviourHandoffDropsSelectionWhileAnExistingShotStillCapsFire()
        {
            var target = new Unit();
            var owner = new Aircraft();
            TacticalCoordinator.NoteSelection(target, owner, 7f);
            Assert.True(TacticalCoordinator.TryClaim(target, owner, 1, 3f));

            TacticalCoordinator.ReleaseSelection(owner);
            Assert.Equal(1, TacticalCoordinator.CountClaims(target));
            Assert.False(TacticalCoordinator.TryClaim(target, new Aircraft(), 1, 3f));
            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
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

        [Theory]
        [InlineData(WingBehaviours.Rejoin)]
        [InlineData(WingBehaviours.MissileBreak)]
        [InlineData(WingBehaviours.DeckHold)]
        public void SuspendedDesignationsDoNotDiscourageAvailableShooters(string behaviour)
        {
            var target = new Unit();
            var member = new WingMember
            {
                Aircraft = new Aircraft(), AssignedTarget = target, BehaviourId = behaviour,
            };
            WingCommandManager.Instance.Wing.Members.Add(member);
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
            Assert.Same(target, member.AssignedTarget);
            member.BehaviourId = WingBehaviours.Task;
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void PendingOrUnavailablePilotsCannotPromiseAnAttack()
        {
            var target = new Unit();
            var member = new WingMember
            {
                Aircraft = new Aircraft(), AssignedTarget = target, DeliveryPending = true,
            };
            WingCommandManager.Instance.Wing.Members.Add(member);
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
            member.DeliveryPending = false;
            member.Alive = false;
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
        }

        [Fact]
        public void AShotAlreadyFiredRemainsReservedAfterTheShooterBreaksAway()
        {
            var target = new Unit();
            var member = new WingMember { Aircraft = new Aircraft(), AssignedTarget = target };
            WingCommandManager.Instance.Wing.Members.Add(member);
            Assert.True(TacticalCoordinator.TryClaim(target, member.Aircraft, 1, 3f));
            member.BehaviourId = WingBehaviours.Rejoin;
            Assert.Equal(1, TacticalCoordinator.CountCommitments(target));
            Assert.Equal(1, TacticalCoordinator.CountClaims(target));
            UnityEngine.Time.timeSinceLevelLoad += 3f;
            Assert.Equal(0, TacticalCoordinator.CountCommitments(target));
            Assert.Equal(0, TacticalCoordinator.CountClaims(target));
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
