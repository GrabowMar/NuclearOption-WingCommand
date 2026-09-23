// Data contracts between behaviours, guidance, constraints and control. Some fields are filled only by
// behaviours (M1b), the engine (M1c) or the FlightSim, so the mod assembly may not assign them yet.
#pragma warning disable CS0649

namespace WingCommand
{
    /// <summary>A reference trajectory point to track (world, SI).</summary>
    internal readonly struct RefState
    {
        public readonly Vec3 Pos, Vel, Acc;

        public RefState(Vec3 pos, Vec3 vel, Vec3 acc)
        {
            Pos = pos;
            Vel = vel;
            Acc = acc;
        }
    }

    internal readonly struct SpeedLimits
    {
        public readonly float Min, Max;
        public readonly bool AfterburnerAllowed, AirbrakeAllowed;

        public SpeedLimits(float min, float max, bool afterburnerAllowed, bool airbrakeAllowed)
        {
            Min = min;
            Max = max;
            AfterburnerAllowed = afterburnerAllowed;
            AirbrakeAllowed = airbrakeAllowed;
        }
    }

    /// <summary>What the active behaviour asks the flight pipeline to track this tick.</summary>
    internal struct FlightIntent
    {
        public RefState Ref;
        public SpeedLimits Limits;
        /// <summary>Pilot precision 0.8–1.2; guidance time constants are divided by it (0 means 1).</summary>
        public float Precision;
        /// <summary>0–1; widens the bank ceiling from 60° to 85°.</summary>
        public float Aggression;
        /// <summary>Formation spacing in metres for the near-reference overtake cap; 0 disables it.</summary>
        public float Spacing;
        public float TerrainClearance;
        /// <summary>The heading to face when slow (a helicopter holding a slot faces its leader's way).</summary>
        public bool HasHeading;
        public float HeadingDeg;
    }

    /// <summary>Guidance output: commanded kinematic acceleration and velocity (world). A rotary law also sets the
    /// heading to face (<see cref="HasHeading"/>); without one the aircraft holds its heading.</summary>
    internal struct GuidanceCommand
    {
        public Vec3 Accel, VelCmd;
        public bool AfterburnerAllowed, AirbrakeAllowed;
        public bool HasHeading;
        public float HeadingDeg;
    }

    /// <summary>Attitude-level demand for the inner loops. EnergyRate is V·V̇/g + ḣ in m/s.</summary>
    internal struct AttitudeCommand
    {
        public float BankDeg, Nz, EnergyRate;
        public bool AfterburnerAllowed, AirbrakeAllowed, Gcas;
    }
}
