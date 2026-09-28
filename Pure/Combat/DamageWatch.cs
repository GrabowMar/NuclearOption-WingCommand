namespace WingCommand
{
    /// <summary>A member's hull from its parts' hit points (spec WMC rebuild R3; the DAMAGED alert, INSPECT, later the rules):
    /// the attached parts' health over the most parts ever counted (parts that come off the list must not read as healthier),
    /// DAMAGED at or below <see cref="DamagedAt"/> or with a part detached, clear only at <see cref="ClearAt"/> with none
    /// detached. <see cref="Update"/> is true only on the edge into DAMAGED.</summary>
    internal sealed class DamageWatch
    {
        public static float DamagedAt = 0.85f, ClearAt = 0.90f;

        private int baseline;

        public float Hull { get; private set; } = 1f;
        public bool Damaged { get; private set; }

        /// <param name="healthSum">Sum over attached parts of health 0–1.</param>
        /// <param name="parts">Parts counted now (attached and detached).</param>
        /// <param name="detached">Any part detached.</param>
        public bool Update(float healthSum, int parts, bool detached)
        {
            if (parts > baseline) baseline = parts;
            Hull = baseline > 0 ? System.Math.Max(0f, System.Math.Min(1f, healthSum / baseline)) : 1f;
            if (Damaged)
            {
                if (!detached && Hull >= ClearAt) Damaged = false;
                return false;
            }
            if (!detached && Hull > DamagedAt) return false;
            Damaged = true;
            return true;
        }
    }

    /// <summary>A member out of weapons in formation calls it once (spec WMC rebuild R3): the fight's own Winchester disengage
    /// latches it, and rearming clears it.</summary>
    internal struct WinchesterLatch
    {
        public bool Latched;

        /// <summary>True once when <paramref name="dry"/> first holds while <paramref name="inFormation"/>.</summary>
        public bool Update(bool dry, bool inFormation)
        {
            if (!dry)
            {
                Latched = false;
                return false;
            }
            if (Latched) return false;
            Latched = true;
            return inFormation;
        }
    }
}
