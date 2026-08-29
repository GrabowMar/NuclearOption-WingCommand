using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Per-frame driver: tracks the player's aircraft as formation leader, handles input,
    /// and draws the radial menu and wing status panel.
    /// </summary>
    internal class WingCommandManager : MonoBehaviour
    {
        internal static WingCommandManager Instance { get; private set; }

        internal readonly WingRegistry Wing = new WingRegistry();
        internal readonly WingCommandSelection Selection = new WingCommandSelection();
        internal WingDirectiveDispatcher Commands { get; private set; }
        private MapCommandLayer mapLayer;

        internal string MapStatus => mapLayer?.Status;


        // Radial menu state
        private bool radialOpen;
        private Vector2 radialCentre;
        private Vector2 radialDelta;
        private int hoveredSlice = -1;

        private string toast;
        private float toastUntil;
        private string lastToast;
        private float lastToastAt;
        private Aircraft pendingNearestRecruit;
        private float nearestConfirmationUntil;

        private readonly System.Collections.Generic.List<Aircraft> recruitQueue =
            new System.Collections.Generic.List<Aircraft>();

        private static readonly RadialSlice[] Slices =
        {
            new RadialSlice("Form Up", WingAction.Rejoin),
            new RadialSlice("Attack\nMy Target", WingAction.AttackMyTarget),
            new RadialSlice("Engage", WingAction.Engage),
            new RadialSlice("Disengage", WingAction.FallBack),
            new RadialSlice("Return\nTo Base", WingAction.ReturnToBase),
            new RadialSlice("Cycle\nROE", WingAction.CycleRoe),
        };

        private void Awake()
        {
            Instance = this;
            Commands = new WingDirectiveDispatcher(Wing, Selection);
            mapLayer = new MapCommandLayer(Wing);
            Wing.Roe = Plugin.Config2.DefaultRoe.Value;
        }

        private void Update()
        {
            if (!InPlayableState())
            {
                if (radialOpen) CloseRadial(apply: false);
                WingHud.ResetStatusPanel();
                WmcScreen.Reset();
                PlayerFireWatcher.Reset();
                WingComms.Reset();
                TacticalCoordinator.Reset();
                WingMarkers.Reset();
                AiCombatTweak.Reset();
                WingShop.Reset();
                WingRecruitment.Reset();
                WingSupplyReserve.Reset();
                WingTakeover.Reset();
                mapLayer?.Reset();
                Wing.Clear();
                Selection.Reset();
                pendingNearestRecruit = null;
                nearestConfirmationUntil = 0f;
                return;
            }

            // The player's own aircraft is always the formation leader.
            Wing.SetLeader(GameManager.GetLocalAircraft(out Aircraft local) ? local : null);
            WingSupplyReserve.Tick(local);
            Wing.Prune();
            Selection.Prune(Wing);
            WingTakeover.Tick();
            Wing.CheckThreats();
            Wing.CheckLeashes();
            Wing.CheckReserves();
            PlayerFireWatcher.Track(local);

            if (NativeRadialActive)
                WingRadialMenu.Tick();

            HandleRadialInput();
            HandleHotkeys();

            if (Plugin.Config2.MapCommandEnabled.Value)
                mapLayer.Update();

            WingMarkers.Tick(Wing);
            WingHud.TickStatusPanel(Wing);
            WmcScreen.Tick(Wing);
            FlushRecruitQueue();
        }

        /// <summary>Assign an aircraft to the wing on the next frame.</summary>
        internal void QueueRecruit(Aircraft aircraft)
        {
            if (aircraft != null) recruitQueue.Add(aircraft);
        }

        /// <summary>
        /// Aircraft spawned during this frame have not finished initialising their pilot
        /// state machine yet, so assignment is deferred by a frame.
        /// </summary>
        private void FlushRecruitQueue()
        {
            if (recruitQueue.Count == 0) return;

            Aircraft[] batch = recruitQueue.ToArray();
            recruitQueue.Clear();

            foreach (Aircraft a in batch)
            {
                if (a == null || a.disabled) continue;
                if (Wing.Count >= Plugin.Config2.MaxWingSize.Value) break;
                Wing.Add(a);
            }
        }

        /// <summary>
        /// The native wheel is used when the player asked for it and every private member
        /// it depends on resolved. Otherwise the standalone fallback wheel takes over.
        /// </summary>
        internal static bool NativeRadialActive =>
            Plugin.Config2.UseNativeRadial.Value && GameAccess.Available;

        private static bool InPlayableState()
        {
            GameState s = GameManager.gameState;
            return s == GameState.SinglePlayer || s == GameState.Multiplayer;
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Standalone fallback wheel, used only when the native integration is switched
        /// off or the game's radial singleton is absent.
        /// </summary>
        private void HandleRadialInput()
        {
            if (NativeRadialActive && SceneSingleton<RadialMenuMain>.i != null)
            {
                if (radialOpen) CloseRadial(apply: false);
                return;
            }

            KeyCode key = Plugin.Config2.RadialKey.Value;
            if (key == KeyCode.None) return;

            if (Input.GetKeyDown(key) && Wing.Leader != null)
            {
                radialOpen = true;
                radialCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                radialDelta = Vector2.zero;
                hoveredSlice = -1;
            }
            else if (Input.GetKeyUp(key) && radialOpen)
            {
                CloseRadial(apply: true);
            }

            if (radialOpen)
            {
                AccumulateRadialDelta();
                hoveredSlice = SliceFromDelta();
            }
        }

        /// <summary>
        /// In flight the cursor is captured for mouse-look, so <c>Input.mousePosition</c>
        /// does not move. The game's own wheel integrates the Rewired look axes instead;
        /// this mirrors that exactly, including the decay term.
        /// </summary>
        private void AccumulateRadialDelta()
        {
            Rewired.Player p = GameManager.playerInput;
            if (p != null)
            {
                radialDelta += new Vector2(p.GetAxis("Pan View"), -p.GetAxis("Tilt View")) * 0.5f;
            }
            else
            {
                radialDelta += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            }

            radialDelta = Vector2.Lerp(radialDelta, Vector2.zero, 0.05f);
        }

        private void HandleHotkeys()
        {
            if (Wing.Count == 0) return;

            if (Plugin.Config2.QuickRejoinKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Config2.QuickRejoinKey.Value))
                Execute(WingAction.Rejoin);

            if (Plugin.Config2.QuickEngageKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Config2.QuickEngageKey.Value))
                Execute(WingAction.Engage);
        }

        /// <summary>Same angle convention the stock wheel uses: index 0 at the top, clockwise.</summary>
        private int SliceFromDelta()
        {
            if (radialDelta.sqrMagnitude <= 0.1f) return hoveredSlice;

            float angle = -Vector2.SignedAngle(Vector2.up, radialDelta.normalized);
            if (angle < 0f) angle += 360f;

            float per = 360f / Slices.Length;
            angle = Mathf.Repeat(angle + per * 0.5f, 360f);
            return Mathf.Clamp(Mathf.FloorToInt(angle / per), 0, Slices.Length - 1);
        }

        private void CloseRadial(bool apply)
        {
            if (apply && hoveredSlice >= 0 && hoveredSlice < Slices.Length)
                Execute(Slices[hoveredSlice].Action);

            radialOpen = false;
            hoveredSlice = -1;
        }

        // ---------------------------------------------------------------- actions

        internal void Execute(WingAction action) => Execute(action, wholeWing: true);

        /// <summary>
        /// Run an interface action. Radial/hotkey callers use the whole wing; WMC/map
        /// callers explicitly pass <paramref name="wholeWing"/> as false.
        /// </summary>
        internal void Execute(WingAction action, bool wholeWing)
        {
            switch (action)
            {
                case WingAction.RecruitNearest:
                {
                    RecruitNearestWithConfirmation();
                    break;
                }

                case WingAction.Rejoin:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Formation), wholeWing));
                    break;

                case WingAction.Engage:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.Engage), wholeWing));
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

                case WingAction.DeliverCargo:
                    Show(Commands.Apply(WingDirective.Simple(WingOrder.DeliverCargo), wholeWing));
                    break;

                case WingAction.LandHere:
                {
                    Aircraft leader = Wing.Leader;
                    if (leader == null) { Toast("Not flying"); break; }
                    Show(Commands.Apply(
                        WingDirective.AtPoint(WingOrder.LandHere, leader.GlobalPosition()),
                        wholeWing));
                    break;
                }

                case WingAction.CycleShape:
                {
                    FormationShape next = FormationShapes.CycleCore(Plugin.Config2.Shape.Value, 1);
                    Plugin.Config2.Shape.Value = next;
                    Toast("Formation: " + FormationShapes.Pretty(next));
                    break;
                }

                case WingAction.AttackMyTarget:
                {
                    List<Unit> targets = CurrentPlayerTargets();
                    // The radial is the fast whole-wing command surface: every live
                    // member receives the attack directive, unlike a scoped WMC attack
                    // which deliberately caps useful simultaneous attackers.
                    Show(Commands.Attack(targets, wholeWing, forceAll: wholeWing));
                    break;
                }

                case WingAction.CycleRoe:
                {
                    // Cycles all three rungs rather than toggling two, so the wheel can
                    // reach the whole escalation without a submenu.
                    Wing.Roe = (WingRoe)(((int)Wing.Roe + 1) % 3);
                    Toast("ROE: " + Wing.Roe.ToString().ToUpperInvariant());
                    break;
                }

                case WingAction.Disband:
                    if (RequireWing())
                    {
                        int n = Wing.Count;
                        Wing.DisbandAll("player disbanded wing");
                        Toast("Wing disbanded (" + n + ")");
                    }
                    break;
            }
        }

        private void RecruitNearestWithConfirmation()
        {
            Aircraft candidate = Wing.FindNearestRecruitCandidate();
            if (candidate == null)
            {
                pendingNearestRecruit = null;
                Toast(Wing.Count >= Plugin.Config2.MaxWingSize.Value
                    ? "Wing is full"
                    : "No eligible friendly AI aircraft in range");
                return;
            }

            float cost = WingRecruitment.PriceOf(Wing, candidate);
            if (WingShop.Allocation < cost)
            {
                pendingNearestRecruit = null;
                Toast("Assignment costs " + Mathf.RoundToInt(cost) + ", have " +
                      Mathf.RoundToInt(WingShop.Allocation));
                return;
            }

            if (candidate != pendingNearestRecruit || Time.unscaledTime > nearestConfirmationUntil)
            {
                pendingNearestRecruit = candidate;
                nearestConfirmationUntil = Time.unscaledTime + 5f;
                Toast("Assign nearest " + candidate.unitName + " for " +
                      Mathf.RoundToInt(cost) + " - press ASSIGN NEAREST again to confirm");
                return;
            }

            pendingNearestRecruit = null;
            nearestConfirmationUntil = 0f;
            if (WingRecruitment.TryRecruit(Wing, candidate, out WingMember member, out string why))
                Toast(member.Name + " joined as " + SlotName(member.Slot));
            else
                Toast(why);
        }

        internal void IssuePointOrder(WingOrder order, GlobalPosition point)
        {
            Show(Commands.Apply(WingDirective.AtPoint(order, point), wholeWing: false));
        }

        internal void ArmPointOrder(WingOrder order)
        {
            if (Selection.IsNone)
            {
                Toast("No wingmen selected");
                return;
            }
            mapLayer?.ArmPointOrder(order);
        }

        internal void SelectMember(WingMember member, bool toggle)
        {
            if (toggle) Selection.Toggle(member);
            else Selection.SelectOnly(member);
            foreach (WingMember candidate in Wing.Members)
                WingMarkers.Repaint(candidate.Aircraft);
        }

        internal void SelectAllMembers()
        {
            Selection.SelectAll();
            foreach (WingMember member in Wing.Members) WingMarkers.Repaint(member.Aircraft);
        }

        private void Show(WingDispatchResult result)
        {
            Toast(result.Message);
            if (result.Success && result.Applied > 0)
            {
                WingMember speaker = Commands.Scope(wholeWing: false).Count > 0
                    ? Commands.Scope(wholeWing: false)[0]
                    : Wing.Members.Count > 0 ? Wing.Members[0] : null;
                WingComms.Say(speaker, WingComms.Call.Copy);
            }
        }

        /// <summary>Drop one member back to the stock AI. Used by the map panel.</summary>
        internal void RemoveMember(WingMember member)
        {
            if (member == null) return;
            string name = member.Name;
            Wing.Remove(member, "removed from the map panel");
            Toast(name + " released");
        }

        /// <summary>Assign the current map selection to the wing. Used by the map panel.</summary>
        internal void AddSelectedFromMap()
        {
            mapLayer?.AddSelected();
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

        private bool RequireWing()
        {
            if (Wing.Count > 0) return true;
            Toast("No wingmen assigned");
            return false;
        }

        /// <summary>
        /// Confirmation of a player order. Routed into the game's own on-screen message
        /// feed rather than drawn as a custom overlay, so it matches everything else on
        /// screen by construction instead of by imitation. The IMGUI box remains only as a
        /// fallback for when that feed is unavailable.
        /// </summary>
        internal void Toast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (message == lastToast && Time.unscaledTime - lastToastAt < 1.25f) return;
            lastToast = message;
            lastToastAt = Time.unscaledTime;

            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo("[Wing] " + message);

            try
            {
                MessageUI ui = SceneSingleton<MessageUI>.i;
                if (ui != null)
                {
                    ui.GameMessage(message);
                    return;
                }
            }
            catch { /* fall through to the overlay */ }

            toast = message;
            toastUntil = Time.unscaledTime + 3f;
        }

        internal static string SlotName(int slot)
        {
            return "wingman " + (slot + 1);
        }

        // --------------------------------------------------------------------- UI

        private void OnGUI()
        {
            if (!InPlayableState()) return;

            WingTakeover.DrawWindow();

            if (radialOpen)
                WingHud.DrawRadial(Slices, radialCentre, hoveredSlice);

            if (toast != null && Time.unscaledTime < toastUntil)
                WingHud.DrawToast(toast);
        }
    }

    internal enum WingAction
    {
        RecruitNearest,
        Rejoin,
        Engage,
        ReturnToBase,
        FallBack,
        OrbitHere,
        DeliverCargo,
        LandHere,
        CycleShape,
        AttackMyTarget,
        CycleRoe,
        Disband,
    }

    internal struct RadialSlice
    {
        public readonly string Label;
        public readonly WingAction Action;

        public RadialSlice(string label, WingAction action)
        {
            Label = label;
            Action = action;
        }
    }
}
