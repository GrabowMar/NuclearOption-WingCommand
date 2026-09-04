using System;
using System.Collections.Generic;
using UnityEngine;

// Unity invokes Awake, Update and OnGUI by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

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

        /// <summary>True while the map layer has something specific to report.</summary>
        internal bool MapStatusIsNotice => mapLayer != null && mapLayer.HasNotice;


        // Radial menu state
        private bool radialOpen;
        private Vector2 radialDelta;
        private int hoveredSlice = -1;

        private string toast;
        private float toastUntil;
        private string lastToast;
        private float lastToastAt;
        private bool resetForNonPlayableState;

        /// <summary>An aircraft on its way into the wing, and how long it has to get there.</summary>
        private struct PendingRecruit
        {
            public Aircraft Aircraft;
            public WingMember Member;
            public float ReadyAt;
            public float Deadline;
        }

        /// <summary>How long a delivery has to taxi out and get off the ground.</summary>
        private const float RecruitTimeout = 420f;

        private readonly List<PendingRecruit> recruitQueue = new List<PendingRecruit>();

        private static RadialSlice[] slices;
        private static int slicesRevision = -1;

        /// <summary>
        /// The overlay wheel's ten cards.
        ///
        /// Rebuilt when <see cref="WingHost.Revision"/> moves rather than being a static
        /// initialiser, because the rejoin card names an order whose meaning a host profile
        /// can change - "FORM UP" is not what the wing does above a moving warship - and a
        /// once-per-process array would keep showing the aircraft wording forever.
        /// </summary>
        private static RadialSlice[] Slices
        {
            get
            {
                if (slices != null && slicesRevision == WingHost.Revision) return slices;
                slicesRevision = WingHost.Revision;
                slices = BuildSlices();
                return slices;
            }
        }

        private static RadialSlice[] BuildSlices() => new[]
        {
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Formation).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "ON STATION" : "REJOIN",
                WingAction.Rejoin, "rejoin"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Attack).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "PRIORITY LOCK" : "PRIORITY LOCK",
                WingAction.AttackMyTarget, "attack"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Engage).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "CLOSE AIR SUPPORT" : "SEARCH & DESTROY",
                WingAction.Engage, "engage"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.FallBack).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "BREAK CONTACT" : "DEFENSIVE BREAK",
                WingAction.FallBack, "fallback"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.ReturnToBase).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "WITHDRAW" : "RTB RECOVERY",
                WingAction.ReturnToBase, "rtb"),
            new RadialSlice("CYCLE ROE", "RULES OF ENGAGEMENT", WingAction.CycleRoe, "posture"),
        };

        private void Awake()
        {
            Instance = this;
            Commands = new WingDirectiveDispatcher(Wing, Selection);
            mapLayer = new MapCommandLayer(Wing);
            Wing.Roe = Plugin.Settings.DefaultRoe.Value;
        }

        private void Update()
        {
            if (!InPlayableState())
            {
                // Update continues to run in menus. Teardown is transition work, not frame
                // work: several resets clear caches, destroy UI, or roll back transactions.
                if (resetForNonPlayableState) return;

                if (radialOpen) CloseRadial(apply: false);
                WingRadialOverlay.Reset();
                WingHud.ResetStatusPanel();
                WmcScreen.Reset();
                SetScreen.Reset();
                WingComms.Reset();
                TacticalCoordinator.Reset();
                WingMarkers.Reset();
                WingShopDelivery.Reset();
                WingShop.Reset();
                WingRecruitment.Reset();
                WingPilotRoster.Reset();
                WingKillCredit.Reset();
                WingDeliveryTracker.Reset();
                recruitQueue.Clear();
                WingRecovery.Reset();
                WingDeparture.Reset();
                WingSupplyReserve.Reset();
                WingTakeover.Reset();
                WingUi.Reset();
                MfdRailPatch.Reset();
                mapLayer?.Reset();
                FormationFlyState.ResetTerrainCache();
                Wing.Clear();
                Selection.Reset();
                WingInteropPush.Clear();
                resetForNonPlayableState = true;
                return;
            }

            if (resetForNonPlayableState)
            {
                // First frame back in a mission: resolve the Smart/Performance mode for
                // this one. Snapshotting here is what makes a mid-mission change inert
                // until the next mission.
                WingBrain.Begin(Plugin.Settings.Mode.Value);

                // A reflex disabled by a fault in the last mission gets another chance in
                // this one; a genuinely broken one faults again immediately at no real cost.
                // Behaviour factories are dropped outright: they close over nothing that
                // survives a mission, and leaving them registered leaked a previous
                // mission's states into this one.
                WingAi.ResetFaults();
                WingBehaviourCatalog.Clear();
                Plugin.Logger.LogInfo("[WingBrain] mission start - " + WingBrain.Summary());
            }
            resetForNonPlayableState = false;

            // The player's own aircraft is always the formation leader.
            Wing.SetLeader(GameManager.GetLocalAircraft(out Aircraft local) ? local : null);
            WingSupplyReserve.Tick();
            WingShop.Tick();

            // Before Prune, deliberately: a wingman that has completed its RTB has an
            // ejected pilot, which Prune would otherwise report as a combat loss.
            WingRecovery.Tick(Wing);
            Wing.Prune();
            Selection.Prune(Wing);
            WingTakeover.Tick();
            // Housekeeping first - it can retire an order - then one arbitration pass that
            // decides what every member actually flies. These used to be three separate
            // passes whose order was the priority system.
            Wing.CheckReserves();
            Wing.Tick();

            if (NativeRadialActive)
                WingRadialMenu.Tick();

            HandleRadialInput();
            HandleHotkeys();

            WingInteropPush.Publish(Wing);

            if (Plugin.Settings.MapCommandEnabled.Value)
                mapLayer.Update();

            WingKillCredit.Tick();
            WingDeliveryTracker.Tick();
            WingMarkers.Tick(Wing);
            WingHud.TickStatusPanel(Wing);
            WmcScreen.Tick(Wing);
            SetScreen.Tick();
            VanillaMfdRebuild.Tick();
            MfdLogPanel.Tick();
            WingComms.Tick(Wing);
            WingShopDelivery.Tick();
            FlushRecruitQueue();
        }

        /// <summary>
        /// Put a delivery on the wing roster immediately, then wait to take command until the
        /// airbase has launched it. The roster and HUD can therefore show the aircraft while
        /// the stock taxi/door sequence still owns its controls.
        /// </summary>
        internal void QueueRecruit(Aircraft aircraft)
        {
            if (aircraft == null) return;

            if (Wing.Find(aircraft) != null) return;
            for (int i = 0; i < recruitQueue.Count; i++)
                if (recruitQueue[i].Aircraft == aircraft) return;

            WingMember member = WingRegistry.HasRoom(Wing.Count)
                ? Wing.Add(aircraft, deferCommand: true)
                : null;

            if (member != null)
            {
                Plugin.Logger.LogInfo("[Wing] " + aircraft.unitName +
                                      " rostered slot " + member.Slot +
                                      ", awaiting airborne activation");
            }
            else
            {
                Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
                Plugin.Logger.LogInfo(
                    "[Wing] " + aircraft.unitName + " bought but not yet rostered" +
                    " (LocalSim=" + aircraft.LocalSim +
                    ", room=" + WingRegistry.HasRoom(Wing.Count) +
                    ", pilot=" + (pilot != null) + ")");
            }

            recruitQueue.Add(new PendingRecruit
            {
                Aircraft = aircraft,
                Member = member,
                ReadyAt = Time.unscaledTime + 0.25f,
                Deadline = Time.unscaledTime + RecruitTimeout,
            });
        }

        /// <summary>
        /// Activate deliveries once they can actually hold station.
        ///
        /// Two waits, for two different reasons. An aircraft spawned this frame has not
        /// finished initialising its pilot state machine, so nothing may touch it yet. And an
        /// aircraft delivered into a hangar is parked: it has to taxi out and take off under
        /// the stock AI first, and switching it to formation flight on the apron would strand
        /// it there with its gear up.
        /// </summary>
        private void FlushRecruitQueue()
        {
            for (int i = recruitQueue.Count - 1; i >= 0; i--)
            {
                PendingRecruit p = recruitQueue[i];
                Aircraft a = p.Aircraft;

                if (a == null || a.disabled)
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                if (Time.unscaledTime > p.Deadline)
                {
                    if (p.Member != null && Wing.Contains(p.Member))
                        Wing.Remove(p.Member, "delivery never got airborne");
                    recruitQueue.RemoveAt(i);
                    Toast(p.Member == null
                        ? a.unitName + " never joined the wing - assign it from the map when airborne"
                        : a.unitName + " never got airborne - assign it from the map when it does");
                    continue;
                }

                // If the immediate add had no slot, claim one as soon as another member
                // leaves. Once claimed, it stays on the roster through taxi and launch.
                if (p.Member == null)
                {
                    p.Member = Wing.Find(a);
                    if (p.Member == null && WingRegistry.HasRoom(Wing.Count))
                    {
                        p.Member = Wing.Add(a, deferCommand: true);
                        if (p.Member != null)
                            Plugin.Logger.LogInfo("[Wing] " + a.unitName +
                                                  " rostered slot " + p.Member.Slot +
                                                  " after wait, awaiting airborne activation");
                    }
                    recruitQueue[i] = p;
                    if (p.Member == null) continue;
                }

                // A player may release a still-parked delivery. Do not add it back from the
                // queue after that explicit removal.
                if (!Wing.Contains(p.Member))
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                // Not yet flying: keep waiting while the roster already shows the member.
                if (Time.unscaledTime < p.ReadyAt) continue;
                if (!p.Member.IsAirborne) continue;

                recruitQueue.RemoveAt(i);
                p.Member.ActivateWhenAirborne();
            }
        }

        /// <summary>
        /// The native wheel is used whenever every private member it depends on resolved.
        /// Nothing else gates it.
        ///
        /// It used to also require the standalone wheel's key to be unbound, on the theory
        /// that binding a key was a deliberate opt-out. That coupling made a keybind
        /// silently delete a whole feature: a key left bound in an existing config removed
        /// the Wing Command slice from the game's wheel with no message anywhere, and the
        /// only visible symptom was a stock-looking wheel — indistinguishable from the
        /// integration being broken. A key that opens our own wheel and a slice on the
        /// game's wheel are not mutually exclusive, so they are no longer wired to each
        /// other; the key is now purely an *additional* way in.
        /// </summary>
        internal static bool NativeRadialActive => GameAccess.Available;

        private static bool InPlayableState()
        {
            GameState s = GameManager.gameState;
            return s == GameState.SinglePlayer || s == GameState.Multiplayer;
        }

        // ------------------------------------------------------------------ input

        private float lastSliceSelectTime;

        /// <summary>
        /// The mod's own wheel, opened by the optional key. Independent of the slice on the
        /// game's wheel: binding a key adds a second way in rather than turning the first
        /// one off, so an unbound key is now the only thing this checks.
        /// </summary>
        private void HandleRadialInput()
        {
            KeyCode key = Plugin.Settings.RadialKey.Value;
            if (key == KeyCode.None)
            {
                if (radialOpen) CloseRadial(apply: false);
                return;
            }

            // Right-click while radial is open cancels immediately
            if (radialOpen && Input.GetMouseButtonDown(1))
            {
                CloseRadial(apply: false);
                return;
            }

            if (Input.GetKeyDown(key) && Wing.Leader != null)
            {
                radialOpen = true;
                radialDelta = Vector2.zero;
                hoveredSlice = -1;
                lastSliceSelectTime = 0f;
            }
            else if (Input.GetKeyUp(key) && radialOpen)
            {
                CloseRadial(apply: true);
                return;
            }

            if (radialOpen)
            {
                AccumulateRadialDelta();
                hoveredSlice = SliceFromDelta();
                WingRadialOverlay.Show(Slices, hoveredSlice, Wing);
            }
            else
            {
                WingRadialOverlay.Hide();
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
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            Vector2 mouse = new Vector2(mx, my);

            if (p != null)
            {
                Vector2 look = new Vector2(p.GetAxis("Pan View"), -p.GetAxis("Tilt View")) * 0.5f;
                if (look.sqrMagnitude > mouse.sqrMagnitude)
                    mouse = look;

                float stickH = p.GetAxis("Radial Menu Horizontal");
                float stickV = p.GetAxis("Radial Menu Vertical");
                Vector2 stick = new Vector2(stickH, stickV);
                if (stick.sqrMagnitude > 0.1f)
                {
                    radialDelta = stick * 2.5f;
                    return;
                }
            }

            radialDelta += mouse * 1.6f;
            radialDelta = Vector2.ClampMagnitude(radialDelta, 3.0f);
            radialDelta = Vector2.Lerp(radialDelta, Vector2.zero, 0.04f);
        }

        private void HandleHotkeys()
        {
            if (Wing.Count == 0) return;

            if (Plugin.Settings.QuickRejoinKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickRejoinKey.Value))
                Execute(WingAction.Rejoin);

            if (Plugin.Settings.QuickEngageKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickEngageKey.Value))
                Execute(WingAction.Engage);
        }

        /// <summary>Same angle convention the stock wheel uses: index 0 at the top, clockwise.</summary>
        private int SliceFromDelta()
        {
            if (radialDelta.sqrMagnitude > 0.08f)
            {
                lastSliceSelectTime = Time.unscaledTime;

                float angle = -Vector2.SignedAngle(Vector2.up, radialDelta.normalized);
                if (angle < 0f) angle += 360f;

                float per = 360f / Slices.Length;
                angle = Mathf.Repeat(angle + per * 0.5f, 360f);
                return Mathf.Clamp(Mathf.FloorToInt(angle / per), 0, Slices.Length - 1);
            }

            // In deadzone: latch previous selection for 1.2s so stopping mouse drag doesn't drop selection!
            if (hoveredSlice >= 0 && (Time.unscaledTime - lastSliceSelectTime) < 1.2f)
            {
                return hoveredSlice;
            }

            return -1;
        }

        private void CloseRadial(bool apply)
        {
            if (apply && hoveredSlice >= 0 && hoveredSlice < Slices.Length)
                Execute(Slices[hoveredSlice].Action);

            radialOpen = false;
            hoveredSlice = -1;
            lastSliceSelectTime = 0f;
            WingRadialOverlay.Hide();
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

        internal void SelectMember(WingMember member, bool toggle)
        {
            if (toggle) Selection.Toggle(member);
            else Selection.SelectOnly(member);
            foreach (WingMember candidate in Wing.Members)
                WingMarkers.Repaint(candidate.Aircraft);
        }

        /// <summary>
        /// Set which weapons the current command scope reaches for first.
        ///
        /// Scoped like an order rather than held wing-wide, so a mixed flight can be split
        /// between the air and the ground without changing anyone's rules of engagement.
        /// </summary>
        internal void SetWeaponPreference(WingWeaponPreference preference)
        {
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            if (scope.Count == 0)
            {
                Toast(Wing.Count == 0
                    ? "No wingmen. Requisition on SUPPLY."
                    : "No wingmen selected");
                return;
            }

            foreach (WingMember member in scope) member.WeaponPreference = preference;

            Toast((Selection.IsAll ? "Wing" : scope.Count + " selected") + ": weapons " +
                  WingWeaponPreferences.Label(preference));
        }

        /// <summary>
        /// The preference shared by the current scope, or null when they disagree. The
        /// selector uses this to decide which button to light.
        /// </summary>
        internal WingWeaponPreference? ScopeWeaponPreference()
        {
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            if (scope.Count == 0) return null;

            WingWeaponPreference first = scope[0].WeaponPreference;
            for (int i = 1; i < scope.Count; i++)
            {
                if (scope[i].WeaponPreference != first) return null;
            }
            return first;
        }

        internal void SelectAllMembers()
        {
            Selection.SelectAll();
            foreach (WingMember member in Wing.Members) WingMarkers.Repaint(member.Aircraft);
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

        /// <summary>Drop one member back to the stock AI. Used by the map panel.</summary>
        internal void RemoveMember(WingMember member)
        {
            if (member == null) return;
            string name = member.Name;
            Wing.Remove(member, "removed from the map panel");
            Toast(name + " released - returning to base");
        }

        /// <summary>
        /// Grant or revoke temporary flight lead. Pressing it on the current lead, or on a
        /// second wingman, hands it over cleanly - there is only ever one lead.
        /// </summary>
        internal void ToggleFlightLead(WingMember member)
        {
            if (member == null) return;

            if (Wing.FlightLead == member)
            {
                Wing.ClearFlightLead();
                Toast("Flight lead released - wing forming on you");
                return;
            }

            Toast(Wing.TrySetFlightLead(member, out string reason)
                ? member.Name + " leads the flight - wing forming on them"
                : "Cannot make " + member.Name + " lead: " + reason);
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

        /// <summary>
        /// Internal gameplay notice. These remain available to verbose diagnostics but no
        /// longer enter MessageUI: its black boxes were the obsolete second chatter/log
        /// surface. Command state belongs on the map/WMC; pilot events belong on radio.
        /// </summary>
        internal void Toast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo("[Wing] " + message);
        }

        /// <summary>The only mod messages intentionally allowed into the old game feed.</summary>
        internal void DebugToast(string message)
        {
            if (!Plugin.Settings.EnableDebugActions.Value || string.IsNullOrWhiteSpace(message))
                return;
            if (message == lastToast && Time.unscaledTime - lastToastAt < 1.25f) return;
            lastToast = message;
            lastToastAt = Time.unscaledTime;

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

        // --------------------------------------------------------------------- UI

        private void OnGUI()
        {
            if (!InPlayableState()) return;

            // The aircraft-recovery prompt and the radial command wheel are native uGUI;
            // only the debug-only fallback toast still lives here.
            if (toast != null && Time.unscaledTime < toastUntil)
                WingHud.DrawToast(toast);
        }
    }

    internal enum WingAction
    {
        Rejoin,
        Engage,
        FireForEffect,
        ReturnToBase,
        FallBack,
        OrbitHere,
        AttackMyTarget,
        CycleRoe,
        JamMyTarget,
    }

    internal struct RadialSlice
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly WingAction Action;
        public readonly string IconKey;

        public RadialSlice(string title, string subtitle, WingAction action, string iconKey)
        {
            Title = title;
            Subtitle = subtitle;
            Action = action;
            IconKey = iconKey;
        }
    }
}
