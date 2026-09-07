using System.Collections.Generic;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        internal void Execute(WingAction action) => Execute(action, wholeWing: true);

        /// <summary>
        /// Run an interface action. Radial/hotkey callers use the whole wing; WMC/map
        /// callers explicitly pass <paramref name="wholeWing"/> as false.
        /// </summary>
        internal void Execute(WingAction action, bool wholeWing)
        {
            // Immediate WMC/radial orders are not map tools. Drop any armed map order so
            // the next right-click is a move rather than the leftover Hold or Attack.
            CancelMapOrder(notify: false);

            switch (action)
            {
                case WingAction.Rejoin:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Formation), wholeWing));
                    break;

                case WingAction.Engage:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Engage), wholeWing));
                    break;

                case WingAction.StandDown:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.StandDown), wholeWing));
                    break;

                case WingAction.Refit:
                {
                    // Refit is a workflow rather than a standing order, so it is dispatched
                    // directly instead of through the directive dispatcher — but it is still
                    // a player command, and gets the same radio acknowledgement as one.
                    refitScratch.Clear();
                    foreach (WingMember member in Commands.Scope(wholeWing))
                    {
                        if (!member.IsCommandable || member.IsSurface) continue;
                        member.RequestRefit();
                        refitScratch.Add(member);
                    }

                    if (refitScratch.Count == 0)
                    {
                        Toast("No selected wingman can be refitted");
                        break;
                    }

                    WingComms.AcknowledgeRefit(refitScratch);
                    break;
                }

                case WingAction.ReturnToBase:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.ReturnToBase), wholeWing));
                    break;

                case WingAction.FallBack:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.FallBack), wholeWing));
                    break;

                case WingAction.OrbitHere:
                {
                    Aircraft leader = Wing.Leader;
                    if (leader == null) { Toast("Not flying"); break; }
                    Show(Commands.Apply(
                        WingDirective.AtPoint(WingOrder.OrbitHere, leader.GlobalPosition()),
                        wholeWing));
                    break;
                }

                case WingAction.FireForEffect:
                    Show(Commands.FireForEffect(CurrentPlayerTargets(), wholeWing));
                    break;

                case WingAction.AttackMyTarget:
                {
                    List<Unit> targets = CurrentPlayerTargets();
                    // The radial is the fast whole-wing command surface: every live
                    // member receives the attack directive, unlike a scoped WMC attack
                    // which deliberately caps useful simultaneous attackers.
                    Show(Commands.Attack(targets, wholeWing, forceAll: wholeWing));
                    break;
                }

                case WingAction.JamMyTarget:
                    Show(Commands.JamTarget(CurrentPlayerTargets(), wholeWing));
                    break;

                case WingAction.CycleRoe:
                {
                    // Cycles all three rungs rather than toggling two, so the wheel can
                    // reach the whole escalation without a submenu.
                    Wing.Roe = RoeRules.Next(Wing.Roe);
                    Toast("ROE: " + RoeRules.Label(Wing.Roe));
                    break;
                }
            }
        }

        internal void IssuePointOrder(WingOrder order, GlobalPosition point, bool append = false)
        {
            IssueMapTask(WingDirective.AtPoint(order, point), order, append);
        }

        internal void IssueMove(GlobalPosition point, bool append)
        {
            float altitude = mapLayer != null ? mapLayer.MoveAltitude : 0f;
            float speed = mapLayer != null ? mapLayer.MoveSpeed : 0f;
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (member == null || !member.Alive) continue;
                bool appendThis = append && member.Order == WingOrder.MoveToPoint;
                member.IssueMapTask(WingDirective.AtPoint(WingOrder.MoveToPoint, point), appendThis);
                member.SetMoveAltitude(altitude);
                member.SetMoveSpeed(speed);
                responders.Add(member);
            }

            AcknowledgeMapIssue(responders, WingOrder.MoveToPoint, append);
        }

        internal void StepMoveHeight(int sign)
        {
            mapLayer?.StepMoveHeight(sign);
        }

        internal void StepMoveSpeed(int sign)
        {
            mapLayer?.StepMoveSpeed(sign);
        }

        internal void AttackUnit(Unit target, bool append = false)
        {
            if (target == null || target.disabled) return;
            IssueMapTask(WingDirective.Attack(target), WingOrder.Attack, append);
        }

        private void IssueMapTask(WingDirective directive, WingOrder order, bool append)
        {
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            var responders = new List<WingMember>();
            foreach (WingMember member in scope)
            {
                if (member == null || !member.Alive) continue;
                if (!WingOrderCatalog.CanApply(member, order)) continue;
                member.IssueMapTask(directive, append);
                responders.Add(member);
            }

            AcknowledgeMapIssue(responders, order, append);
        }

        private void AcknowledgeMapIssue(List<WingMember> responders, WingOrder order, bool append)
        {
            if (responders.Count == 0)
            {
                Toast(WingOrderCatalog.UnavailableReason(order));
                return;
            }

            WingComms.Acknowledge(responders, order);
            if (!append) return;
            int n = responders.Count;
            Toast("Queued " + WingOrderCatalog.Label(order) + " for " + n +
                  " selected wingman" + (n == 1 ? "" : "men"));
        }

        /// <summary>Send a command scope through one scripted manoeuvre, then rejoin.</summary>
        internal void ExecuteManeuver(ManeuverKind kind, bool wholeWing)
        {
            CancelMapOrder(notify: false);
            Show(Commands.Maneuver(kind, wholeWing));
        }

        private static readonly List<WingMember> refitScratch = new List<WingMember>();

        /// <summary>
        /// Left-click a WMC map order: arm it (highlighted) so the next map right-click
        /// applies it. Attack is the exception that also fires immediately when the player
        /// already has targets designated.
        /// </summary>
        internal void SelectMapOrder(WingOrder order)
        {
            if (Selection.IsNone)
            {
                Toast("No wingmen selected");
                return;
            }

            bool alreadyArmed = mapLayer != null && mapLayer.PointArmed &&
                                mapLayer.ArmedOrder == order;
            MapOrderButtonIntent intent = MapOrderPolicy.ResolveButton(
                order, alreadyArmed, CurrentPlayerTargets().Count > 0);

            switch (intent)
            {
                case MapOrderButtonIntent.Disarm:
                    CancelMapOrder(notify: true);
                    return;

                case MapOrderButtonIntent.CargoFallback:
                    CancelMapOrder(notify: false);
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.DeliverCargo),
                                        wholeWing: false));
                    return;

                case MapOrderButtonIntent.ExecuteAndArm:
                    Show(Commands.Attack(CurrentPlayerTargets(), wholeWing: false,
                                         forceAll: false));
                    mapLayer?.ArmPointOrder(order);
                    return;

                case MapOrderButtonIntent.Arm:
                    mapLayer?.ArmPointOrder(order);
                    return;
            }
        }

        internal void ArmPointOrder(WingOrder order) => SelectMapOrder(order);

        internal void CancelMapOrder(bool notify) => mapLayer?.CancelPointOrder(notify);

        /// <summary>
        /// Deliver Cargo, which is the one order with two useful shapes.
        ///
        /// The first press arms a drop point. Pressing again while armed gives up the
        /// point and runs the stock supply route instead.
        /// </summary>
        internal void RequestCargoRun() => SelectMapOrder(WingOrder.DeliverCargo);

        private void Show(WingDispatchResult result)
        {
            if (!result.Success)
            {
                Toast(result.Message);
                return;
            }

            // Successful orders are confirmed by the pilots themselves. Mirroring the same
            // event into MessageUI produced the old black "Wing: Engage" box beside the new
            // radio subtitle. Keep the native feed for actual command failures only.
            WingComms.Acknowledge(result.Responders, result.Order);
        }

        /// <summary>
        /// Everything the player currently has designated, most recent first.
        ///
        /// Read from <c>CombatHUD.GetTargetList()</c>, which is what the player's own HUD
        /// tracks. <c>Pilot.GetPrimaryTarget</c> looks like the obvious source, but nothing
        /// in the game ever calls its setter — only the AI states read and write it — so
        /// for a player-controlled pilot it is always null.
        ///
        /// The whole list matters, not just its head. The player can designate several
        /// contacts, and taking only the first meant the entire wing piled onto one of
        /// them no matter how many were marked.
        /// </summary>
        private static readonly List<Unit> playerTargets = new List<Unit>();

        private static List<Unit> CurrentPlayerTargets()
        {
            playerTargets.Clear();

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return playerTargets;

            List<Unit> targets = hud.GetTargetList();
            if (targets == null) return playerTargets;

            // GetTargetList inserts at the head, so this is already newest-first — which
            // is the right priority order for handing targets out.
            foreach (Unit t in targets)
            {
                if (t != null && !t.disabled && !playerTargets.Contains(t))
                    playerTargets.Add(t);
            }

            return playerTargets;
        }
    }
}
