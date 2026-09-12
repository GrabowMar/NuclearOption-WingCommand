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

    /// <summary>Validates and applies commands consistently across WMC, map, radial, and hotkeys; callers
    /// choose scope.</summary>
    internal sealed class WingDirectiveDispatcher
    {
        private readonly WingRegistry wing;
        private readonly WingCommandSelection selection;

        public WingDirectiveDispatcher(WingRegistry wing, WingCommandSelection selection)
        {
            this.wing = wing;
            this.selection = selection;
        }

        public List<WingMember> Scope(bool wholeWing) => selection.Snapshot(wing, wholeWing);

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

            if (directive.Order == WingOrder.Engage)
                CombatFacade.Roe.EnsureFree(wing);

            string label = directive.Order == WingOrder.Maneuver
                ? ManeuverCatalog.Label(directive.Maneuver)
                : WingOrderCatalog.Label(directive.Order);
            string message = ScopePrefix(wholeWing, applied) + ": " + label;
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

        /// <summary>Assign the entire selection to each member, bypassing Attack's distribution and
        /// useful-attacker cap.</summary>
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
                Unit first = null;
                foreach (Unit candidate in targets)
                    if (candidate != null && !candidate.disabled && (member.DeliveryPending ||
                        CombatFacade.Weapons.CanStillEngage(member.Aircraft, candidate)))
                    {
                        first = candidate;
                        break;
                    }
                if (first == null) continue;
                member.Apply(WingDirective.Splash(targets, first));
                responders.Add(member);
            }

            int applied = responders.Count;
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0,
                    WingOrderCatalog.UnavailableReason(WingOrder.FireForEffect));

            string message = ScopePrefix(wholeWing, applied) + ": splash 'em on " +
                             (targets.Count > 1 ? targets.Count + " selected targets" : target.unitName);
            if (skipped > 0) message += " (" + skipped + " unable)";
            return new WingDispatchResult(applied, WithQueued(message, responders), responders, WingOrder.FireForEffect);
        }

        /// <summary>Assign jam-capable members to hold station and jam one target until destroyed or
        /// superseded.</summary>
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

        /// <summary>Run one manoeuvre across the scope, then rejoin.</summary>
        public WingDispatchResult Maneuver(ManeuverKind kind, bool wholeWing) =>
            Apply(WingDirective.RunManeuver(kind), wholeWing);

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
