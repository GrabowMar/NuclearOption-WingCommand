using System;

namespace WingCommand
{
    internal enum ApDisengage : byte { None, Gloc, GearLow, Slow, Throttle }

// Filled by the engine's player autopilot from the player's stick and by tests.
#pragma warning disable CS0649
    /// <summary>What the pilot is doing this tick, in Pure signs (pitch + nose up).</summary>
    internal struct PilotInputs
    {
        public float Pitch, Roll, Yaw, Throttle;
        public bool Gloc, GearDown;
    }
#pragma warning restore CS0649

    /// <summary>Which axes the autopilot writes this tick. When <see cref="Recaptured"/> is set, the caller seeds
    /// the flight pipeline from the applied inputs first (bumpless).</summary>
    internal struct AutopilotTick
    {
        public bool WritePitch, WriteRoll, WriteYaw, WriteThrottle, Recaptured;
        public ApDisengage Disengaged;
    }

    /// <summary>The player autopilot's modes, per-axis stick override and safety disengage (spec §7).
    /// <list type="bullet">
    /// <item>Engaging a mode captures the current value.</item>
    /// <item>A stick deflection above <see cref="StickDeadzone"/> hands that axis to the pilot.
    /// <see cref="RecaptureSeconds"/> after release, the axis re-captures its current value; ALT also waits
    /// for |vs| &lt; 2 m/s.</item>
    /// <item>Moving the throttle more than <see cref="ThrottleDisengage"/> drops SPD alone.</item>
    /// <item>G-LOC, gear down below 20 m AGL, or speed under 1.1 × loaded minimum drop every mode.</item>
    /// </list></summary>
    internal sealed class AutopilotSession
    {
        public static float StickDeadzone = 0.08f, RecaptureSeconds = 0.4f, AltitudeRecaptureVs = 2f;
        public static float ThrottleDisengage = 0.05f, GearLowAgl = 20f, SlowFactor = 1.1f;

        public HoldSpec Spec;
        public bool LateralOverride { get; private set; }
        public bool VerticalOverride { get; private set; }
        private float lateralQuiet, verticalQuiet, throttleRef;

        public bool Engaged => Spec.Lateral != LateralHold.None || Spec.Vertical != VerticalHold.None || Spec.Speed;

        public void SetLateral(LateralHold mode, in AircraftState s)
        {
            Spec.Lateral = mode;
            Spec.HeadingDeg = s.TrackDeg;
            LateralOverride = false;
        }

        public void SetVertical(VerticalHold mode, in AircraftState s)
        {
            Spec.Vertical = mode;
            Spec.AltitudeM = s.Pos.Y;
            Spec.VerticalSpeedMps = s.Vel.Y;
            VerticalOverride = false;
        }

        public void SetSpeed(bool on, in AircraftState s, float playerThrottle)
        {
            Spec.Speed = on;
            Spec.SpeedMps = s.Tas;
            throttleRef = playerThrottle;
        }

        public void Off()
        {
            Spec = default;
            LateralOverride = false;
            VerticalOverride = false;
        }

        public AutopilotTick Step(in AircraftState s, in PilotInputs pilot, float loadedMinimum, float dt)
        {
            var t = new AutopilotTick();
            if (!Engaged) return t;

            ApDisengage safety = pilot.Gloc ? ApDisengage.Gloc
                : pilot.GearDown && s.RadarAlt < GearLowAgl ? ApDisengage.GearLow
                : s.Tas < SlowFactor * loadedMinimum ? ApDisengage.Slow
                : ApDisengage.None;
            if (safety != ApDisengage.None)
            {
                Off();
                t.Disengaged = safety;
                return t;
            }

            if (Spec.Speed && Math.Abs(pilot.Throttle - throttleRef) > ThrottleDisengage)
            {
                Spec.Speed = false;
                t.Disengaged = ApDisengage.Throttle;
            }

            if (Spec.Lateral != LateralHold.None)
            {
                if (Math.Abs(pilot.Roll) > StickDeadzone || Math.Abs(pilot.Yaw) > StickDeadzone)
                {
                    LateralOverride = true;
                    lateralQuiet = 0f;
                }
                else if (LateralOverride)
                {
                    lateralQuiet += dt;
                    if (lateralQuiet >= RecaptureSeconds)
                    {
                        LateralOverride = false;
                        Spec.HeadingDeg = s.TrackDeg;
                        t.Recaptured = true;
                    }
                }
                t.WriteRoll = t.WriteYaw = !LateralOverride;
            }

            if (Spec.Vertical != VerticalHold.None)
            {
                if (Math.Abs(pilot.Pitch) > StickDeadzone)
                {
                    VerticalOverride = true;
                    verticalQuiet = 0f;
                }
                else if (VerticalOverride)
                {
                    verticalQuiet += dt;
                    bool settled = Spec.Vertical != VerticalHold.Altitude || Math.Abs(s.Vel.Y) < AltitudeRecaptureVs;
                    if (verticalQuiet >= RecaptureSeconds && settled)
                    {
                        VerticalOverride = false;
                        Spec.AltitudeM = s.Pos.Y;
                        Spec.VerticalSpeedMps = s.Vel.Y;
                        t.Recaptured = true;
                    }
                }
                t.WritePitch = !VerticalOverride;
            }

            t.WriteThrottle = Spec.Speed;
            return t;
        }
    }
}
