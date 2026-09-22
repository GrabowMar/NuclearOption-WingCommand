using System;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>Reflected access to the native fly-by-wire limiter so wing AI can be granted pitch
    /// authority beyond the published G and angle-of-attack ceilings. The public
    /// SetFlyByWireParameters path cannot change these fields, and application control blocks a
    /// publicizer, so resolve them once through AccessTools like the other native members.</summary>
    internal static partial class GameAccess
    {
        private static AccessTools.FieldRef<ControlsFilter.FlyByWire, float> flyByWireGLimitRef;
        private static AccessTools.FieldRef<ControlsFilter.FlyByWire, float> flyByWireAlphaLimiterRef;

        /// <summary>Whether both fly-by-wire limit fields resolved.</summary>
        public static bool FlyByWireLimitsAvailable { get; private set; }

        /// <summary>Resolve fly-by-wire limit fields independently so failure disables only the
        /// wingman overdrive rather than the radial and MFD integrations.</summary>
        public static void InitialiseFlyByWireLimits()
        {
            try
            {
                flyByWireGLimitRef = Field<ControlsFilter.FlyByWire, float>("gLimitPositive");
                flyByWireAlphaLimiterRef = Field<ControlsFilter.FlyByWire, float>("alphaLimiter");
                FlyByWireLimitsAvailable = true;
            }
            catch (Exception e)
            {
                FlyByWireLimitsAvailable = false;
                Plugin.Logger.LogWarning(
                    "Fly-by-wire limit access unavailable (" + e.Message +
                    "). Wingman overdrive is disabled.");
            }
        }

        /// <summary>Read the live limiter values so the caller can restore them after the call.</summary>
        public static bool TryReadFlyByWireLimits(ControlsFilter.FlyByWire flyByWire,
            out float gLimit, out float alphaLimiter)
        {
            gLimit = 0f;
            alphaLimiter = 0f;
            if (!FlyByWireLimitsAvailable || flyByWire == null) return false;

            try
            {
                gLimit = flyByWireGLimitRef(flyByWire);
                alphaLimiter = flyByWireAlphaLimiterRef(flyByWire);
                return true;
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Fly-by-wire limit read failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Overwrite the live limiter values for the current filter call.</summary>
        public static bool SetFlyByWireLimits(ControlsFilter.FlyByWire flyByWire,
            float gLimit, float alphaLimiter)
        {
            if (!FlyByWireLimitsAvailable || flyByWire == null) return false;

            try
            {
                flyByWireGLimitRef(flyByWire) = gLimit;
                flyByWireAlphaLimiterRef(flyByWire) = alphaLimiter;
                return true;
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Fly-by-wire limit write failed: " + e.Message);
                return false;
            }
        }
    }
}
