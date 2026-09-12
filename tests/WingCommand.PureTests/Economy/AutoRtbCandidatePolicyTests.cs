using Xunit;

namespace WingCommand.PureTests
{
    public class AutoRtbCandidatePolicyTests
    {
        [Fact]
        public void AmbientAiAircraftIsEligibleForAutoRtb()
        {
            bool eligible = AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false,
                sameHq: true,
                hasPlayer: false,
                isWingMemberOrLeader: false,
                isRecruitPending: false,
                isPurchased: false,
                isDeparting: false,
                isPilotUnavailable: false,
                isLanding: false);

            Assert.True(eligible);
        }

        [Fact]
        public void WingMemberOrLeaderIsNeverEligibleForAutoRtb()
        {
            bool eligible = AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false,
                sameHq: true,
                hasPlayer: false,
                isWingMemberOrLeader: true,
                isRecruitPending: false,
                isPurchased: false,
                isDeparting: false,
                isPilotUnavailable: false,
                isLanding: false);

            Assert.False(eligible);
        }

        [Fact]
        public void PlayerControlledAircraftIsNeverEligibleForAutoRtb()
        {
            bool eligible = AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false,
                sameHq: true,
                hasPlayer: true,
                isWingMemberOrLeader: false,
                isRecruitPending: false,
                isPurchased: false,
                isDeparting: false,
                isPilotUnavailable: false,
                isLanding: false);

            Assert.False(eligible);
        }

        [Fact]
        public void PurchasedAircraftIsNeverEligibleForAutoRtb()
        {
            bool eligible = AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false,
                sameHq: true,
                hasPlayer: false,
                isWingMemberOrLeader: false,
                isRecruitPending: false,
                isPurchased: true,
                isDeparting: false,
                isPilotUnavailable: false,
                isLanding: false);

            Assert.False(eligible);
        }

        [Fact]
        public void PendingRecruitIsNeverEligibleForAutoRtb()
        {
            bool eligible = AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false,
                sameHq: true,
                hasPlayer: false,
                isWingMemberOrLeader: false,
                isRecruitPending: true,
                isPurchased: false,
                isDeparting: false,
                isPilotUnavailable: false,
                isLanding: false);

            Assert.False(eligible);
        }

        [Fact]
        public void AlreadyDepartingOrLandingAircraftIsNeverEligibleForAutoRtb()
        {
            Assert.False(AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false, sameHq: true, hasPlayer: false, isWingMemberOrLeader: false,
                isRecruitPending: false, isPurchased: false, isDeparting: true,
                isPilotUnavailable: false, isLanding: false));

            Assert.False(AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false, sameHq: true, hasPlayer: false, isWingMemberOrLeader: false,
                isRecruitPending: false, isPurchased: false, isDeparting: false,
                isPilotUnavailable: false, isLanding: true));
        }

        [Fact]
        public void DisabledOrOtherHqOrDeadPilotIsNeverEligibleForAutoRtb()
        {
            Assert.False(AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: true, sameHq: true, hasPlayer: false, isWingMemberOrLeader: false,
                isRecruitPending: false, isPurchased: false, isDeparting: false,
                isPilotUnavailable: false, isLanding: false));

            Assert.False(AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false, sameHq: false, hasPlayer: false, isWingMemberOrLeader: false,
                isRecruitPending: false, isPurchased: false, isDeparting: false,
                isPilotUnavailable: false, isLanding: false));

            Assert.False(AutoRtbCandidatePolicy.IsCandidateEligible(
                disabled: false, sameHq: true, hasPlayer: false, isWingMemberOrLeader: false,
                isRecruitPending: false, isPurchased: false, isDeparting: false,
                isPilotUnavailable: true, isLanding: false));
        }
    }
}
