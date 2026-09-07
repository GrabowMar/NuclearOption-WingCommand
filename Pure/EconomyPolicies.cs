using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Idempotent reverse-order compensation for a transaction's completed effects.</summary>
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
            // Failed actions stay in order for retry; successful ones never run twice.
            closed = compensations.Count == 0;
            return closed;
        }
    }

    /// <summary>Outstanding delivery capacity counted independently of live aircraft.</summary>
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

    /// <summary>Priority rules for selecting one concrete reserve slot without parallel FIFOs.</summary>
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

        /// <summary>
        /// Whether an airframe being recovered or stored can enter the wing reserve.
        /// Owned airframes are exempt from faction hold capacity because the player paid for them.
        /// </summary>
        public static bool CanStoreAirframe(bool owned, int currentCount, int factionStockCapacity) =>
            owned || currentCount < factionStockCapacity;
    }

    /// <summary>
    /// How a requisition picks a field among the ones the player has allowed.
    /// </summary>
    internal enum HangarLaunchMode
    {
        /// <summary>
        /// Pin to the closest allowed field that can ever produce the airframe, and wait
        /// there even if every pad is busy. A farther idle field is not a better answer.
        /// </summary>
        OnlyNearest,

        /// <summary>
        /// Do not pin. Take the closest allowed field that can launch right now. If none
        /// can, wait unpinned until one can, rather than queueing at a busy nearest.
        /// </summary>
        Any,
    }

    /// <summary>
    /// OnlyNearest waits at the closest compatible allowed field, even when busy.
    /// Any selects the closest field that can launch now and otherwise stays unpinned.
    /// </summary>
    internal static class HangarFieldPolicy
    {
        /// <summary>Refund only when no observed aircraft or unfinished native launch owns the order.</summary>
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

        /// <summary>QUE while waiting for a pad; DEPT once a hangar has taken the order.</summary>
        public static string StatusCode(bool hangarClaimed) => hangarClaimed ? "DEPT" : "QUE";
    }

    public enum LaunchBaseStatus
    {
        None,
        Ready,
        Blocked,
        NoPad
    }

    /// <summary>
    /// Pure presentation and evaluation policy for launch base rows in the supply panel.
    /// Determines whether a base can support the selected aircraft and what badge/tooltip to show.
    /// </summary>
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

    /// <summary>
    /// Labels and matching for the hangar prefab stock lists the game serializes as
    /// <c>Hangar.availableAircraft</c>. Pad codes are the building <c>UnitDefinition.code</c>
    /// values the native airbase info panel already uses.
    /// </summary>
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

    /// <summary>
    /// Encyclopedia entries that must not appear on the Supply or Loadout panels.
    /// The April Fools UFO is named "???" (dev key "UFO") and is not a squadron airframe.
    /// </summary>
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
