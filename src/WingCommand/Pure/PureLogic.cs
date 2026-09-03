using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// What a wingman has been told to do. Movement/task authority is deliberately separate
    /// from the standing rules of engagement below.
    ///
    /// Public, unlike almost everything else here, because <see cref="WingSituation"/> hands
    /// it to third-party reflexes — a reflex that recalls a wingman only when it is actually
    /// hunting has to be able to ask which order is standing.
    /// </summary>
    public enum WingOrder
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

        /// <summary>Hold the formation slot, but run the jammer pod against a designated unit.</summary>
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
        NotchThreat,
    }

    /// <summary>
    /// Standing weapons policy for the wing. Public for the same reason as
    /// <see cref="WingOrder"/>: it is part of the situation a reflex scores against.
    /// </summary>
    public enum WingRoe
    {
        Hold,
        Tight,
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

    /// <summary>
    /// Facts about an order that more than one system needs to agree on.
    ///
    /// <see cref="SendsWingmanHunting"/> lived in two places that disagreed: the leash
    /// reflex leashed Engage and Attack, while <c>WingOrderCatalog.IsTargetOrder</c> — whose
    /// doc still described the leash it no longer drove — answered Attack alone. Neither was
    /// obviously wrong from where it sat, which is the usual way two definitions of one idea
    /// survive next to each other.
    /// </summary>
    internal static class WingOrderRules
    {
        /// <summary>
        /// True when the order sends a wingman away from the formation to prosecute
        /// something, and therefore needs a tether.
        ///
        /// Jam Target and Splash 'Em are excluded deliberately: both carry a designated
        /// target, but both are flown from the slot, so neither can overshoot a leash in the
        /// first place.
        /// </summary>
        public static bool SendsWingmanHunting(WingOrder order) =>
            order == WingOrder.Engage || order == WingOrder.Attack;

        /// <summary>
        /// True when the directive holds a designated unit at all, weapons or not.
        ///
        /// Deliberately next to <see cref="SendsWingmanHunting"/>, because the two are easy
        /// to mistake for one another and the difference matters: Splash 'Em and Jam Target
        /// both carry a target and are answered yes here, but are flown from the slot and
        /// are answered no there.
        /// </summary>
        public static bool CarriesTarget(WingOrder order) =>
            order == WingOrder.Attack ||
            order == WingOrder.FireForEffect ||
            order == WingOrder.JamTarget;

        /// <summary>
        /// Standing orders a hangar delivery may accept while it is still taxiing.
        ///
        /// The airframe is already on the roster, but the stock launch AI owns it until it
        /// is airborne. Recording the order here is what lets ActivateWhenAirborne fly what
        /// the player asked for instead of defaulting to Form Up. Manoeuvres are excluded
        /// because they are transient: they would be spent on the apron and never flown.
        /// </summary>
        public static bool CanQueueWhilePending(WingOrder order) =>
            order != WingOrder.Maneuver;
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

        /// <summary>
        /// Weapons authority for what a wingman is <i>actually doing</i>, which is not always
        /// what it was told to do.
        ///
        /// This is the fix for the oldest conflict in the system. A wingman recalled from
        /// past its leash flies formation while its standing order still reads Engage, and
        /// asking the order alone therefore granted it autonomous-combat authority — weapons
        /// free, air and ground, at explicit-order range — from inside the formation slot,
        /// with the standing rules of engagement bypassed completely. A behaviour that is
        /// not the standing task answers for itself.
        /// </summary>
        public static OrderEngagementAuthority AuthorityFor(string behaviourId, WingOrder order)
        {
            switch (behaviourId)
            {
                // Rejoining and holding overhead are both station-keeping, whatever the
                // order underneath them says. The standing ROE governs, as it does for any
                // other wingman flying its slot.
                case WingBehaviours.Rejoin:
                case WingBehaviours.DeckHold:
                    return OrderEngagementAuthority.StandingRoe;

                // Running from a missile, or not ours to fly at all.
                case WingBehaviours.MissileBreak:
                case WingBehaviours.Held:
                    return OrderEngagementAuthority.DefensiveOnly;

                default:
                    return Authority(order);
            }
        }
    }

    /// <summary>
    /// Which aircraft a wingman formates on, given an optional designated flight lead.
    ///
    /// The rule is small on purpose and lives here so it can be tested without Unity: a
    /// follower forms on the designated lead; the lead itself, and every member when no
    /// lead is set, falls through to the wing leader (the player).
    /// </summary>
    internal static class FlightLeadPolicy
    {
        public static T FormationLeader<T>(bool isThisMemberTheLead, T designatedLead,
                                           T wingLeader) where T : class =>
            (isThisMemberTheLead || designatedLead == null) ? wingLeader : designatedLead;
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

    /// <summary>
    /// Where a requisitioned airframe should come from, and what the roster calls that wait.
    ///
    /// A farther field that happens to have an idle hangar this frame is not a better
    /// answer than the nearest one that can produce the airframe. The order queues there
    /// until a hangar or helipad frees up, rather than materialising in the circuit or
    /// defecting across the map.
    /// </summary>
    internal static class HangarFieldPolicy
    {
        public static int SelectNearestStocked(int count, Func<int, float> distanceSq,
                                               Func<int, bool> stocks)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!stocks(i)) continue;
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

    /// <summary>
    /// When a wingman must climb before it is allowed to chase a slot.
    ///
    /// Formation is a station-keeping law. Handing it an aircraft at 25-150 m AGL with a
    /// kilometres-long slot error produces a 100° intercept at treetop height — the live
    /// log's 131° command at 25 m, then pilot-killed at 1-8 m. Climb-out is the missing
    /// state between "stock launch AI is done" and "fly the slot": wings level, climb,
    /// then rejoin. Nap-of-earth station keeping (low and already close) is left alone.
    /// </summary>
    internal static class ClimbOutPolicy
    {
        /// <summary>Grab climb-out below this AGL when far from the leader.</summary>
        public const float GrabAlt = 300f;

        /// <summary>Keep climbing until here, so the behaviour does not flap on the floor.</summary>
        public const float ReleaseAlt = 450f;

        /// <summary>Metres from the leader that count as "far" — a rejoin, not a slot.</summary>
        public const float GrabRange = 400f;

        /// <summary>Release the range check inside this, so a closing climb-out can finish.</summary>
        public const float ReleaseRange = 200f;

        /// <summary>Survival pull-up: below this, even a missile break is the worse turn.</summary>
        public const float AbortAlt = 50f;

        /// <summary>Hold the abort until here so a bounce off the floor does not resume the intercept.</summary>
        public const float AbortReleaseAlt = 90f;

        public static bool ShouldClimbOut(
            float radarAlt, float leaderDistance, WingOrder order,
            bool incumbent, bool deliveryPending, bool leaderPresent)
        {
            if (deliveryPending || !leaderPresent) return false;
            if (order != WingOrder.Formation) return false;

            float alt = incumbent ? ReleaseAlt : GrabAlt;
            float range = incumbent ? ReleaseRange : GrabRange;
            return radarAlt < alt && leaderDistance > range;
        }

        public static bool ShouldAbort(
            float radarAlt, float leaderDistance, WingOrder order,
            bool incumbent, bool deliveryPending)
        {
            if (deliveryPending) return false;
            if (!AllowsAbort(order)) return false;

            float alt = incumbent ? AbortReleaseAlt : AbortAlt;
            float range = incumbent ? ReleaseRange : GrabRange;
            return radarAlt < alt && leaderDistance > range;
        }

        /// <summary>
        /// Orders that are supposed to be low. A pull-up here would fight the task.
        /// </summary>
        public static bool AllowsAbort(WingOrder order) =>
            order != WingOrder.LandHere &&
            order != WingOrder.ReturnToBase &&
            order != WingOrder.Attack &&
            order != WingOrder.FireForEffect &&
            order != WingOrder.DeliverCargo;
    }
}
