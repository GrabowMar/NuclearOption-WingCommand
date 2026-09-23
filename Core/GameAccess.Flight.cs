using System;
using System.Reflection;
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
        // Private nested types (HeloControlsFilter.HeloFlyByWire, Autopilot.HoverController): plain FieldInfo, read once
        // per aircraft when its profile is derived.
        private static FieldInfo heloFbwField, heloMaxAngularVelField, heloGLimitField, hoverControllerField, hoverThrottleField;

        public static bool FlyByWireAvailable { get; private set; }
        public static bool FbwGateAvailable { get; private set; }
        public static bool WindAvailable { get; private set; }
        public static bool PilotStateAvailable { get; private set; }
        public static bool HeloFlyByWireAvailable { get; private set; }
        public static bool HoverThrottleAvailable { get; private set; }

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
                heloFbwField = Required(typeof(HeloControlsFilter), "heloFlyByWire");
                heloMaxAngularVelField = Required(heloFbwField.FieldType, "maxAngularVel", typeof(Vector3));
                heloGLimitField = Required(heloFbwField.FieldType, "gLimit", typeof(float));
                HeloFlyByWireAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Helicopter fly-by-wire unreadable (" + e.Message + "); rotary profiles use defaults.");
            }
            try
            {
                hoverControllerField = Required(typeof(Autopilot), "hoverController");
                hoverThrottleField = Required(hoverControllerField.FieldType, "hoverThrottle", typeof(float));
                HoverThrottleAvailable = true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Hover throttle unreadable (" + e.Message + "); rotary hover trim starts at 0.5.");
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

        /// <summary>A helicopter's fly-by-wire rate limits (rad/s: x pitch, y yaw, z roll) and its g limit.</summary>
        public static bool TryReadHeloFlyByWire(Aircraft a, out Vector3 maxAngularVel, out float gLimit)
        {
            maxAngularVel = Vector3.zero;
            gLimit = 0f;
            if (!HeloFlyByWireAvailable || !(a.GetControlsFilter() is HeloControlsFilter filter)) return false;
            object fbw = heloFbwField.GetValue(filter);
            if (fbw == null) return false;
            maxAngularVel = (Vector3)heloMaxAngularVelField.GetValue(fbw);
            gLimit = (float)heloGLimitField.GetValue(fbw);
            return true;
        }

        /// <summary>The collective the aircraft's own autopilot hovers at (0.5 when unknown or implausible).</summary>
        public static bool TryReadHoverThrottle(Aircraft a, out float hoverThrottle)
        {
            hoverThrottle = 0f;
            Autopilot autopilot = HoverThrottleAvailable ? a.autopilot : null;
            object controller = autopilot != null ? hoverControllerField.GetValue(autopilot) : null;
            if (controller == null) return false;
            hoverThrottle = (float)hoverThrottleField.GetValue(controller);
            return hoverThrottle > 0.05f && hoverThrottle < 0.95f;
        }

        /// <summary>A field that must exist (and, given <paramref name="fieldType"/>, have that type: the reads unbox it,
        /// so a type change in a game update fails here once instead of throwing on every read).</summary>
        private static FieldInfo Required(Type type, string name, Type fieldType = null)
        {
            FieldInfo f = AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
            if (fieldType != null && f.FieldType != fieldType)
                throw new MissingFieldException($"{type.Name}.{name} is {f.FieldType.Name}, expected {fieldType.Name}");
            return f;
        }

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
