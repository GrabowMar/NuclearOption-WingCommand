using UnityEngine;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        // Standalone radial state.
        private bool radialOpen;
        private Vector2 radialDelta;
        private int hoveredSlice = -1;

        private static RadialSlice[] slices;
        private static int slicesRevision = -1;

        /// <summary>Six overlay sectors, rebuilt on WingHost.Revision changes so host-specific order
        /// labels stay current.</summary>
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
                WingHost.Current.IsSurfaceVehicle ? "RETURN TO YOUR STATION" : "RETURN TO FORMATION",
                WingAction.Rejoin, "rejoin"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Attack).ToUpperInvariant(),
                "ATTACK YOUR LOCKED TARGET",
                WingAction.AttackMyTarget, "attack"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Engage).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "PROVIDE CLOSE AIR SUPPORT" : "SEARCH FOR AND ENGAGE HOSTILES",
                WingAction.Engage, "engage"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.FallBack).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "BREAK CONTACT" : "BREAK OFF AND FLY DEFENSIVELY",
                WingAction.FallBack, "fallback"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.FireForEffect).ToUpperInvariant(),
                "FIRE A FULL SALVO AT YOUR LOCKED TARGET",
                WingAction.FireForEffect, "attack"),
            new RadialSlice("CYCLE ROE", "RULES OF ENGAGEMENT", WingAction.CycleRoe, "posture"),
        };

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
                hoveredSlice = RadialSelection.FromPointer(radialDelta.x, radialDelta.y, hoveredSlice, Slices.Length);
                if (Input.GetKeyUp(key))
                {
                    CloseRadial(apply: true);
                    return;
                }
                WingRadialOverlay.Show(Slices, hoveredSlice, Wing, radialDelta);
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

            if (Plugin.Settings.CycleRoeKey.Value != KeyCode.None &&
                Input.GetKeyDown(Plugin.Settings.CycleRoeKey.Value))
                Execute(WingAction.CycleRoe);
        }

        private void CloseRadial(bool apply)
        {
            if (apply && hoveredSlice >= 0 && hoveredSlice < Slices.Length)
            {
                WingRadioAudio.Transmission();
                Execute(Slices[hoveredSlice].Action);
            }

            radialOpen = false;
            hoveredSlice = -1;
            radialMousePosition = Input.mousePosition;
            WingRadialOverlay.Hide();
        }
    }
}
