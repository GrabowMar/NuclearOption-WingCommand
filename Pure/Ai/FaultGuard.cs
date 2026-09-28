namespace WingCommand
{
    /// <summary>Counts faults (exceptions in a member's flight step). <see cref="Record"/> returns true when the
    /// <see cref="Limit"/>th fault falls within <see cref="WindowSeconds"/> of the first of them: the caller then
    /// hands the aircraft to native AI once and logs once (spec §8).</summary>
    internal sealed class FaultGuard
    {
        public static int Limit = 3;
        public static float WindowSeconds = 10f;

        private readonly float[] times = new float[8];
        private int count;

        public bool Record(float time)
        {
            int limit = System.Math.Max(1, System.Math.Min(Limit, times.Length));
            if (count == limit)
            {
                for (int i = 1; i < count; i++) times[i - 1] = times[i];
                count--;
            }
            times[count++] = time;
            return count == limit && time - times[0] <= WindowSeconds;
        }
    }
}
