using System.Collections.Generic;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        internal void Execute(WingAction action) => Execute(action, wholeWing: true);

        /// <summary>Execute a UI action. Radial and hotkeys target the whole wing; WMC and map pass
        /// wholeWing=false.</summary>
        internal void Execute(WingAction action, bool wholeWing)
        {
            Plugin.LogAction($"request action={action} scope={(wholeWing ? "wing" : "selection")}");
            // Clear armed map tools after an immediate order so the next right-click defaults to Move.
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
                    // Dispatch refit directly as a recovery workflow, with the usual command
                    // acknowledgement.
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
                    Plugin.LogAction($"result action=Refit applied={refitScratch.Count}");
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
                    // Whole-wing radial attacks reach every live member; scoped WMC attacks cap useful
                    // attackers.
                    Show(Commands.Attack(targets, wholeWing, forceAll: wholeWing));
                    break;
                }

                case WingAction.JamMyTarget:
                    Show(Commands.JamTarget(CurrentPlayerTargets(), wholeWing));
                    break;

                case WingAction.CycleRoe:
                {
                    // Cycle all three ROE levels without a submenu.
                    Wing.Roe = CombatFacade.Roe.Next(Wing.Roe);
                    Toast("ROE: " + CombatFacade.Roe.Label(Wing.Roe));
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
            Plugin.LogAction($"request order=MoveToPoint point={point} append={append} altitude={altitude} speed={speed}");
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
            Plugin.LogAction($"request order={order} point={directive.Point} hasPoint={directive.HasPoint} target={directive.Target?.unitName} append={append}");
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
            Plugin.LogAction($"result order={order} applied={responders.Count} append={append}");
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

        /// <summary>Run one manoeuvre for the command scope, then rejoin.</summary>
        internal void ExecuteManeuver(ManeuverKind kind, bool wholeWing)
        {
            Plugin.LogAction($"request maneuver={kind} scope={(wholeWing ? "wing" : "selection")}");
            CancelMapOrder(notify: false);
            Show(Commands.Maneuver(kind, wholeWing));
        }

        private static readonly List<WingMember> refitScratch = new List<WingMember>();

        /// <summary>Arm a WMC order for the next map right-click. Attack also executes immediately if
        /// player targets are already designated.</summary>
        internal void SelectMapOrder(WingOrder order)
        {
            Plugin.LogAction($"select map order={order}");
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

        private void Show(WingDispatchResult result)
        {
            Plugin.LogAction($"result success={result.Success} applied={result.Applied} message={result.Message}");
            if (!result.Success)
            {
                Toast(result.Message);
                return;
            }

            // Pilots acknowledge successful orders; reserve the native feed for command failures to
            // avoid duplicate notices.
            WingComms.Acknowledge(result.Responders, result.Order);
        }

        /// <summary>Player designations from CombatHUD.GetTargetList, newest first. Pilot.GetPrimaryTarget
        /// is populated by AI, not player HUD selection. Keep all designations for target
        /// distribution.</summary>
        private static readonly List<Unit> playerTargets = new List<Unit>();

        private static List<Unit> CurrentPlayerTargets()
        {
            playerTargets.Clear();

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return playerTargets;

            List<Unit> targets = hud.GetTargetList();
            if (targets == null) return playerTargets;

            // Native insertion at the head already gives newest-first priority.
            foreach (Unit t in targets)
            {
                if (t != null && !t.disabled && !playerTargets.Contains(t))
                    playerTargets.Add(t);
            }

            return playerTargets;
        }
    }
}
