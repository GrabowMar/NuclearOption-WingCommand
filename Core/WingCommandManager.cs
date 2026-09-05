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
    ///
    /// Split by concern across partial files: see WingCommandManager.Radial.cs (radial menu),
    /// .Recruit.cs (delivery queue), .Orders.cs (action dispatch) and .Selection.cs (roster
    /// selection).
    /// </summary>
    internal partial class WingCommandManager : MonoBehaviour
    {
        internal static WingCommandManager Instance { get; private set; }

        internal readonly WingRegistry Wing = new WingRegistry();
        internal readonly WingCommandSelection Selection = new WingCommandSelection();
        internal WingDirectiveDispatcher Commands { get; private set; }
        private MapCommandLayer mapLayer;

        internal string MapStatus => mapLayer?.Status;

        /// <summary>True while the map layer has something specific to report.</summary>
        internal bool MapStatusIsNotice => mapLayer != null && mapLayer.HasNotice;

        private string toast;
        private float toastUntil;
        private string lastToast;
        private float lastToastAt;
        private bool resetForNonPlayableState;

        private void Awake()
        {
            Instance = this;
            Commands = new WingDirectiveDispatcher(Wing, Selection);
            mapLayer = new MapCommandLayer(Wing);
            Wing.Roe = Plugin.Settings.DefaultRoe.Value;
            WingFormation.Shape = Plugin.Settings.FormationShape.Value;
            WingFormation.SlotSpacing = Plugin.Settings.FormationSpacing.Value;
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
                MfdWallpaper.Reset();
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
                WingFormation.Shape = Plugin.Settings.FormationShape.Value;
                WingFormation.SlotSpacing = Plugin.Settings.FormationSpacing.Value;

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
            MfdPresentation.Tick();
            VanillaMfdRebuild.Tick();
            MfdLogPanel.Tick();
            WingComms.Tick(Wing);
            WingShopDelivery.Tick();
            FlushRecruitQueue();
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
        Refit,
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
