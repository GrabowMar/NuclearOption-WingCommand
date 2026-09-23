using System;

namespace WingCommand
{
    /// <summary>Formation quality over one window. Sentinels: <c>-1</c> for a capture time, separation or speed
    /// that was never observed; zeros elsewhere.</summary>
    internal struct MetricsSnapshot
    {
        public int Members, CapturedMembers, Gcas, Collision, FallingBehind, Transitions;
        public float WindowSeconds, SlotRmsM, SlotMaxM, StationFraction, MeanCaptureSeconds, MinSeparationM, MinSpeedMps;
    }

    /// <summary>Accumulates formation metrics for the automated in-game scenarios (the M1 exit criteria): slot error
    /// RMS and max (time-weighted), fraction of time in station, capture time from the window start, closest
    /// separation, slowest member and event counts. Fixed arrays, no allocation per sample. A member released
    /// mid-window keeps its samples.</summary>
    internal sealed class WingMetrics
    {
        private const int N = FormationCatalog.MaxSlots;
        private readonly bool[] seen = new bool[N];
        private readonly float[] capture = new float[N];
        private float start, sumSq, sumDt, stationDt, max, minSeparation, minSpeed;
        private int gcas, collision, fallingBehind, transitions;

        public WingMetrics() => Reset(0f);

        public void Reset(float time)
        {
            start = time;
            sumSq = sumDt = stationDt = max = 0f;
            minSeparation = minSpeed = float.MaxValue;
            gcas = collision = fallingBehind = transitions = 0;
            for (int i = 0; i < N; i++)
            {
                seen[i] = false;
                capture[i] = -1f;
            }
        }

        /// <summary>One member tick: its slot error (m), whether it is keeping station, its speed and the tick
        /// length. Slots outside the wing are ignored.</summary>
        public void Sample(int slot, float slotError, bool stationKeeping, float speed, float time, float dt)
        {
            if (slot < 0 || slot >= N || dt <= 0f) return;
            seen[slot] = true;
            sumSq += slotError * slotError * dt;
            sumDt += dt;
            max = Math.Max(max, slotError);
            minSpeed = Math.Min(minSpeed, speed);
            if (!stationKeeping) return;
            stationDt += dt;
            if (capture[slot] < 0f) capture[slot] = time - start;
        }

        /// <summary>The closest distance between two aircraft of the wing (the leader included) this tick.</summary>
        public void Separation(float metres) => minSeparation = Math.Min(minSeparation, metres);

        public void Event(WingEventKind kind)
        {
            switch (kind)
            {
                case WingEventKind.GcasActivated: gcas++; break;
                case WingEventKind.CollisionEmergency: collision++; break;
                case WingEventKind.FallingBehind: fallingBehind++; break;
                case WingEventKind.BehaviourChanged: transitions++; break;
            }
        }

        public MetricsSnapshot Snapshot(float time)
        {
            var s = new MetricsSnapshot
            {
                WindowSeconds = time - start,
                SlotRmsM = sumDt > 0f ? (float)Math.Sqrt(sumSq / sumDt) : 0f,
                SlotMaxM = max,
                StationFraction = sumDt > 0f ? stationDt / sumDt : 0f,
                MinSeparationM = minSeparation == float.MaxValue ? -1f : minSeparation,
                MinSpeedMps = minSpeed == float.MaxValue ? -1f : minSpeed,
                Gcas = gcas,
                Collision = collision,
                FallingBehind = fallingBehind,
                Transitions = transitions,
            };
            float captureSum = 0f;
            for (int i = 0; i < N; i++)
            {
                if (seen[i]) s.Members++;
                if (capture[i] < 0f) continue;
                s.CapturedMembers++;
                captureSum += capture[i];
            }
            s.MeanCaptureSeconds = s.CapturedMembers > 0 ? captureSum / s.CapturedMembers : -1f;
            return s;
        }
    }
}
