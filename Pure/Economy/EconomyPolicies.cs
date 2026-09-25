using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>RTB despawn/park and allocation-refund policy; recruitment fees are never refunded as
    /// purchases.</summary>
    internal static class RecoverySettlementPolicy
    {
        /// <summary>Use native Returned despawn when enabled.</summary>
        public static bool ShouldDespawn(bool rtbReturnsToReserve) => rtbReturnsToReserve;

        /// <summary>Refund only allocation actually paid for an owned purchase.</summary>
        public static bool ShouldRefund(bool purchased, float paid) => purchased && paid > 0f;

        /// <summary>Native spawning replaces missing/empty weapon lists with loadouts[1]; deliberate empty
        /// fits need one entry per hardpoint.</summary>
        public static bool NativeLoadoutReplaces(int weaponCount) => weaponCount <= 0;
    }

    /// <summary>Exclude non-squadron encyclopedia placeholders such as the April Fools UFO from Supply and
    /// Loadout.</summary>
    internal static class AirframeCatalogPolicy
    {
        public static bool IsHiddenFromPanels(string unitName, string code = null, string jsonKey = null) =>
            IsQuestionMarkPlaceholder(unitName)
            || IsQuestionMarkPlaceholder(code)
            || IsQuestionMarkPlaceholder(jsonKey)
            || MatchesKey(jsonKey, "UFO");

        public static bool IsQuestionMarkPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] != '?') return false;
            return true;
        }

        private static bool MatchesKey(string value, string key) =>
            !string.IsNullOrEmpty(value)
            && string.Equals(value, key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Guards auto-RTB candidate selection so wing members, player aircraft, and pending deliveries
    /// are never sacrificed to make room for new requisitions.</summary>
    internal static class AutoRtbCandidatePolicy
    {
        public static bool IsCandidateEligible(bool disabled,
                                               bool sameHq,
                                               bool hasPlayer,
                                               bool isWingMemberOrLeader,
                                               bool isRecruitPending,
                                               bool isPurchased,
                                               bool isDeparting,
                                               bool isPilotUnavailable,
                                               bool isLanding)
        {
            if (disabled) return false;
            if (!sameHq) return false;
            if (hasPlayer) return false;
            if (isWingMemberOrLeader) return false;
            if (isRecruitPending) return false;
            if (isPurchased) return false;
            if (isDeparting) return false;
            if (isPilotUnavailable) return false;
            if (isLanding) return false;
            return true;
        }
    }
}
