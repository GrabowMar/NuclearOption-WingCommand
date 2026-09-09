using System;
using System.Collections.Generic;
using UnityEngine;

// Unity calls Awake, Update, and OnGUI by reflection, so suppress IDE0051 in this file.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Tracks the player leader, drives per-frame updates, handles input, and renders wing UI.
    /// Partial files separate radial input, recruitment, orders, and selection.</summary>
    [DefaultExecutionOrder(10000)]
    internal partial class WingCommandManager : MonoBehaviour
    {
        internal static WingCommandManager Instance { get; private set; }

        internal readonly WingRegistry Wing = new WingRegistry();
        internal readonly WingCommandSelection Selection = new WingCommandSelection();
        internal WingDirectiveDispatcher Commands { get; private set; }
        private MapCommandLayer mapLayer;

        internal string MapStatus => mapLayer?.Status;

        /// <summary>Whether the map layer has an active notice.</summary>
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

        private void FixedUpdate()
        {
            if (InPlayableState()) WingAirfield.Tick();
        }

        private void Update()
        {
            if (!InPlayableState())
            {
                // Run teardown once on entering menus; resets may destroy UI, clear caches, or roll
                // back purchases.
                if (resetForNonPlayableState) return;

                if (radialOpen) CloseRadial(apply: false);
                WingRadialOverlay.Reset();
                WingHud.ResetStatusPanel();
                WmcScreen.Reset();
                WingComms.Reset();
                TacticalCoordinator.Reset();
                WingMarkers.Reset();
                WingShopDelivery.Reset();
                WingAirfield.Reset();
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
                MfdPresentation.Reset();
                ManeuverScriptLoader.Reset();
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
                // Snapshot fidelity on mission entry; setting changes apply next mission.
                WingFidelity.Begin(Plugin.Settings.Mode.Value);
                WingFormation.Shape = Plugin.Settings.FormationShape.Value;
                WingFormation.SlotSpacing = Plugin.Settings.FormationSpacing.Value;

                // Retry faulted reflexes each mission. Keep factory registrations; per-aircraft states
                // leave with the old roster.
                WingAi.ResetFaults();
                Plugin.LogVerbose("[WingFidelity] mission start - " + WingFidelity.Summary());
            }
            resetForNonPlayableState = false;

            // Use the local player's aircraft as formation leader.
            Wing.SetLeader(GameManager.GetLocalAircraft(out Aircraft local) ? local : null);
            WingSupplyReserve.Tick();
            WingShop.Tick();
            // Advance delivery and flight ownership before UI so panel failures cannot block
            // departures.
            WingShopDelivery.Tick();
            FlushRecruitQueue();

            // Settle RTB before Prune can misclassify an ejected landing pilot as a combat loss.
            WingRecovery.Tick(Wing);
            WingSearchAndRescue.Tick();
            Wing.Prune();
            Selection.Prune(Wing);
            WingTakeover.Tick();
            // Retire completed orders before the single behaviour-arbitration pass.
            Wing.CheckReserves();
            Wing.Tick();

            if (NativeRadialActive)
                WingRadialMenu.Tick();

            HandleRadialInput();
            HandleHotkeys();

            WingInteropPush.Publish(Wing);

            mapLayer.Update();

            WingKillCredit.Tick();
            WingDeliveryTracker.Tick();
            WingMarkers.Tick(Wing);
            WingHud.TickStatusPanel(Wing);
            WmcScreen.Tick(Wing);
            MfdPresentation.Tick();
            WingComms.Tick(Wing);
        }

        /// <summary>Enable the native wheel whenever reflection resolves. The standalone key adds another
        /// entry point and does not disable integration.</summary>
        internal static bool NativeRadialActive => GameAccess.Available;

        private static bool InPlayableState()
        {
            GameState s = GameManager.gameState;
            return s == GameState.SinglePlayer || s == GameState.Multiplayer;
        }

        /// <summary>Log internal notices through verbose diagnostics. Show command state on WMC/map and
        /// pilot events on radio, avoiding duplicate MessageUI boxes.</summary>
        internal void Toast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Plugin.LogVerbose("[Wing] " + message);
        }

        /// <summary>Debug messages permitted in the native game feed.</summary>
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
            catch { /* Use the overlay if the native feed fails. */ }

            toast = message;
            toastUntil = Time.unscaledTime + 3f;
        }

        // Fallback UI.

        private void OnGUI()
        {
            if (!InPlayableState()) return;

            // Recovery and radial UI use uGUI; this IMGUI path only renders fallback debug notices.
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
        StandDown,
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
