namespace WingCommand
{
    /// <summary>One known contact as the radio sees it: air or ground, and how far from the nearest flying member.</summary>
    internal struct ContactSample
    {
        public uint Id;
        public bool Air;
        public float Distance;
    }

    /// <summary>Spec M7 §2.2: which contacts are new enough to call. A contact is reported once when it first comes
    /// within its range (<see cref="AirRange"/>, or <see cref="GroundRange"/> while <see cref="Ground"/> is on); it is
    /// forgotten beyond <see cref="ForgetFactor"/> × its range or after <see cref="ForgetSeconds"/> missing, and reported
    /// again if it returns. At most <see cref="MaxPerUpdate"/> reports an update, the nearest first; the rest wait
    /// unreported. Bounded at <see cref="Capacity"/>; nothing is allocated.</summary>
    internal sealed class ContactWatch
    {
        public static float AirRange = 40000f, GroundRange = 8000f, ForgetFactor = 1.25f, ForgetSeconds = 60f;
        public static int MaxPerUpdate = 1;
        public const int Capacity = 64;

        private readonly uint[] ids = new uint[Capacity];
        private readonly float[] lastSeen = new float[Capacity];
        private int known;

        /// <summary>Report ground contacts (scouting).</summary>
        public bool Ground;
        public int Known => known;

        public void Clear() => known = 0;

        /// <summary>Report this contact again when it next qualifies (the radio dropped its call: review M7a-2 I1).</summary>
        public void Forget(uint id)
        {
            int k = IndexOf(id);
            if (k >= 0) RemoveAt(k);
        }

        /// <summary>Whether a contact is worth sampling at all: air, or ground while scouting, no farther than its forget
        /// distance (review M7a-2 C1: every far building and vehicle filled the samples and hid new bandits).</summary>
        public bool Considers(bool air, float distance) =>
            (air || Ground) && distance <= (air ? AirRange : GroundRange) * ForgetFactor;

        /// <summary>Writes the indices into <paramref name="samples"/> to report now into <paramref name="report"/>;
        /// returns how many.</summary>
        public int Update(ContactSample[] samples, int count, float now, int[] report)
        {
            for (int i = 0; i < count; i++)
            {
                if (!Range(samples[i], out float range)) continue;
                int k = IndexOf(samples[i].Id);
                if (k < 0) continue;
                if (samples[i].Distance > range * ForgetFactor) RemoveAt(k);
                else lastSeen[k] = now;
            }
            for (int k = known - 1; k >= 0; k--)
                if (now - lastSeen[k] > ForgetSeconds) RemoveAt(k);
            int n = 0;
            int limit = System.Math.Min(MaxPerUpdate, report.Length);
            while (n < limit)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (!Range(samples[i], out float range) || samples[i].Distance > range || IndexOf(samples[i].Id) >= 0) continue;
                    if (best < 0 || samples[i].Distance < samples[best].Distance) best = i;
                }
                if (best < 0) break;
                Remember(samples[best].Id, now);
                report[n++] = best;
            }
            return n;
        }

        private bool Range(in ContactSample s, out float range)
        {
            range = s.Air ? AirRange : GroundRange;
            return s.Air || Ground;
        }

        private int IndexOf(uint id)
        {
            for (int k = 0; k < known; k++)
                if (ids[k] == id) return k;
            return -1;
        }

        private void Remember(uint id, float now)
        {
            if (known == Capacity)
            {
                int oldest = 0;
                for (int k = 1; k < known; k++)
                    if (lastSeen[k] < lastSeen[oldest]) oldest = k;
                RemoveAt(oldest);
            }
            ids[known] = id;
            lastSeen[known] = now;
            known++;
        }

        private void RemoveAt(int k)
        {
            known--;
            ids[k] = ids[known];
            lastSeen[k] = lastSeen[known];
        }
    }
}
