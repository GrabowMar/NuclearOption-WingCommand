using System.Collections.Generic;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.EventSystems;

// Harmony invokes patch Prefix methods by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

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
        private float moveAltitude;
        private readonly MapPointGesture pointGesture = new MapPointGesture();

        /// <summary>Commanded Move height, metres AGL. Zero means each airframe's default.</summary>
        public float MoveAltitude => moveAltitude;

        public bool PointArmed => pointArmed;
        public WingOrder ArmedOrder => armedOrder;
        /// <summary>
        /// Left-click still selects wing icons while an order is armed. Placement moved to
        /// right-click, so the held left gesture is only consumed when a leftover press
        /// actually belongs to this layer.
        /// </summary>
        internal bool ConsumesIconClick => pointGesture.ConsumesClick(Time.frameCount);

        /// <summary>
        /// True while <see cref="Status"/> is reporting something rather than repeating the
        /// standing instructions. The WMC status line uses it to decide whether that line is
        /// free to explain the current rules of engagement instead.
        /// </summary>
        public bool HasNotice => pointArmed ||
            (pendingRecruit.Count > 0 && Time.unscaledTime <= recruitConfirmationUntil);

        public string Status
        {
            get
            {
                if (pointArmed)
                    return MapOrderPolicy.ArmPrompt(armedOrder);
                if (MapPicker.IsBusy && !MapPicker.IsOwner(MapPicker.WingPoint))
                    return MapPicker.Prompt ?? "MAP BUSY";
                if (pendingRecruit.Count > 0 && Time.unscaledTime <= recruitConfirmationUntil)
                    return "CONFIRM ASSIGNMENT: " + pendingRecruit.Count + " AIRCRAFT · " +
                           Mathf.RoundToInt(pendingRecruitCost) + " FUNDS";
                return "Right-click moves at " + FormatAltitude(moveAltitude) +
                       ". Alt+scroll altitude. Shift queues. Left-click an order to arm it.";
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
                pointGesture.Reset();
                return;
            }

            pointGesture.Update(Time.frameCount, Input.GetMouseButton(0));
            HandleMoveAltitudeScroll();

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            // An armed order owns the next right-click. Unarmed, that click is a move.
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

            // Do not acknowledge an arm we cannot keep. Before this guard, pressing Hold
            // while the map or its Tactical page was closing set pointArmed for a frame,
            // then Update silently cleared it. That reads exactly like a map click was
            // ignored. A map command is useful only while this layer can receive the
            // follow-up click, so reject it at the boundary with an actionable reason.
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

            string prompt = MapOrderPolicy.ArmPrompt(order);
            if (!MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureRight, prompt))
            {
                Toast(MapPicker.Prompt ?? "Map is busy");
                return;
            }

            pointArmed = true;
            armedOrder = order;
            armedFrame = Time.frameCount;
            Toast(MapOrderPolicy.PicksTarget(order)
                ? WingOrderCatalog.Label(order) + " armed - right-click a hostile on the map"
                : WingOrderCatalog.Label(order) + " armed - right-click a point on the map");
        }

        public void CancelPointOrder(bool notify)
        {
            if (!pointArmed) return;
            pointArmed = false;
            MapPicker.Disarm(MapPicker.WingPoint);
            if (notify) Toast("Order cancelled");
        }

        public void Reset()
        {
            MapPicker.Disarm(MapPicker.WingPoint);
            pointArmed = false;
            moveAltitude = 0f;
            pointGesture.Reset();
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

            // The WMC button that armed this order is a left-click. The follow-up is a
            // right-click, so the arming press cannot also place the order. The one-frame
            // skip still drops a right-click that lands in the same frame as the arm.
            if (Time.frameCount <= armedFrame + 1 || !Input.GetMouseButtonDown(1)) return;
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
                    manager?.AttackUnit(target, append: false);
                    break;
                case MapClickIntent.QueueAttackTarget:
                    manager?.AttackUnit(target, append: true);
                    break;
                case MapClickIntent.NeedTarget:
                    Toast("Right-click a hostile on the map");
                    break;
            }
        }

        private void HandleWaypointInput(DynamicMap map)
        {
            if (pointArmed || !WmcScreen.TacticalCommandModeActive ||
                !Input.GetMouseButtonDown(1)) return;
            if (MapPicker.IsBusy && !MapPicker.IsOwner(MapPicker.WingPoint)) return;

            WingCommandManager manager = WingCommandManager.Instance;
            if (manager == null) return;
            if (!TryGetMapPointer(map, out GlobalPosition point, out _)) return;

            bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            manager.IssueMove(point, append);
        }

        private void HandleMoveAltitudeScroll()
        {
            if (!MapOrderPolicy.IsMoveTool(pointArmed)) return;
            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;

            int sign = MapOrderPolicy.ScrollSign(Input.mouseScrollDelta.y);
            if (sign == 0) return;

            WingCommandManager manager = WingCommandManager.Instance;
            Aircraft leader = manager?.Wing.Leader;
            bool rotary = WingRegistry.IsRotary(leader);
            moveAltitude = MapOrderPolicy.StepMoveAltitude(moveAltitude, sign, rotary);

            if (manager != null)
            {
                foreach (WingMember member in manager.Commands.Scope(wholeWing: false))
                {
                    if (member == null || !member.Alive) continue;
                    if (member.Order != WingOrder.MoveToPoint && !HasQueuedMove(member)) continue;
                    member.SetMoveAltitude(moveAltitude);
                }
            }

            Toast("Move altitude " + FormatAltitude(moveAltitude));
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

        internal static bool SuppressMapZoom
        {
            get
            {
                if (!Plugin.Settings.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                    !WmcScreen.TacticalCommandModeActive) return false;
                if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
                    return false;
                WingCommandManager manager = WingCommandManager.Instance;
                return manager != null && MapOrderPolicy.IsMoveTool(manager.MapOrderArmed);
            }
        }

        private static string FormatAltitude(float altitude)
        {
            WingCommandManager manager = WingCommandManager.Instance;
            bool rotary = WingRegistry.IsRotary(manager?.Wing.Leader);
            float metres = MapOrderPolicy.StepMoveAltitude(altitude, 0, rotary);
            return Mathf.RoundToInt(metres) + " m";
        }

        private static MapPointerKind PointerKind(Unit target)
        {
            if (target == null || target.disabled) return MapPointerKind.Empty;
            return DynamicMap.GetFactionMode(target.NetworkHQ) == FactionMode.Enemy
                ? MapPointerKind.Enemy
                : MapPointerKind.Other;
        }

        /// <summary>
        /// Resolve the foremost pointer target before accepting a map point. The native
        /// coordinate helper tests only the map rectangle, so it also succeeds behind MFD
        /// controls. A foreground panel blocks the click; a friendly icon cannot expose an
        /// enemy underneath it.
        /// </summary>
        private static bool TryGetMapPointer(DynamicMap map, out GlobalPosition point, out Unit unit) =>
            TryGetMapPointer(map, out point, out unit, out _);

        internal static bool TryGetMapPointer(DynamicMap map, out GlobalPosition point, out Unit unit,
                                              out bool pointerOverIcon)
        {
            unit = null;
            point = default;
            pointerOverIcon = false;
            if (map == null) return false;
            if (!map.TryGetCursorCoordinates(out point)) return false;
            EventSystem events = EventSystem.current;
            // The coordinate conversion above is the authoritative map hit-test. An
            // EventSystem is only needed to discover foreground UI and map icons; it can
            // be absent for a frame while the tactical display is rebuilding. In that
            // window a valid Hold/S&D point must still be placeable rather than appearing
            // to eat the click.
            if (events == null) return true;

            var pointer = new PointerEventData(events) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);

            foreach (RaycastResult hit in hits)
            {
                if (hit.gameObject == null) continue;

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
                return (map.mapBackground != null && target == map.mapBackground.transform) ||
                       (map.mapImage != null && target.IsChildOf(map.mapImage.transform));
            }

            // Some map artwork does not receive raycasts. The rectangle test above still
            // permits an empty map point when no interactive foreground target was hit.
            return true;
        }

        /// <summary>
        /// The stock map consumes right-click for ICommandable units. While Tactical is
        /// open, that gesture belongs to the armed WMC order, or to a move if none is armed.
        /// </summary>
        internal static bool ShouldConsumeNativeRightClick()
        {
            if (!Plugin.Settings.MapCommandEnabled.Value || !DynamicMap.mapMaximized ||
                !WmcScreen.TacticalCommandModeActive || !Input.GetMouseButtonDown(1))
                return false;
            if (MapPicker.IsBusy && !MapPicker.IsOwner(MapPicker.WingPoint))
                return false;

            WingCommandManager manager = WingCommandManager.Instance;
            return manager != null &&
                   (manager.MapOrderArmed || manager.Commands.Scope(wholeWing: false).Count > 0);
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

            if (recruited.Count == 0)
            {
                pendingRecruit.Clear();
                if (!WingRegistry.HasRoom(wing.Count)) Toast("Wing is full");
                else Toast("No eligible friendly AI aircraft selected");
                return;
            }

            float total = 0f;
            for (int i = 0; i < recruited.Count; i++)
                total += WingRecruitment.PriceOf(recruited[i]);

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
            else if (!WingRegistry.HasRoom(wing.Count))
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

    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.SetZoomLevel))]
    internal static class WingMapAltitudeZoomPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !MapCommandLayer.SuppressMapZoom;
    }

    /// <summary>Claim wing-icon clicks only while WMC is explicitly in tactical mode.</summary>
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

            // The EventSystem click runs on release, which may be several frames after
            // the point was placed. The entire held gesture belongs to the point command.
            if (manager.MapConsumesIconClick) return false;
            if (!(__instance.unit is Aircraft aircraft)) return true;

            WingMember member = manager.Wing.Find(aircraft);
            if (member == null) return true;

            // Native controller selection searches near the cursor without raycasting
            // foreground UI. A WMC row click must not also select a plane behind the panel.
            if (!MapCommandLayer.TryGetMapPointer(SceneSingleton<DynamicMap>.i, out _, out _,
                                                  out bool pointerOverIcon))
                return false;

            // Rewired Select can share the mouse binding. Its controller-source call on
            // press and the EventSystem's mouse call on release must not toggle twice.
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
