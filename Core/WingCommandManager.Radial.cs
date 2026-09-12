using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        // Standalone radial state.
        private bool radialOpen;
        private Vector2 radialDelta;
        private int hoveredSlice = -1;

        private static readonly RadialSlice[] liveSlices = new RadialSlice[6];

        private RadialSlice[] GetCurrentSlices()
        {
            List<Unit> targets = CurrentPlayerTargets();
            bool hasTarget = targets != null && targets.Count > 0;
            string targetName = hasTarget ? targets[0].unitName : null;

            string formationName = FormationShapes.Pretty(WingFormation.Shape).ToUpperInvariant();
            liveSlices[0] = new RadialSlice(
                WingOrderCatalog.Label(WingOrder.Formation).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "RETURN TO YOUR STATION" : $"RETURN TO {formationName}",
                WingAction.Rejoin, "rejoin", available: true);

            liveSlices[1] = new RadialSlice(
                WingOrderCatalog.Label(WingOrder.Attack).ToUpperInvariant(),
                RadialSelection.FormatTargetSubtitle(hasTarget, targetName, "NO TARGET DESIGNATED"),
                WingAction.AttackMyTarget, "attack", available: hasTarget, requiresTarget: true);

            liveSlices[2] = new RadialSlice(
                WingOrderCatalog.Label(WingOrder.Engage).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "PROVIDE CLOSE AIR SUPPORT" : "SEARCH FOR AND ENGAGE HOSTILES",
                WingAction.Engage, "engage", available: true);

            liveSlices[3] = new RadialSlice(
                WingOrderCatalog.Label(WingOrder.FallBack).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "BREAK CONTACT" : "BREAK OFF AND FLY DEFENSIVELY",
                WingAction.FallBack, "fallback", available: true);

            liveSlices[4] = new RadialSlice(
                WingOrderCatalog.Label(WingOrder.FireForEffect).ToUpperInvariant(),
                RadialSelection.FormatTargetSubtitle(hasTarget, targetName, "NO TARGET DESIGNATED"),
                WingAction.FireForEffect, "attack", available: hasTarget, requiresTarget: true);

            liveSlices[5] = new RadialSlice(
                "CYCLE ROE",
                Wing != null
                    ? RadialSelection.FormatRoeTransition(CombatFacade.Roe.Label(Wing.Roe), CombatFacade.Roe.Label(CombatFacade.Roe.Next(Wing.Roe)))
                    : "RULES OF ENGAGEMENT",
                WingAction.CycleRoe, "posture", available: true);

            return liveSlices;
        }

        private Vector2 radialMousePosition;

        /// <summary>Handle the optional standalone wheel key independently of native radial
        /// integration.</summary>
        private void HandleRadialInput()
        {
            KeyCode key = Plugin.Settings.RadialKey.Value;
            if (key == KeyCode.None || WingKeyboardGuard.Captured || Wing.Leader == null || !Application.isFocused)
            {
                if (radialOpen) CloseRadial(apply: false);
                return;
            }

            // Right-click cancels the open radial.
            if (radialOpen && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
            {
                CloseRadial(apply: false);
                return;
            }

            if (Input.GetKeyDown(key) && Wing.Leader != null)
            {
                radialOpen = true;
                radialDelta = Vector2.zero;
                hoveredSlice = -1;
                radialMousePosition = Input.mousePosition;
            }

            if (radialOpen)
            {
                AccumulateRadialDelta();
                RadialSlice[] currentSlices = GetCurrentSlices();
                int nextHovered = RadialSelection.FromPointer(radialDelta.x, radialDelta.y, hoveredSlice, currentSlices.Length);
                if (nextHovered != hoveredSlice)
                {
                    hoveredSlice = nextHovered;
                    if (hoveredSlice >= 0)
                        WingRadioAudio.Play(WingRadioAudio.Earcon.RadialTick);
                }
                if (Input.GetKeyUp(key))
                {
                    CloseRadial(apply: true);
                    return;
                }
                WingRadialOverlay.Show(currentSlices, hoveredSlice, Wing, radialDelta);
            }
            else
            {
                WingRadialOverlay.Hide();
            }
        }

        /// <summary>A persistent virtual pointer, independent of camera-look axes and frame decay.</summary>
        private void AccumulateRadialDelta()
        {
            Vector2 position = Input.mousePosition;
            Vector2 mouse = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f
                : (position - radialMousePosition) * (1080f / Mathf.Max(1, Screen.height));
            radialMousePosition = position;
            Rewired.Player player = GameManager.playerInput;
            if (player != null)
            {
                Vector2 stick = new Vector2(player.GetAxis("Radial Menu Horizontal"), player.GetAxis("Radial Menu Vertical"));
                if (stick.sqrMagnitude > 0.16f && mouse.sqrMagnitude < 0.01f)
                {
                    radialDelta = Vector2.ClampMagnitude(stick, 1f) * RadialSelection.PointerRadius;
                    return;
                }
            }
            radialDelta = Vector2.ClampMagnitude(radialDelta + mouse, RadialSelection.PointerRadius);
        }
        private void HandleHotkeys()
        {
            if (WingKeyboardGuard.Captured) return;
            if (WmcScreen.TacticalFlightExpanded &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                for (int i = 0; i < FlightGroups<WingMember>.Count; i++)
                {
                    if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) continue;
                    if (!Selection.Groups.Exists(i)) return;
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        SaveFlightGroup(i, Selection.Groups.Name(i));
                    else RecallFlightGroup(i);
                    return;
                }
            }
            if (Wing.Count == 0) return;

            if (Plugin.Settings.QuickRejoinKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickRejoinKey.Value))
                Execute(WingAction.Rejoin);

            if (Plugin.Settings.QuickEngageKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickEngageKey.Value))
                Execute(WingAction.Engage);

            if (Plugin.Settings.QuickDisengageKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickDisengageKey.Value))
                Execute(WingAction.FallBack);

            if (Plugin.Settings.QuickAttackKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickAttackKey.Value))
                Execute(WingAction.AttackMyTarget);

            if (Plugin.Settings.QuickBreakKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.QuickBreakKey.Value))
                Execute(WingAction.DefensiveBreak);

            if (Plugin.Settings.CycleRoeKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.CycleRoeKey.Value))
                Execute(WingAction.CycleRoe);
        }

        private void CloseRadial(bool apply)
        {
            if (apply && hoveredSlice >= 0)
            {
                RadialSlice[] currentSlices = GetCurrentSlices();
                if (hoveredSlice < currentSlices.Length)
                {
                    RadialSlice chosen = currentSlices[hoveredSlice];
                    if (chosen.Available)
                    {
                        WingRadioAudio.Transmission();
                        Execute(chosen.Action);
                    }
                    else
                    {
                        WingRadioAudio.Play(WingRadioAudio.Earcon.Unable);
                        if (chosen.RequiresTarget)
                            Toast("Cannot order: No target locked on HUD");
                        else
                            Toast("Order unavailable");
                    }
                }
            }

            radialOpen = false;
            hoveredSlice = -1;
            radialMousePosition = Input.mousePosition;
            WingRadialOverlay.Hide();
        }
    }
}
