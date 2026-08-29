using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Tactical-map input that belongs specifically to WingCommand. Wing selection is
    /// kept separate from the stock selectedIcons/CombatHUD target list.
    /// </summary>
    internal sealed class MapCommandLayer
    {
        private readonly WingRegistry wing;
        private readonly List<Aircraft> recruited = new List<Aircraft>();
        private readonly List<Aircraft> pendingRecruit = new List<Aircraft>();
        private float recruitConfirmationUntil;
        private float pendingRecruitCost;

        private bool pointArmed;
        private WingOrder armedOrder;
        private int armedFrame;

        public bool PointArmed => pointArmed;
        public WingOrder ArmedOrder => armedOrder;
        public string Status => pointArmed
            ? WingOrderCatalog.Label(armedOrder).ToUpperInvariant() + " ARMED - CLICK MAP"
            : pendingRecruit.Count > 0 && Time.unscaledTime <= recruitConfirmationUntil
                ? "CONFIRM ASSIGNMENT: " + pendingRecruit.Count + " AIRCRAFT · " +
                  Mathf.RoundToInt(pendingRecruitCost) + " FUNDS"
                : "Select a row or wing icon; right-click moves; Shift queues points.";

        public MapCommandLayer(WingRegistry wing)
        {
            this.wing = wing;
        }

        public void Update()
        {
            TacticalMapOverlay.Tick(wing);
            if (!DynamicMap.mapMaximized) return;

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            HandlePointOrder(map);
            HandleWaypointInput(map);
        }

        public void ArmPointOrder(WingOrder order)
        {
            if (!WingOrderCatalog.NeedsPoint(order)) return;
            pointArmed = true;
            armedOrder = order;
            armedFrame = Time.frameCount;
            Toast(WingOrderCatalog.Label(order) + " armed - click a point on the map");
        }

        public void CancelPointOrder(bool notify)
        {
            if (!pointArmed) return;
            pointArmed = false;
            if (notify) Toast("Point order cancelled");
        }

        public void Reset()
        {
            pointArmed = false;
            recruited.Clear();
            pendingRecruit.Clear();
            recruitConfirmationUntil = 0f;
            pendingRecruitCost = 0f;
            TacticalMapOverlay.Reset();
        }

        private void HandlePointOrder(DynamicMap map)
        {
            if (!pointArmed) return;

            if (!WmcScreen.TacticalCommandModeActive)
            {
                CancelPointOrder(notify: false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPointOrder(notify: true);
                return;
            }

            // Do not consume the WMC button press that armed the command.
            if (Time.frameCount <= armedFrame + 1 || !Input.GetMouseButtonDown(0)) return;
            if (!map.TryGetCursorCoordinates(out GlobalPosition point)) return;

            WingOrder order = armedOrder;
            pointArmed = false;
            WingCommandManager.Instance?.IssuePointOrder(order, point);
        }

        private void HandleWaypointInput(DynamicMap map)
        {
            if (pointArmed || !WmcScreen.TacticalCommandModeActive ||
                !Input.GetMouseButtonDown(1)) return;

            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null || !manager.Selection.IsExplicit) return;
            if (!map.TryGetCursorCoordinates(out GlobalPosition point)) return;

            List<WingMember> scope = manager.Commands.Scope(wholeWing: false);
            if (scope.Count == 0) return;

            bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int moved = 0;
            foreach (WingMember member in scope)
            {
                if (member == null || !member.Alive) continue;
                member.IssueWaypoint(point, append);
                moved++;
            }

            if (moved > 0)
            {
                manager.Toast((append ? "Queued point for " : "Moving ") + moved +
                              " selected wingman" + (moved == 1 ? "" : "men"));
            }
        }

        /// <summary>
        /// The stock map consumes right-click for ICommandable units. When a tactical wing
        /// scope is explicitly selected, reserve that gesture for aircraft waypoints.
        /// </summary>
        internal static bool ShouldConsumeNativeRightClick()
        {
            if (!Plugin.Config2.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive || !Input.GetMouseButtonDown(1))
                return false;

            WingCommandManager manager = WingCommandManager.Instance;
            return manager != null && manager.Selection.IsExplicit &&
                   manager.Commands.Scope(wholeWing: false).Count > 0;
        }

        /// <summary>
        /// Assign eligible friendly AI aircraft from the stock map selection. The
        /// recruitment transaction performs final eligibility and economy validation.
        /// </summary>
        public void AddSelected()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            if (wing.Leader == null)
            {
                Toast("Not flying - cannot form a wing");
                return;
            }

            if (map.selectedIcons.Count == 0)
            {
                Toast("Nothing selected on the map");
                return;
            }

            recruited.Clear();
            int max = Plugin.Config2.MaxWingSize.Value;

            foreach (MapIcon icon in map.selectedIcons)
            {
                if (wing.Count + recruited.Count >= max) break;
                if (!(icon is UnitMapIcon unitIcon)) continue;
                if (!(unitIcon.unit is Aircraft aircraft)) continue;
                if (aircraft == wing.Leader || aircraft.Player != null) continue;
                if (aircraft.disabled || wing.Contains(aircraft)) continue;
                if (DynamicMap.GetFactionMode(aircraft.NetworkHQ) != FactionMode.Friendly) continue;
                recruited.Add(aircraft);
            }

            if (recruited.Count == 0)
            {
                pendingRecruit.Clear();
                if (wing.Count >= max) Toast("Wing is full");
                else Toast("No eligible friendly AI aircraft selected");
                return;
            }

            float total = 0f;
            for (int i = 0; i < recruited.Count; i++)
                total += WingRecruitment.PriceOf(wing, recruited[i], i);

            if (WingShop.Allocation < total)
            {
                pendingRecruit.Clear();
                Toast("Assignment costs " + Mathf.RoundToInt(total) + ", have " +
                      Mathf.RoundToInt(WingShop.Allocation));
                return;
            }

            bool confirmed = Time.unscaledTime <= recruitConfirmationUntil &&
                             SameAircraft(recruited, pendingRecruit);
            if (!confirmed)
            {
                pendingRecruit.Clear();
                pendingRecruit.AddRange(recruited);
                pendingRecruitCost = total;
                recruitConfirmationUntil = Time.unscaledTime + 5f;
                Toast("Assign " + recruited.Count + " aircraft for " +
                      Mathf.RoundToInt(total) + " - press ASSIGN SELECTED again to confirm");
                return;
            }

            pendingRecruit.Clear();
            recruitConfirmationUntil = 0f;
            int added = 0;
            string lastReason = null;
            foreach (Aircraft aircraft in recruited)
            {
                if (WingRecruitment.TryRecruit(wing, aircraft, out _, out string reason)) added++;
                else lastReason = reason;
            }

            ReleaseSelection(map);

            if (added > 0)
                Toast("Wing: " + added + " aircraft assigned (" + wing.Count + " total)");
            else if (!string.IsNullOrEmpty(lastReason))
                Toast(lastReason);
            else if (wing.Count >= max)
                Toast("Wing is full");
            else
                Toast("No eligible friendly AI aircraft selected");
        }

        private static bool SameAircraft(List<Aircraft> a, List<Aircraft> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private void ReleaseSelection(DynamicMap map)
        {
            if (recruited.Count == 0) return;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;

            foreach (Aircraft aircraft in recruited)
            {
                if (aircraft == null) continue;
                if (flying && hud.GetTargetList().Contains(aircraft)) hud.DeSelectUnit(aircraft);
                else map.DeselectIcon(aircraft);
            }
            recruited.Clear();
        }

        private static void Toast(string message) => WingCommandManager.Instance?.Toast(message);
    }

    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class WingMapWaypointPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !MapCommandLayer.ShouldConsumeNativeRightClick();
        }
    }

    /// <summary>Claim wing-icon clicks only while WMC is explicitly in tactical mode.</summary>
    [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.ClickIcon))]
    internal static class WingMapSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitMapIcon __instance, MapIcon.ClickSource clickSource)
        {
            if (!Plugin.Config2.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive)
                return true;

            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null || !(__instance.unit is Aircraft aircraft)) return true;

            WingMember member = manager.Wing.Find(aircraft);
            if (member == null) return true;

            bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            manager.SelectMember(member, toggle);
            return false;
        }
    }
}
