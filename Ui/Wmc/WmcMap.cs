using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>CENTER, FIT and FOLLOW on the game's maximized map (spec bezel v2 §6; critic §14.12). A request waits for the
    /// game's own MapControls (the patch applies it first thing), which then draws the map from the offsets in the same frame:
    /// nothing flickers about the mouse, and the view stays where it was put — the game stops following once the offset is not
    /// zero (the old SetMapTarget glide snapped back when it ended). With the private offsets missing (a game update), CENTER
    /// falls back to the glide and FIT does nothing, with one warning.</summary>
    internal static class WmcMap
    {
        private static AccessTools.FieldRef<DynamicMap, Vector2> positionOffset, stationaryOffset;
        private static bool resolved, usable, pending, hasZoom, follow, warned;
        private static float atX, atZ, zoom;

        /// <summary>FIT and a steady CENTER are available (the game's view fields were found).</summary>
        public static bool Usable
        {
            get
            {
                Resolve();
                return usable;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            try
            {
                positionOffset = AccessTools.FieldRefAccess<DynamicMap, Vector2>("positionOffset");
                stationaryOffset = AccessTools.FieldRefAccess<DynamicMap, Vector2>("stationaryOffset");
                usable = true;
            }
            catch (System.Exception e)
            {
                usable = false;
                Plugin.Logger.LogWarning("[WMC] map view fields not found (" + e.Message + "): FIT is off and CENTER glides.");
            }
        }

        public static void Center(Unit unit)
        {
            if (unit == null) return;
            GlobalPosition g = unit.GlobalPosition();
            Center(g.x, g.z);
        }

        /// <summary>Puts world point (<paramref name="x"/>, <paramref name="z"/>) at the view's centre; the zoom stays.</summary>
        public static void Center(float x, float z)
        {
            if (!Usable)
            {
                SceneSingleton<DynamicMap>.i?.SetMapTarget(new GlobalPosition(x, 0f, z));
                return;
            }
            atX = x;
            atZ = z;
            follow = false;
            hasZoom = false;
            pending = true;
        }

        /// <summary>Frames <paramref name="box"/> (8 km at least, 15 % margin).</summary>
        public static void Fit(in MapBox box)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (!Usable || !box.Any || map == null || map.mapBackground == null) return;
            Rect r = map.mapBackground.rectTransform.rect;
            // ponytail: the bezel covers part of the map; three quarters of the shorter side is what shows beside it. Measure the
            // panel's side and offset the centre if a wing lands under the bezel.
            float visible = Mathf.Min(r.width, r.height) * 0.75f;
            zoom = MapFit.Zoom(box.Span, visible, map.mapDisplayFactor);
            atX = box.CenterX;
            atZ = box.CenterZ;
            follow = false;
            hasZoom = true;
            pending = true;
        }

        /// <summary>The whole theatre: the game's widest zoom, centred.</summary>
        public static void All()
        {
            if (!Usable) return;
            zoom = MapFit.MinZoom;
            atX = atZ = 0f;
            follow = false;
            hasZoom = true;
            pending = true;
        }

        /// <summary>Back to following you (the game's own view).</summary>
        public static void Follow()
        {
            if (!Usable) return;
            follow = true;
            hasZoom = false;
            pending = true;
        }

        /// <summary>The view follows you (no offset of its own).</summary>
        public static bool Following
        {
            get
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return !Usable || map == null || positionOffset(map) == Vector2.zero;
            }
        }

        /// <summary>From the MapControls patch, just before the game draws the view from its offsets.</summary>
        internal static void Apply(DynamicMap map)
        {
            if (!pending || map == null) return;
            pending = false;
            try
            {
                if (hasZoom) map.SetZoomLevel(zoom);
                if (follow) positionOffset(map) = Vector2.zero;
                else
                {
                    Vector2 st = stationaryOffset(map);
                    var (ox, oy) = MapFit.Offset(atX, atZ, map.mapDisplayFactor, st.x, st.y);
                    positionOffset(map) = new Vector2(ox, oy);
                }
                WmcPanel.Instance?.Overlay.Dirty();
            }
            catch (System.Exception e)
            {
                if (warned) return;
                warned = true;
                Plugin.Logger.LogWarning("[WMC] could not move the map: " + e.Message);
            }
        }
    }
}
