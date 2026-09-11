using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Retries transaction compensation in reverse order without repeating successful
    /// actions.</summary>
    internal sealed class RollbackJournal
    {
        private readonly List<Action> compensations = new List<Action>();
        private bool closed;

        public void Add(Action compensation)
        {
            if (closed || compensation == null) return;
            compensations.Add(compensation);
        }

        public void Commit()
        {
            if (closed) return;
            compensations.Clear();
            closed = true;
        }

        public bool Rollback(Action<Exception> onError = null)
        {
            if (closed) return true;
            for (int i = compensations.Count - 1; i >= 0; i--)
            {
                try
                {
                    compensations[i]();
                    compensations.RemoveAt(i);
                }
                catch (Exception e)
                {
                    onError?.Invoke(e);
                }
            }
            // Retain failed actions in order; discard successful actions before retry.
            closed = compensations.Count == 0;
            return closed;
        }
    }

    /// <summary>Tracks pending delivery capacity separately from live aircraft.</summary>
    internal sealed class CapacityReservations
    {
        public int Wing { get; private set; }
        public int Squadron { get; private set; }
        public int OverLimit { get; private set; }

        public void Reserve(bool overLimit)
        {
            Wing++;
            Squadron++;
            if (overLimit) OverLimit++;
        }

        public void Release(bool overLimit)
        {
            Wing = Math.Max(0, Wing - 1);
            Squadron = Math.Max(0, Squadron - 1);
            if (overLimit) OverLimit = Math.Max(0, OverLimit - 1);
        }

        public void Reset()
        {
            Wing = 0;
            Squadron = 0;
            OverLimit = 0;
        }
    }

    /// <summary>Selects concrete reserve slots while preserving fit and ownership identity.</summary>
    internal static class ReserveSlotPolicy
    {
        public static int SelectForPurchase(int count, Func<int, bool> matchesDefinition,
                                            Func<int, bool> isOwned,
                                            Func<int, bool> isReserved)
        {
            int owned = Find(count, matchesDefinition, isOwned, null, isReserved);
            return owned >= 0
                ? owned
                : Find(count, matchesDefinition, i => !isOwned(i), null, isReserved);
        }

        public static int SelectForRelease(int count, Func<int, bool> matchesDefinition,
                                           Func<int, bool> isOwned,
                                           Func<int, bool> hasLoadout,
                                           Func<int, bool> isReserved)
        {
            int heldWithoutFit = Find(
                count, matchesDefinition, i => !isOwned(i), i => !hasLoadout(i), isReserved);
            if (heldWithoutFit >= 0) return heldWithoutFit;

            int held = Find(count, matchesDefinition, i => !isOwned(i), null, isReserved);
            return held >= 0
                ? held
                : Find(count, matchesDefinition, isOwned, null, isReserved);
        }

        private static int Find(int count, Func<int, bool> matchesDefinition,
                                Func<int, bool> ownership, Func<int, bool> loadout,
                                Func<int, bool> isReserved)
        {
            for (int i = 0; i < count; i++)
            {
                if (!matchesDefinition(i) || isReserved(i) || !ownership(i)) continue;
                if (loadout != null && !loadout(i)) continue;
                return i;
            }
            return -1;
        }

        /// <summary>Apply capacity to manual faction holds while exempting paid aircraft. RTB no longer
        /// auto-stores through this policy.</summary>
        public static bool CanStoreAirframe(bool owned, int currentCount, int factionStockCapacity) =>
            owned || currentCount < factionStockCapacity;
    }

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

    /// <summary>Launch-field routing preference among enabled fields.</summary>
    internal enum HangarLaunchMode
    {
        /// <summary>Queue at the nearest compatible enabled field, even when a farther field is
        /// idle.</summary>
        OnlyNearest,

        /// <summary>Choose the nearest currently available field; wait unpinned if all are busy.</summary>
        Any,
    }

    /// <summary>Select nearest-compatible pinning or nearest-currently-free routing according to launch
    /// mode.</summary>
    internal static class HangarFieldPolicy
    {
        /// <summary>Permit refund only when neither an observed aircraft nor an unfinished accepted native
        /// sequence owns the purchase.</summary>
        internal static bool CanRefundDelivery(bool nativeAccepted, bool nativeSequenceFinished,
                                               bool hangarDestroyed, bool aircraftObserved) =>
            !aircraftObserved && (!nativeAccepted || nativeSequenceFinished || hangarDestroyed);

        public static int SelectOrigin(
            int count,
            HangarLaunchMode mode,
            Func<int, float> distanceSq,
            Func<int, bool> allowed,
            Func<int, bool> stocks,
            Func<int, bool> readyNow)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!allowed(i) || !stocks(i)) continue;
                if (mode == HangarLaunchMode.Any && !readyNow(i)) continue;
                float sq = distanceSq(i);
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = i;
            }
            return best;
        }

        /// <summary>Display QUE before field acceptance and DEPT afterward.</summary>
        public static string StatusCode(bool hangarClaimed) => hangarClaimed ? "DEPT" : "QUE";
    }

    public enum LaunchBaseStatus
    {
        None,
        Ready,
        Blocked,
        NoPad
    }

    /// <summary>Supply-row capability, badge, and tooltip policy for the selected airframe and
    /// base.</summary>
    internal static class LaunchBaseStatusPolicy
    {
        public static LaunchBaseStatus Evaluate(bool allowed, bool canProduce, bool hasAirframeSelection)
        {
            if (!hasAirframeSelection)
                return allowed ? LaunchBaseStatus.None : LaunchBaseStatus.Blocked;

            if (!canProduce)
                return LaunchBaseStatus.NoPad;

            return allowed ? LaunchBaseStatus.Ready : LaunchBaseStatus.Blocked;
        }

        public static string BadgeText(LaunchBaseStatus status)
        {
            switch (status)
            {
                case LaunchBaseStatus.Ready: return "READY";
                case LaunchBaseStatus.NoPad: return "NO PAD";
                case LaunchBaseStatus.Blocked: return "BLOCKED";
                default: return "";
            }
        }

        public static string Tooltip(string baseName, string airframeName, bool allowed, bool canProduce)
        {
            if (string.IsNullOrEmpty(airframeName))
                return baseName + (allowed ? " — launches allowed" : " — launches blocked");

            if (!canProduce)
                return baseName + (allowed ? " [CHECKED]" : "") +
                       " — Cannot launch " + airframeName + " (no compatible hangar or helipad)";

            return baseName + (allowed
                ? " — Can launch " + airframeName + " [ALLOWED]"
                : " — Can launch " + airframeName + " [BLOCKED - click to allow]");
        }
    }

    /// <summary>Matches native hangar availableAircraft lists and labels pads with the building
    /// UnitDefinition.code.</summary>
    internal static class HangarStockPolicy
    {
        public static string PadLabel(string code)
        {
            if (string.IsNullOrEmpty(code)) return "pad";
            switch (code)
            {
                case "HPAD": return "helipad";
                case "REV": return "revetment";
                case "HGR-M": return "hangar";
                case "HGR-H": return "shelter";
                case "SHP": return "ship";
                default: return code;
            }
        }

        public static bool SameAirframe(string jsonKey, string unitName,
                                        string otherKey, string otherName)
        {
            if (!string.IsNullOrEmpty(jsonKey) && jsonKey == otherKey) return true;
            return !string.IsNullOrEmpty(unitName) && unitName == otherName;
        }

        public static string FormatPadStock(string padLabel, IList<string> aircraftCodes)
        {
            if (string.IsNullOrEmpty(padLabel)) padLabel = "pad";
            if (aircraftCodes == null || aircraftCodes.Count == 0) return padLabel + ": —";
            return padLabel + ": " + JoinLimited(aircraftCodes, 8);
        }

        public static string FormatFieldStock(IList<string> padSummaries)
        {
            if (padSummaries == null || padSummaries.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < padSummaries.Count; i++)
            {
                if (string.IsNullOrEmpty(padSummaries[i])) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(padSummaries[i]);
            }
            return sb.ToString();
        }

        public static string FormatLaunchSites(IList<string> fieldSummaries)
        {
            if (fieldSummaries == null || fieldSummaries.Count == 0)
                return "No hangar or helipad lists this airframe";
            return "Launch: " + JoinLimited(fieldSummaries, 6);
        }

        public static string JoinLimited(IList<string> values, int max)
        {
            if (values == null || values.Count == 0) return "";
            if (max < 1) max = 1;
            var sb = new System.Text.StringBuilder();
            int shown = Math.Min(values.Count, max);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(values[i]);
            }
            int extra = values.Count - shown;
            if (extra > 0) sb.Append(" +").Append(extra);
            return sb.ToString();
        }
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
}
