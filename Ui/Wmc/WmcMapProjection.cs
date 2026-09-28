using System;
using System.Reflection;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Where a world point is drawn on the maximized map, in the overlay layer's local space. Boscali Summer's 3D map
    /// relief (on by default) renders the map through its own tilted camera, moves the game's icons there and answers the cursor
    /// from that camera; drawn flat (world × mapDisplayFactor) WMC's legs and rings landed kilometres away from the icons and the
    /// clicked point (in-game 2026-09-28: a MOVE leg ran off toward the map's corner). While the relief draws, points go through
    /// its projection; otherwise, and when Boscali is absent, they stay flat.</summary>
    internal static class WmcMapProjection
    {
        private const string ReliefType = "BoscaliSummer.Features.Command.Presentation.MapUi.MfdTerrainRelief";

        private delegate bool ProjectFn(float worldX, float worldZ, Rect rect, out Vector2 point);

        private static bool looked;
        private static Func<bool> isDrawing;
        private static Func<int> revision;
        private static ProjectFn project;

        /// <summary>The relief is drawing now: points are projected, and they move when its camera does.</summary>
        public static bool Relief
        {
            get
            {
                if (!looked) Look();
                if (isDrawing == null) return false;
                try
                {
                    return isDrawing();
                }
                catch (Exception)
                {
                    isDrawing = null;
                    return false;
                }
            }
        }

        /// <summary>Changes whenever the relief's camera moved (orbit, pan, zoom); 0 without the relief.</summary>
        public static int Revision => Relief && revision != null ? revision() : 0;

        /// <summary><paramref name="x"/>, <paramref name="z"/> (world metres) in <paramref name="layer"/>'s local space.</summary>
        public static Vector3 At(DynamicMap map, Transform layer, float x, float z)
        {
            float f = map.mapDisplayFactor;
            if (!Relief || project == null || map.mapImage == null) return new Vector3(x * f, z * f, 0f);
            var image = (RectTransform)map.mapImage.transform;
            if (!project(x, z, image.rect, out Vector2 p)) return new Vector3(x * f, z * f, 0f);
            Vector3 local = layer.InverseTransformPoint(image.TransformPoint(new Vector3(p.x, p.y, 0f)));
            return new Vector3(local.x, local.y, 0f);
        }

        // ponytail: reflection into Boscali Summer's internal relief (both mods are the user's); a shared NOAvionics map-projection
        // hook replaces it if the relief is ever renamed. A miss logs once and leaves the flat map.
        private static void Look()
        {
            looked = true;
            try
            {
                Type t = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "BoscaliSummer") continue;
                    t = a.GetType(ReliefType, false);
                    break;
                }
                if (t == null) return;
                const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo drawing = t.GetProperty("IsDrawing", any)?.GetGetMethod(true);
                MethodInfo rev = t.GetProperty("ViewRevision", any)?.GetGetMethod(true);
                MethodInfo proj = t.GetMethod("TryProject", any, null,
                    new[] { typeof(float), typeof(float), typeof(Rect), typeof(Vector2).MakeByRefType() }, null);
                if (drawing == null || proj == null)
                {
                    Plugin.Logger.LogWarning("[WMC] Boscali Summer's map relief has no projection WMC knows; its routes stay on the flat map");
                    return;
                }
                isDrawing = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), drawing);
                project = (ProjectFn)Delegate.CreateDelegate(typeof(ProjectFn), proj);
                if (rev != null) revision = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), rev);
                Plugin.LogVerbose("[WMC] routes follow Boscali Summer's 3D map relief");
            }
            catch (Exception e)
            {
                isDrawing = null;
                project = null;
                Plugin.Logger.LogWarning("[WMC] could not read Boscali Summer's map relief (" + e.Message + "); routes stay on the flat map");
            }
        }
    }
}
