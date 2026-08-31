using System.Collections.Generic;

namespace WingCommand
{
    internal readonly struct WingDispatchResult
    {
        public readonly int Applied;
        public readonly int Skipped;
        public readonly int CoveredTargets;
        public readonly string Message;

        public bool Success => Applied > 0;

        public WingDispatchResult(int applied, int skipped, string message, int coveredTargets = 0)
        {
            Applied = applied;
            Skipped = skipped;
            Message = message;
            CoveredTargets = coveredTargets;
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
                if (member != null && member.IsCommandable) result.Add(member);
            }
            return result;
        }

        public WingDispatchResult Apply(WingDirective directive, bool wholeWing)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, 0,
                    wholeWing ? "No wingmen assigned" : "No wingmen selected");

            if (WingOrderCatalog.NeedsPoint(directive.Order) && !directive.HasPoint)
                return new WingDispatchResult(0, scope.Count, "Select a point on the map");

            int applied = 0;
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, directive.Order)) continue;
                member.Apply(directive);
                applied++;
            }

            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0, skipped,
                    WingOrderCatalog.UnavailableReason(directive.Order));

            string message = ScopePrefix(wholeWing, applied) + ": " +
                             WingOrderCatalog.Label(directive.Order);
            if (skipped > 0) message += " (" + skipped + " unable)";
            return new WingDispatchResult(applied, skipped, message);
        }

        public WingDispatchResult Attack(IReadOnlyList<Unit> targets, bool wholeWing,
                                         bool forceAll = false)
        {
            List<WingMember> scope = Scope(wholeWing);
            if (scope.Count == 0)
                return new WingDispatchResult(0, 0,
                    wholeWing ? "No wingmen assigned" : "No wingmen selected");
            if (targets == null || targets.Count == 0)
                return new WingDispatchResult(0, scope.Count, "No target selected");

            int applied = wing.AttackTargets(scope, targets, out int covered, forceAll);
            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0, skipped, "No valid target selected");

            string message = ScopePrefix(wholeWing, applied) + ": attack";
            if (targets.Count > 1) message += " " + covered + " target(s)";
            if (skipped > 0) message += " (" + skipped + " covering)";
            return new WingDispatchResult(applied, skipped, message, covered);
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
                return new WingDispatchResult(0, 0,
                    wholeWing ? "No wingmen assigned" : "No wingmen selected");

            Unit target = FirstLive(targets);
            if (target == null)
                return new WingDispatchResult(0, scope.Count, "No target selected");

            int applied = 0;
            foreach (WingMember member in scope)
            {
                if (!WingOrderCatalog.CanApply(member, WingOrder.FireForEffect)) continue;
                if (!WingWeapons.CanStillEngage(member.Aircraft, target)) continue;
                member.FireForEffect(target);
                applied++;
            }

            int skipped = scope.Count - applied;
            if (applied == 0)
                return new WingDispatchResult(0, skipped,
                    WingOrderCatalog.UnavailableReason(WingOrder.FireForEffect));

            string message = ScopePrefix(wholeWing, applied) + ": fire for effect on " +
                             target.unitName;
            if (skipped > 0) message += " (" + skipped + " unable)";
            return new WingDispatchResult(applied, skipped, message, 1);
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

        private static string ScopePrefix(bool wholeWing, int applied) =>
            wholeWing ? "Wing" : applied + " selected";
    }
}
