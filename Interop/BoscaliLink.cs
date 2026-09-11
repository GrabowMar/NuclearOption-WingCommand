using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace WingCommand
{
    /// <summary>Optional read-only link to Boscali Summer. Wing Command runs fully without it.
    /// When Boscali <em>is</em> loaded, this reflects its public
    /// <c>BoscaliSummer.Interop.SupportMapMode</c> by assembly-qualified type name — no
    /// assembly reference, no shared DLL, no shared AppDomain channel — so a wing point-order
    /// and a Boscali support call-in don't both fire on one right-click.</summary>
    internal static class BoscaliLink
    {
        private const string BoscaliGuid = "com.marci.boscalisummer";
        private const string SupportMapModeTypeName = "BoscaliSummer.Interop.SupportMapMode, BoscaliSummer";

        private static bool resolved;
        private static PropertyInfo gestureArmedProperty;

        /// <summary>Whether the Boscali Summer plugin is present in this session.</summary>
        public static bool Available => Chainloader.PluginInfos.ContainsKey(BoscaliGuid);

        /// <summary>Whether Boscali has a support call-in armed on the tactical map. False
        /// whenever Boscali is absent or its API could not be resolved.</summary>
        public static bool SupportGestureArmed
        {
            get
            {
                PropertyInfo prop = Resolve();
                if (prop == null) return false;
                try { return prop.GetValue(null) is bool armed && armed; }
                catch (Exception e) { Fail(e); return false; }
            }
        }

        /// <summary>Resolve the interop member once, the first time Boscali is seen loaded.
        /// Until then every call is a cheap dictionary lookup that returns null, so a call
        /// that lands before Boscali's Awake simply retries later.</summary>
        private static PropertyInfo Resolve()
        {
            if (resolved) return gestureArmedProperty;
            if (!Available) return null;
            resolved = true;

            try
            {
                Type t = Type.GetType(SupportMapModeTypeName, throwOnError: false);
                if (t == null)
                {
                    Plugin.Logger?.LogWarning(
                        "BoscaliLink: Boscali Summer is loaded but BoscaliSummer.Interop.SupportMapMode " +
                        "was not found; continuing without map-gesture deconfliction.");
                    return null;
                }

                gestureArmedProperty = t.GetProperty("GestureArmed",
                    BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception e)
            {
                Fail(e);
            }

            return gestureArmedProperty;
        }

        private static void Fail(Exception e)
        {
            gestureArmedProperty = null;
            Plugin.Logger?.LogWarning(
                "BoscaliLink: Boscali Summer interop failed, continuing without map-gesture " +
                "deconfliction. " + e.Message);
        }
    }
}
