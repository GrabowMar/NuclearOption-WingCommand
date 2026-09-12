using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Harmony calls prefixes by reflection, so suppress IDE0051 in this file.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Wing-specific tactical-map input with command selection independent of native weapon
    /// targets.</summary>
    internal sealed class MapCommandLayer
    {
        private readonly WingRegistry wing;
        private readonly List<Aircraft> recruited = new List<Aircraft>();
        private readonly List<Aircraft> pendingRecruit = new List<Aircraft>();
        private readonly MapGesture gesture = new MapGesture();
        private float recruitConfirmationUntil;
        private float pendingRecruitCost;

        private bool pointArmed;
        private WingOrder armedOrder;
        private int armedFrame;
        private float moveAltitude;
        private float moveSpeed;

        /// <summary>Requested Move altitude in metres AGL; zero uses airframe defaults.</summary>
        public float MoveAltitude => moveAltitude;
        /// <summary>Requested Move speed fraction; zero selects full speed.</summary>
        public float MoveSpeed => moveSpeed;

        public bool PointArmed => pointArmed;
        public WingOrder ArmedOrder => armedOrder;
        /// <summary>Whether status contains an active notice rather than standing instructions, reserving
        /// WMC space from ROE hints.</summary>
        public bool HasNotice => pointArmed ||
            (pendingRecruit.Count > 0 && Time.unscaledTime <= recruitConfirmationUntil);

        public string Status
        {
            get
            {
                if (pointArmed)
                    return MapOrderPolicy.ArmPrompt(armedOrder);
                if (BoscaliLink.SupportGestureArmed)
                    return "SUPPORT CALL-IN ARMED";
                if (pendingRecruit.Count > 0 && Time.unscaledTime <= recruitConfirmationUntil)
                    return "CONFIRM ASSIGNMENT: " + pendingRecruit.Count + " AIRCRAFT · " +
                           Mathf.RoundToInt(pendingRecruitCost) + " FUNDS";
                return "Right-click moves at " + FormatAltitude(moveAltitude) + " · " +
                       FormatSpeed(moveSpeed) + ". H+/H- height, S+/S- speed. Shift queues.";
            }
        }

        public MapCommandLayer(WingRegistry wing)
        {
            this.wing = wing;
        }

        public void Update()
        {
            TacticalMapOverlay.Tick(wing);
            if (!Plugin.Settings.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive)
            {
                CancelPointOrder(notify: false);
                return;
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            // Apply the armed order on right-click; otherwise issue Move.
            if (pointArmed) HandleArmedOrder(map);
            else HandleWaypointInput(map);
        }

        public void ArmPointOrder(WingOrder order)
        {
            if (!Plugin.Settings.MapCommandEnabled.Value)
            {
                Toast("Map commands are disabled");
                return;
            }
            if (!MapOrderPolicy.ArmsOnMap(order)) return;

            // Reject arming when the map cannot receive the next click, with an actionable reason
            // instead of a transient acknowledgement.
            if (!DynamicMap.mapMaximized)
            {
                Toast("Open the tactical map to place " + WingOrderCatalog.Label(order));
                return;
            }
            if (!WmcScreen.TacticalCommandModeActive)
            {
                Toast("Open WMC TACTICAL to place " + WingOrderCatalog.Label(order));
                return;
            }
            if (SceneSingleton<DynamicMap>.i == null)
            {
                Toast("Tactical map unavailable");
                return;
            }
            if (BoscaliLink.SupportGestureArmed)
            {
                Toast("A Boscali support call-in is armed - cancel it first");
                return;
            }

            pointArmed = true;
            armedOrder = order;
            armedFrame = Time.frameCount;
            gesture.Clear();
            Interop.WingMapMode.GestureArmed = true;
            Toast(MapOrderPolicy.PicksTarget(order)
                ? WingOrderCatalog.Label(order) + " armed - right-click a hostile on the map"
                : WingOrderCatalog.Label(order) + " armed - right-click a point on the map");
        }

        public void CancelPointOrder(bool notify)
        {
            if (!pointArmed) return;
            pointArmed = false;
            gesture.Clear();
            Interop.WingMapMode.GestureArmed = false;
            if (notify) Toast("Order cancelled");
        }

        public void Reset()
        {
            gesture.Clear();
            Interop.WingMapMode.GestureArmed = false;
            pointArmed = false;
            moveAltitude = 0f;
            moveSpeed = 0f;
            recruited.Clear();
            pendingRecruit.Clear();
            recruitConfirmationUntil = 0f;
            pendingRecruitCost = 0f;
            TacticalMapOverlay.Reset();
        }

        private void HandleArmedOrder(DynamicMap map)
        {
            if (!pointArmed) return;

            if (!WmcScreen.TacticalCommandModeActive)
            {
                CancelPointOrder(notify: false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPointOrder(notify: true);
                return;
            }

            // Ignore the arming frame and its immediate successor so a simultaneous right-click cannot
            // place the order. Commit on release, and only when the pointer barely moved: a right-drag
            // pans the map and must not also drop the order at where the drag began.
            if (Time.frameCount <= armedFrame + 1) return;
            if (Input.GetMouseButtonDown(1))
            {
                gesture.NotePointerDown(Input.mousePosition.x, Input.mousePosition.y);
                return;
            }
            if (!Input.GetMouseButtonUp(1) ||
                !gesture.ReleasedAsClick(Input.mousePosition.x, Input.mousePosition.y)) return;
            if (!TryGetMapPointer(map, out GlobalPosition point, out Unit target)) return;

            MapPointerKind pointer = PointerKind(target);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            MapClickIntent intent = MapOrderPolicy.ResolveRightClick(true, armedOrder, pointer, shift);
            WingCommandManager manager = WingCommandManager.Instance;

            switch (intent)
            {
                case MapClickIntent.PlacePoint:
                    manager?.IssuePointOrder(armedOrder, point, append: false);
                    break;
                case MapClickIntent.QueuePlacePoint:
                    manager?.IssuePointOrder(armedOrder, point, append: true);
                    break;
                case MapClickIntent.AttackTarget:
                    manager?.IssueTargetOrder(armedOrder, target, append: false);
                    break;
                case MapClickIntent.QueueAttackTarget:
                    manager?.IssueTargetOrder(armedOrder, target, append: true);
                    break;
                case MapClickIntent.NeedTarget:
                    Toast("Right-click a hostile on the map");
                    break;
            }
        }

        private void HandleWaypointInput(DynamicMap map)
        {
            if (pointArmed || !WmcScreen.TacticalCommandModeActive) return;
            if (BoscaliLink.SupportGestureArmed) return;

            if (Input.GetMouseButtonDown(1))
            {
                gesture.NotePointerDown(Input.mousePosition.x, Input.mousePosition.y);
                return;
            }
            if (!Input.GetMouseButtonUp(1) ||
                !gesture.ReleasedAsClick(Input.mousePosition.x, Input.mousePosition.y)) return;

            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null) return;
            if (!TryGetMapPointer(map, out GlobalPosition point, out _)) return;

            bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            manager.IssueMove(point, append);
        }

        public void StepMoveHeight(int sign)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            bool rotary = WingRegistry.IsRotary(manager?.Wing.Leader);
            moveAltitude = MapOrderPolicy.StepMoveAltitude(moveAltitude, sign, rotary);
            ApplyMoveTuning(altitude: true, speed: false);
            Toast("Move altitude " + FormatAltitude(moveAltitude));
        }

        public void StepMoveSpeed(int sign)
        {
            moveSpeed = MapOrderPolicy.StepMoveSpeed(moveSpeed, sign);
            ApplyMoveTuning(altitude: false, speed: true);
            Toast("Move speed " + FormatSpeed(moveSpeed));
        }

        private void ApplyMoveTuning(bool altitude, bool speed)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null) return;
            foreach (WingMember member in manager.Commands.Scope(wholeWing: false))
            {
                if (member == null || !member.Alive) continue;
                if (member.Order != WingOrder.MoveToPoint && !HasQueuedMove(member)) continue;
                if (altitude) member.SetMoveAltitude(moveAltitude);
                if (speed) member.SetMoveSpeed(moveSpeed);
            }
        }

        private static bool HasQueuedMove(WingMember member)
        {
            IReadOnlyList<WingDirective> route = member.Route;
            for (int i = 0; i < route.Count; i++)
            {
                if (route[i].Order == WingOrder.MoveToPoint) return true;
            }
            return false;
        }

        private static string FormatAltitude(float altitude)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            bool rotary = WingRegistry.IsRotary(manager?.Wing.Leader);
            float metres = MapOrderPolicy.StepMoveAltitude(altitude, 0, rotary);
            return Mathf.RoundToInt(metres) + " m";
        }

        private static string FormatSpeed(float speed)
        {
            float frac = MapOrderPolicy.StepMoveSpeed(speed, 0);
            return Mathf.RoundToInt(frac * 100f) + "%";
        }

        private static MapPointerKind PointerKind(Unit target)
        {
            if (target == null || target.disabled) return MapPointerKind.Empty;
            return DynamicMap.GetFactionMode(target.NetworkHQ) == FactionMode.Enemy
                ? MapPointerKind.Enemy
                : MapPointerKind.Other;
        }

        /// <summary>Resolve the topmost UI target before accepting coordinates. Foreground panels block
        /// map actions, and friendly icons must not expose hostiles beneath them.</summary>
        private static bool TryGetMapPointer(DynamicMap map, out GlobalPosition point, out Unit unit) =>
            TryGetMapPointer(map, out point, out unit, out _);

        internal static bool TryGetMapPointer(DynamicMap map, out GlobalPosition point, out Unit unit,
                                              out bool pointerOverIcon)
        {
            unit = null;
            point = default;
            pointerOverIcon = false;
            if (map == null) return false;
            if (!map.TryGetCursorCoordinates(out point))
            {
                if (Input.GetMouseButtonDown(1)) Plugin.LogAction("map click rejected: outside map rectangle");
                return false;
            }
            EventSystem events = EventSystem.current;
            // Map-rectangle coordinate conversion is authoritative. Without an EventSystem during
            // rebuild, allow valid points; use raycasts only to reject foreground UI or resolve icons.
            if (events == null) return true;

            var pointer = new PointerEventData(events) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);
            GraphicRaycaster mapRaycaster = map.GetComponent<GraphicRaycaster>();

            foreach (RaycastResult hit in hits)
            {
                if (hit.gameObject == null) continue;
                // Transparent map areas can expose HUD/world hits behind the map. Only foreground
                // UI can block a map command; use the same canvas priorities as EventSystem.
                if (!(hit.module is GraphicRaycaster raycaster)) continue;
                if (mapRaycaster != null &&
                    MapSelectionPolicy.IsBehindMap(raycaster.sortOrderPriority, raycaster.renderOrderPriority,
                        mapRaycaster.sortOrderPriority, mapRaycaster.renderOrderPriority)) continue;

                MapIcon icon = hit.gameObject.GetComponentInParent<MapIcon>();
                if (icon == null)
                    icon = hit.gameObject.GetComponentInParent<UnitMapMarker>()?.Icon;
                if (icon != null)
                {
                    pointerOverIcon = true;
                    unit = (icon as UnitMapIcon)?.unit;
                    return true;
                }

                Transform target = hit.gameObject.transform;
                bool overMap = (map.mapBackground != null && target.IsChildOf(map.mapBackground.transform)) ||
                               (map.mapImage != null && target.IsChildOf(map.mapImage.transform));
                if (!overMap && Input.GetMouseButtonDown(1))
                    Plugin.LogAction($"map click rejected: foreground UI {target.name} point={point}");
                return overMap;
            }

            // Accept empty map areas even when noninteractive artwork has no raycast target.
            return true;
        }

        /// <summary>While Tactical is open, reserve native ICommandable right-click for the armed WMC
        /// order or default Move.</summary>
        internal static bool ShouldConsumeNativeRightClick()
        {
            if (!Plugin.Settings.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive)
                return false;
            // Suppress native right-click for the whole press, since WMC now commits its order on
            // release (button-up) after a click-vs-drag check.
            if (!Input.GetMouseButton(1) && !Input.GetMouseButtonUp(1))
                return false;
            if (BoscaliLink.SupportGestureArmed)
                return false;

            WingCommandManager manager = WingCommandManager.Instance;
            return manager != null &&
                   (manager.MapOrderArmed || manager.Commands.Scope(wholeWing: false).Count > 0);
        }

        /// <summary>Recruit eligible aircraft from native map selection; the transaction performs final
        /// eligibility and cost checks.</summary>
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

            float total = SelectedAssignmentCost() ?? 0f;

            if (recruited.Count == 0)
            {
                pendingRecruit.Clear();
                if (!WingRegistry.HasRoom(wing.Count)) Toast("Wing is full");
                else Toast("No eligible friendly AI aircraft selected");
                return;
            }

            if (EconomyFacade.Shop.Allocation < total)
            {
                pendingRecruit.Clear();
                Toast("Assignment costs " + Mathf.RoundToInt(total) + ", have " +
                      Mathf.RoundToInt(EconomyFacade.Shop.Allocation));
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
                if (PersonnelFacade.Recruitment.TryRecruit(wing, aircraft, out _, out string reason)) added++;
                else lastReason = reason;
            }

            ReleaseSelection(map);

            if (added > 0)
                Toast("Wing: " + added + " aircraft assigned (" + wing.Count + " total)");
            else if (!string.IsNullOrEmpty(lastReason))
                Toast(lastReason);
            else if (!WingRegistry.HasRoom(wing.Count))
                Toast("Wing is full");
            else
                Toast("No eligible friendly AI aircraft selected");
        }

        internal float? SelectedAssignmentCost()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || wing.Leader == null) return null;

            recruited.Clear();

            foreach (MapIcon icon in map.selectedIcons)
            {
                if (!WingRegistry.HasRoom(wing.Count + recruited.Count)) break;
                if (!(icon is UnitMapIcon unitIcon)) continue;
                if (!(unitIcon.unit is Aircraft aircraft)) continue;
                if (aircraft == wing.Leader || aircraft.Player != null) continue;
                if (aircraft.disabled || wing.Contains(aircraft)) continue;
                if (DynamicMap.GetFactionMode(aircraft.NetworkHQ) != FactionMode.Friendly) continue;
                recruited.Add(aircraft);
            }

            float total = 0f;
            for (int i = 0; i < recruited.Count; i++)
                total += PersonnelFacade.Recruitment.PriceOf(recruited[i]);

            return recruited.Count > 0 ? (float?)total : null;
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

    /// <summary>Intercept wing-icon clicks only in explicit WMC Tactical mode.</summary>
    [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.ClickIcon))]
    internal static class WingMapSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitMapIcon __instance, MapIcon.ClickSource clickSource)
        {
            if (!Plugin.Settings.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive)
                return true;

            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null) return true;

            if (!(__instance.unit is Aircraft aircraft)) return true;

            WingMember member = manager.Wing.Find(aircraft);
            if (member == null) return true;

            // Reject controller selection through foreground panels; native proximity search does not
            // raycast UI.
            if (!MapCommandLayer.TryGetMapPointer(SceneSingleton<DynamicMap>.i, out _, out _,
                                                  out bool pointerOverIcon))
                return false;

            // Avoid toggling twice when Rewired press and EventSystem release share the left mouse
            // binding.
            bool mouseGestureActive = Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) ||
                                      Input.GetMouseButtonUp(0);
            if (MapSelectionPolicy.DeferToMouseClick(clickSource == MapIcon.ClickSource.Controller,
                                                    mouseGestureActive, pointerOverIcon))
                return false;

            bool toggle = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            manager.SelectMember(member, toggle);
            return false;
        }
    }
}
