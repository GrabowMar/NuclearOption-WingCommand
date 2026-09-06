using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Taxiway corner, braking and recovery rules for a native departure route.</summary>
    internal static class TaxiRoutePolicy
    {
        internal const float WaypointRadius = 3f;
        internal const float MaximumSpeed = 12f;

        // Heavy aircraft need room for their longer wheelbase, but even their
        // corridor stays below the stock 20 m waypoint skip threshold.
        internal static float CornerRadius(float aircraftRadius) =>
            Math.Max(WaypointRadius, Math.Min(10f, Math.Max(0f, aircraftRadius) * 0.3f));

        // Horizontal offsets: aircraft -> waypoint, then waypoint -> next. Heading
        // alone is never permission to skip a corner on another taxiway.
        internal static bool CanAdvance(float toX, float toZ, float nextX, float nextZ,
            float radius = WaypointRadius)
        {
            if (toX * toX + toZ * toZ <= radius * radius) return true;
            float lengthSq = nextX * nextX + nextZ * nextZ;
            if (lengthSq < 0.01f) return false;
            float along = -toX * nextX - toZ * nextZ;
            float cross = toX * nextZ - toZ * nextX;
            return along >= 0f && cross * cross <= radius * radius * lengthSq;
        }

        internal static float SpeedLimit(float distance, float cornerAngle, float headingError,
            float radius = WaypointRadius)
        {
            float cornerSpeed = Math.Max(3.5f, MaximumSpeed / (1f + Math.Abs(cornerAngle) * 0.045f));
            // Begin braking before the corner at 1.5 m/s² rather than turning at the
            // stock 20-30 m/s taxi speed, especially hazardous for heavy airframes.
            float approach = (float)Math.Sqrt(cornerSpeed * cornerSpeed +
                3f * Math.Max(0f, distance - radius * 2f));
            float alignment = MaximumSpeed / (1f + Math.Abs(headingError) * 0.045f);
            return Math.Max(3.5f, Math.Min(MaximumSpeed, Math.Min(approach, alignment)));
        }

        internal static bool ShouldRebuild(bool waiting, bool hasRoute, bool offNetwork,
            float stoppedSeconds, float movingSeconds, float sinceRebuild,
            float destinationDistance = float.PositiveInfinity) =>
            !waiting && movingSeconds >= 12f && sinceRebuild >= 5f &&
            destinationDistance > 20f &&
            (!hasRoute || offNetwork || stoppedSeconds >= 8f);
    }

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
}
