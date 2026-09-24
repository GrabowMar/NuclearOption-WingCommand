using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>What the cursor is over on the maximized map (the 0.9 map layer's rule, spec WMC program §5): a point inside
    /// the map rectangle, the unit of a map icon on top, and never a click through a foreground panel such as the WMC
    /// bezel.</summary>
    internal static class WmcMapPointer
    {
        private static readonly List<RaycastResult> hits = new List<RaycastResult>();

        public static bool TryGet(DynamicMap map, out GlobalPosition point, out Unit unit, out bool overIcon)
        {
            unit = null;
            point = default;
            overIcon = false;
            if (map == null || !map.TryGetCursorCoordinates(out point)) return false;
            EventSystem events = EventSystem.current;
            // Without an EventSystem (a rebuild) the map rectangle is authoritative.
            if (events == null) return true;
            var pointer = new PointerEventData(events) { position = Input.mousePosition };
            hits.Clear();
            events.RaycastAll(pointer, hits);
            GraphicRaycaster mapRaycaster = map.GetComponent<GraphicRaycaster>();
            foreach (RaycastResult hit in hits)
            {
                if (hit.gameObject == null || !(hit.module is GraphicRaycaster raycaster)) continue;
                // Transparent map areas expose HUD hits behind the map; only what is in front can block.
                if (mapRaycaster != null && MapSelectionPolicy.IsBehindMap(raycaster.sortOrderPriority, raycaster.renderOrderPriority,
                        mapRaycaster.sortOrderPriority, mapRaycaster.renderOrderPriority)) continue;
                MapIcon icon = hit.gameObject.GetComponentInParent<MapIcon>();
                if (icon == null) icon = hit.gameObject.GetComponentInParent<UnitMapMarker>()?.Icon;
                if (icon != null)
                {
                    overIcon = true;
                    unit = (icon as UnitMapIcon)?.unit;
                    hits.Clear();
                    return true;
                }
                Transform target = hit.gameObject.transform;
                bool overMap = (map.mapBackground != null && target.IsChildOf(map.mapBackground.transform)) ||
                               (map.mapImage != null && target.IsChildOf(map.mapImage.transform));
                hits.Clear();
                if (overMap) unit = NearestSelected(map, out overIcon);
                return overMap;
            }
            hits.Clear();
            // Empty map areas count even when their artwork is no raycast target. The game turns hit-testing off on the icons
            // it has selected (the player's targets): those are found by distance instead (review P4 I4).
            unit = NearestSelected(map, out overIcon);
            return true;
        }

        private const float SelectedPickPixels = 24f;

        private static Unit NearestSelected(DynamicMap map, out bool overIcon)
        {
            overIcon = false;
            Unit best = null;
            float bestSq = SelectedPickPixels * SelectedPickPixels;
            Vector2 mouse = Input.mousePosition;
            foreach (MapIcon icon in map.selectedIcons)
            {
                if (!(icon is UnitMapIcon u) || u.unit == null || u.unit.disabled || !u.gameObject.activeInHierarchy) continue;
                float d = ((Vector2)u.transform.position - mouse).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                best = u.unit;
            }
            overIcon = best != null;
            return best;
        }
    }
}
