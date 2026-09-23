using System;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Read-only access to private flight fields: the fly-by-wire numbers a profile is derived from,
    /// the wind the aircraft flies in, and the player state's pilot and G-LOC strength. Each group resolves on
    /// its own, so a failure disables only its consumer.</summary>
    internal static partial class GameAccess
    {
        private static AccessTools.FieldRef<ControlsFilter.FlyByWire, float> fbwMaxRollRef, fbwGLimitRef, fbwCornerRef;
        private static AccessTools.FieldRef<ControlsFilter, float> filterMinSpeedRef, filterMinAltRef;
        private static AccessTools.FieldRef<Aircraft, Vector3> windRef;
        private static AccessTools.FieldRef<PilotBaseState, Pilot> statePilotRef;
        private static AccessTools.FieldRef<PilotPlayerState, float> pilotStrengthRef;

        public static bool FlyByWireAvailable { get; private set; }
        public static bool FbwGateAvailable { get; private set; }
        public static bool WindAvailable { get; private set; }
        public static bool PilotStateAvailable { get; private set; }

        public static void InitialiseFlight()
        {
            try
            {
                fbwMaxRollRef = Field<ControlsFilter.FlyByWire, float>("maxRollAngularVel");
                fbwGLimitRef = Field<ControlsFilter.FlyByWire, float>("gLimitPositive");
                fbwCornerRef = Field<ControlsFilter.FlyByWire, float>("cornerSpeed");
                FlyByWireAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Fly-by-wire fields unreadable (" + e.Message + "); profiles use AircraftParameters only.");
            }
            try
            {
                filterMinSpeedRef = Field<ControlsFilter, float>("minSpeed");
                filterMinAltRef = Field<ControlsFilter, float>("minAlt");
                FbwGateAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Controls filter gate unreadable (" + e.Message + "); fly-by-wire assumed from 25 m/s and 1 m.");
            }
            try
            {
                windRef = Field<Aircraft, Vector3>("windVelocity");
                WindAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Aircraft wind unreadable (" + e.Message + "); airspeed ignores wind.");
            }
            try
            {
                statePilotRef = Field<PilotBaseState, Pilot>("pilot");
                pilotStrengthRef = Field<PilotPlayerState, float>("pilotStrength");
                PilotStateAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Player state unreadable (" + e.Message + "); the player autopilot is disabled.");
            }
        }

        public static Vector3 WindOf(Aircraft a) => WindAvailable ? windRef(a) : Vector3.zero;

        public static bool TryReadFlyByWire(Aircraft a, out float maxRollAngularVel, out float gLimit, out float cornerSpeed)
        {
            maxRollAngularVel = gLimit = cornerSpeed = 0f;
            ControlsFilter.FlyByWire fbw = FlyByWireAvailable ? a.GetControlsFilter()?.GetFlyByWire() : null;
            if (fbw == null) return false;
            maxRollAngularVel = fbwMaxRollRef(fbw);
            gLimit = fbwGLimitRef(fbw);
            cornerSpeed = fbwCornerRef(fbw);
            return true;
        }

        /// <summary>The speed and radar altitude below which the aircraft's ControlsFilter stops filtering.</summary>
        public static bool TryReadFbwGate(Aircraft a, out float minSpeed, out float minAlt)
        {
            minSpeed = minAlt = 0f;
            ControlsFilter filter = FbwGateAvailable ? a.GetControlsFilter() : null;
            if (filter == null) return false;
            minSpeed = filterMinSpeedRef(filter);
            minAlt = filterMinAltRef(filter);
            return true;
        }

        public static Pilot PilotOf(PilotBaseState state) => PilotStateAvailable ? statePilotRef(state) : null;

        public static float PilotStrength(PilotPlayerState state) => PilotStateAvailable ? pilotStrengthRef(state) : 1f;
    }
}
