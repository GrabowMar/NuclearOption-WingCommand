using System.Collections.Generic;

namespace WingCommand
{
    internal readonly struct WingDispatchResult
    {
        public readonly int Applied;
        public readonly string Message;
        public readonly IReadOnlyList<WingMember> Responders;
        public readonly WingOrder Order;

        public bool Success => Applied > 0;

        public WingDispatchResult(int applied, string message,
                                  IReadOnlyList<WingMember> responders = null,
                                  WingOrder order = WingOrder.Formation)
        {
            Applied = applied;
            Message = message;
            Responders = responders ?? System.Array.Empty<WingMember>();
            Order = order;
        }
    }

    /// <summary>
    /// Shared command entry point for WMC, map, radial and hotkeys. Interfaces decide the
    /// scope; this class validates and applies it consistently.
    /// </summary>
    internal sealed class WingDirectiveDispatcher
    {
        private readonly WingRegistry wing;
        private readonly WingCommandSelection selection;

        public WingDirectiveDispatcher(WingRegistry wing, WingCommandSelection selection)
        {
            this.wing = wing;
            this.selection = selection;
        }

        public List<WingMember> Scope(bool wholeWing)
        {
            if (!wholeWing) return selection.Snapshot(wing);

            var result = new List<WingMember>();
            if (wing == null) return result;
            foreach (WingMember member in wing.Members)
            {
                if (member != null && member.Alive) result.Add(member);
            }
            return result;
        }

        public WingDispatchResult Apply(WingDirective directive, bool wholeWing)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, EmptyScopeMessage(wholeWing));

            if (WingOrderCatalog.NeedsPoint(directive.Order) && !directive.HasPoint)
                return new WingDispatchResult(0, "Select a point on the map");

            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, directive.Order)) continue;
                member.Apply(directive);
                responders.Add(member);
            }

            int applied = responders.Count;
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0,
                    WingOrderCatalog.UnavailableReason(directive.Order));

            string message = ScopePrefix(wholeWing, applied) + ": " +
                             WingOrderCatalog.Label(directive.Order);
            if (skipped > 0) message += " (" + skipped + " unable)";
            return new WingDispatchResult(applied, WithQueued(message, responders), responders, directive.Order);
        }

        public WingDispatchResult Attack(IReadOnlyList<Unit> targets, bool wholeWing,
                                         bool forceAll = false)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, EmptyScopeMessage(wholeWing));
            if (targets == null || targets.Count == 0)
                return new WingDispatchResult(0, "No target selected");

            var responders = new List<WingMember>();
            int applied = wing.AttackTargets(scope, targets, out int covered, forceAll, responders);
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0, "No valid target selected");

            string message = ScopePrefix(wholeWing, applied) + ": attack";
            if (targets.Count > 1) message += " " + covered + " target(s)";
            if (skipped > 0) message += " (" + skipped + " covering)";
            return new WingDispatchResult(applied, WithQueued(message, responders), responders, WingOrder.Attack);
        }

        /// <summary>
        /// Put every wingman in scope onto one designation, expending.
        ///
        /// No distribution and no useful-attacker cap, unlike <see cref="Attack"/>. Those
        /// exist to stop a wing wasting itself on a target that needed one missile; an
        /// order whose entire meaning is "everyone, everything, that one" is the case they
        /// were never meant to govern.
        /// </summary>
        public WingDispatchResult FireForEffect(IReadOnlyList<Unit> targets, bool wholeWing)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, EmptyScopeMessage(wholeWing));

            Unit target = FirstLive(targets);
            if (target == null)
                return new WingDispatchResult(0, "No target selected");

            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, WingOrder.FireForEffect)) continue;
                if (!member.DeliveryPending &&
                    !WingWeapons.CanStillEngage(member.Aircraft, target)) continue;
                member.FireForEffect(target, report: false);
                responders.Add(member);
            }

            int applied = responders.Count;
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0,
                    WingOrderCatalog.UnavailableReason(WingOrder.FireForEffect));

            string message = ScopePrefix(wholeWing, applied) + ": splash 'em on " +
                             target.unitName;
            if (skipped > 0) message += " (" + skipped + " unable)";
            return new WingDispatchResult(applied, WithQueued(message, responders), responders, WingOrder.FireForEffect);
        }

        /// <summary>
        /// Put every jam-capable wingman in scope onto one designation: hold the slot,
        /// run the jammer pod against that unit until it dies or the order is replaced.
        /// </summary>
        public WingDispatchResult JamTarget(IReadOnlyList<Unit> targets, bool wholeWing)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, EmptyScopeMessage(wholeWing));

            Unit target = FirstLive(targets);
            if (target == null)
                return new WingDispatchResult(0, "No target selected");

            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, WingOrder.JamTarget)) continue;
                member.Apply(WingDirective.AtTarget(WingOrder.JamTarget, target));
                responders.Add(member);
            }

            int applied = responders.Count;
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0,
                    WingOrderCatalog.UnavailableReason(WingOrder.JamTarget));

            string message = ScopePrefix(wholeWing, applied) + ": jamming " + target.unitName;
            if (skipped > 0) message += " (" + skipped + " without a jammer pod)";
            return new WingDispatchResult(applied, WithQueued(message, responders), responders, WingOrder.JamTarget);
        }

        /// <summary>Send the scope through one scripted manoeuvre. Transient; it rejoins after.</summary>
        public WingDispatchResult Maneuver(ManeuverKind kind, bool wholeWing)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, EmptyScopeMessage(wholeWing));

            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, WingOrder.Maneuver)) continue;
                member.Apply(WingDirective.RunManeuver(kind));
                responders.Add(member);
            }

            int applied = responders.Count;
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0,
                    WingOrderCatalog.UnavailableReason(WingOrder.Maneuver));

            string message = ScopePrefix(wholeWing, applied) + ": " + ManeuverCatalog.Label(kind);
            return new WingDispatchResult(applied, message, responders, WingOrder.Maneuver);
        }

        private static Unit FirstLive(IReadOnlyList<Unit> targets)
        {
            if (targets == null) return null;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && !targets[i].disabled) return targets[i];
            }
            return null;
        }

        private string EmptyScopeMessage(bool wholeWing)
        {
            if (wing == null || wing.Count == 0)
                return wholeWing ? "No wingmen assigned" : "No wingmen. Requisition on SUPPLY.";
            if (!wholeWing && selection != null && selection.IsNone)
                return "No wingmen selected";
            return wholeWing ? "No wingmen assigned" : "No wingmen selected";
        }

        private static int CountQueued(List<WingMember> responders)
        {
            int queued = 0;
            if (responders == null) return 0;
            for (int i = 0; i < responders.Count; i++)
                if (responders[i] != null && responders[i].DeliveryPending) queued++;
            return queued;
        }

        private static string WithQueued(string message, List<WingMember> responders)
        {
            int queued = CountQueued(responders);
            if (queued > 0) message += " (" + queued + " queued until airborne)";
            return message;
        }

        private static string ScopePrefix(bool wholeWing, int applied) =>
            wholeWing ? "Wing" : applied + " selected";
    }
}
