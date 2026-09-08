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
                WingHost.Current.IsSurfaceVehicle ? "ON STATION" : "REJOIN",
                WingAction.Rejoin, "rejoin"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Attack).ToUpperInvariant(),
                "PRIORITY LOCK",
                WingAction.AttackMyTarget, "attack"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.Engage).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "CLOSE AIR SUPPORT" : "SEARCH & DESTROY",
                WingAction.Engage, "engage"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.FallBack).ToUpperInvariant(),
                WingHost.Current.IsSurfaceVehicle ? "BREAK CONTACT" : "DEFENSIVE BREAK",
                WingAction.FallBack, "fallback"),
            new RadialSlice(WingOrderCatalog.Label(WingOrder.FireForEffect).ToUpperInvariant(),
                "FULL SALVO ON LOCK",
                WingAction.FireForEffect, "attack"),
            new RadialSlice("CYCLE ROE", "RULES OF ENGAGEMENT", WingAction.CycleRoe, "posture"),
        };

        private float lastSliceSelectTime;

     /// <summary>Handle the optional standalone wheel key independently of native radial
     /// integration.</summary>
        private void HandleRadialInput()
        {
            KeyCode key = Plugin.Settings.RadialKey.Value;
            if (key == KeyCode.None)
            {
                if (radialOpen) CloseRadial(apply: false);
                return;
            }

            // Right-click cancels the open radial.
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

     /// <summary>Integrate Rewired look axes with native decay; captured flight cursors do not move
     /// Input.mousePosition.</summary>
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

     /// <summary>Native wheel angles: top is index 0, increasing clockwise.</summary>
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

            // Retain selection for 1.2 seconds in the deadzone after mouse motion stops.
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
    }
}
