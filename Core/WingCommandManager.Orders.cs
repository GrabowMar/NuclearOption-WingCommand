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
            switch (action)
            {
                case WingAction.Rejoin:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Formation), wholeWing));
                    break;

                case WingAction.Engage:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Engage), wholeWing));
                    break;

                case WingAction.Refit:
                    foreach (WingMember member in Commands.Scope(wholeWing))
                        if (member.IsCommandable && !member.IsSurface) member.RequestRefit();
                    Toast("Refit: land, replenish and relaunch");
                    break;

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

        internal void IssuePointOrder(WingOrder order, GlobalPosition point)
        {
            Show(Commands.Apply(WingDirective.AtPoint(order, point), wholeWing: false));
        }

        /// <summary>Send a command scope through one scripted manoeuvre, then rejoin.</summary>
        internal void ExecuteManeuver(ManeuverKind kind, bool wholeWing)
        {
            Show(Commands.Maneuver(kind, wholeWing));
        }

        /// <summary>
        /// Send the current command scope after one named unit, from the map.
        ///
        /// Unlike <see cref="WingAction.AttackMyTarget"/> this does not go through the
        /// player's own lock list — the target is whatever was pointed at on the map, which
        /// the player may never have designated in the cockpit at all.
        ///
        /// <c>forceAll</c>, unlike the WMC Attack button. That button caps attackers at the
        /// useful number and leaves the surplus as cover, which is right for a considered
        /// order given from a panel. This is the gesture that used to mean "everything I
        /// have selected, go there", so it has to mean "everything I have selected, hit
        /// that" — capping it would take the aircraft that missed the cut and quietly put
        /// them back on Form Up, which is a worse outcome than the move it replaced.
        /// </summary>
        internal void AttackUnit(Unit target)
        {
            if (target == null || target.disabled) return;
            mapAttackScratch.Clear();
            mapAttackScratch.Add(target);
            Show(Commands.Attack(mapAttackScratch, wholeWing: false, forceAll: true));
        }

        private static readonly List<Unit> mapAttackScratch = new List<Unit>();

        internal void ArmPointOrder(WingOrder order)
        {
            if (Selection.IsNone)
            {
                Toast("No wingmen selected");
                return;
            }
            mapLayer?.ArmPointOrder(order);
        }

        /// <summary>
        /// Deliver Cargo, which is the one order with two useful shapes.
        ///
        /// The first press arms a drop point, because "put it there" is the thing the order
        /// could not previously express. Pressing again while armed gives up the point and
        /// runs the stock supply route instead, which is what the order has always done and
        /// is still the right answer when the player does not care where it goes. The status
        /// line says so while the cursor is armed.
        /// </summary>
        internal void RequestCargoRun()
        {
            if (Selection.IsNone)
            {
                Toast("No wingmen selected");
                return;
            }

            if (mapLayer != null && mapLayer.PointArmed &&
                mapLayer.ArmedOrder == WingOrder.DeliverCargo)
            {
                mapLayer.CancelPointOrder(notify: false);
                Show(Commands.Apply(WingDirective.Simple(WingOrder.DeliverCargo),
                                    wholeWing: false));
                return;
            }

            mapLayer?.ArmPointOrder(WingOrder.DeliverCargo);
        }

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
