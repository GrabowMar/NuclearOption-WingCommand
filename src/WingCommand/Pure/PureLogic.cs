using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// What a wingman has been told to do. Movement/task authority is deliberately separate
    /// from the standing rules of engagement below.
    /// </summary>
    internal enum WingOrder
    {
        Formation,
        Engage,
        ReturnToBase,
        FallBack,
        OrbitHere,
        DeliverCargo,
        LandHere,
        Attack,
        FireForEffect,
        MoveToPoint,

        /// <summary>Hold the formation slot, but run the radar jammer against a designated unit.</summary>
        JamTarget,

        /// <summary>Fly one scripted manoeuvre, then rejoin. Transient: never a resting state.</summary>
        Maneuver,
    }

    /// <summary>The scripted manoeuvres a wingman can be told to fly on command.</summary>
    internal enum ManeuverKind
    {
        BreakLeft,
        BreakRight,
        SplitS,
        Immelmann,
        BarrelRoll,
        AileronRoll,
        Loop,
        WingWaggle,
    }

    /// <summary>Standing weapons policy for the wing.</summary>
    internal enum WingRoe
    {
        Hold,
        Escort,
        Free,
    }

    internal enum OrderEngagementAuthority
    {
        /// <summary>The standing ROE may select and fire on incidental targets.</summary>
        StandingRoe,

        /// <summary>The player-designated target and order own weapons employment.</summary>
        ExplicitTarget,

        /// <summary>The task allows self-preservation only, not opportunity fire.</summary>
        DefensiveOnly,

        /// <summary>The order hands flying and fighting to the combat AI.</summary>
        AutonomousCombat,
    }

    /// <summary>Pure precedence table shared by runtime code and tests.</summary>
    internal static class OrderRoePolicy
    {
        public static OrderEngagementAuthority Authority(WingOrder order)
        {
            switch (order)
            {
                case WingOrder.Formation:
                case WingOrder.OrbitHere:
                    return OrderEngagementAuthority.StandingRoe;

                case WingOrder.Attack:
                case WingOrder.FireForEffect:
                    return OrderEngagementAuthority.ExplicitTarget;

                case WingOrder.Engage:
                    return OrderEngagementAuthority.AutonomousCombat;

                case WingOrder.JamTarget:
                case WingOrder.Maneuver:
                    // A jamming wingman holds station and defends itself only; a wingman
                    // mid-manoeuvre has no business picking a fight it cannot fly.
                    return OrderEngagementAuthority.DefensiveOnly;

                default:
                    return OrderEngagementAuthority.DefensiveOnly;
            }
        }
    }

    /// <summary>Pure rotary hover transition, separated from Unity steering for tests.</summary>
    internal static class RotaryHoverPolicy
    {
        public static bool ShouldHover(bool wasHovering, float leaderHorizontalSpeed,
                                       float horizontalSlotError, float spacing,
                                       float hoverSpeed, float hysteresis,
                                       float stationSpacings)
        {
            float threshold = wasHovering ? hoverSpeed + hysteresis
                                          : hoverSpeed - hysteresis;
            bool onStation = horizontalSlotError < spacing * stationSpacings;
            return onStation && leaderHorizontalSpeed < threshold;
        }
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
            var failed = new List<Action>();
            for (int i = compensations.Count - 1; i >= 0; i--)
            {
                try { compensations[i](); }
                catch (Exception e)
                {
                    failed.Add(compensations[i]);
                    onError?.Invoke(e);
                }
            }
            compensations.Clear();
            if (failed.Count == 0)
            {
                closed = true;
                return true;
            }

            failed.Reverse();
            compensations.AddRange(failed);
            return false;
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

    /// <summary>A timeout whose clock restarts whenever a decreasing quantity progresses.</summary>
    internal sealed class CargoProgressTracker
    {
        public int LastAmount { get; private set; }
        public float LastProgressAt { get; private set; }
        public bool MadeProgress { get; private set; }

        public void Reset(int amount, float now)
        {
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = false;
        }

        public bool Observe(int amount, float now)
        {
            if (amount >= LastAmount) return false;
            LastAmount = amount;
            LastProgressAt = now;
            MadeProgress = true;
            return true;
        }

        public bool IsStalled(float now, float timeout) => now - LastProgressAt >= timeout;
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
    }
}
