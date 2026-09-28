using System;

namespace WingCommand
{
    /// <summary>Formation quality over one window. Sentinels: <c>-1</c> for anything never observed (no sample, no
    /// captured member, no pair); counts are 0.</summary>
    internal struct MetricsSnapshot
    {
        public int Members, CapturedMembers, Left, Gcas, Collision, FallingBehind, Transitions;
        public float WindowSeconds, SlotRmsM, SlotMaxM, StationFraction, MeanCaptureSeconds, MinSeparationM, MinSpeedMps;
    }

    /// <summary>Accumulates formation metrics for the automated in-game scenarios (the M1 exit criteria): slot error
    /// RMS and max (time-weighted), fraction of time in station, capture time from the window start, closest
    /// separation, slowest member, members that left, and event counts. Members are keyed by a stable id (the engine
    /// renumbers slots when one leaves), up to <see cref="Capacity"/> per window. Fixed arrays, no allocation per
    /// sample. A member that leaves keeps its samples.</summary>
    internal sealed class WingMetrics
    {
        public const int Capacity = 16;
        private readonly int[] ids = new int[Capacity];
        private readonly float[] capture = new float[Capacity];
        private int used, left;
        private float start, sumSq, sumDt, stationDt, max, minSeparation, minSpeed;
        private int gcas, collision, fallingBehind, transitions;

        public WingMetrics() => Reset(0f);

        public void Reset(float time)
        {
            start = time;
            used = left = 0;
            sumSq = sumDt = stationDt = max = 0f;
            minSeparation = minSpeed = float.MaxValue;
            gcas = collision = fallingBehind = transitions = 0;
        }

        /// <summary>One member tick: its slot error (m), whether it is keeping station, its speed and the tick length.
        /// A member beyond <see cref="Capacity"/> in one window is ignored.</summary>
        public void Sample(int member, float slotError, bool stationKeeping, float speed, float time, float dt)
        {
            int k = IndexOf(member, add: true);
            if (k < 0 || dt <= 0f) return;
            sumSq += slotError * slotError * dt;
            sumDt += dt;
            max = Math.Max(max, slotError);
            minSpeed = Math.Min(minSpeed, speed);
            if (!stationKeeping) return;
            stationDt += dt;
            if (capture[k] < 0f) capture[k] = time - start;
        }

        /// <summary>The member left the wing (lost, released, dismissed) during the window.</summary>
        public void Left(int member)
        {
            if (IndexOf(member, add: false) >= 0) left++;
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
            bool sampled = sumDt > 0f;
            var s = new MetricsSnapshot
            {
                Members = used,
                Left = left,
                WindowSeconds = time - start,
                SlotRmsM = sampled ? (float)Math.Sqrt(sumSq / sumDt) : -1f,
                SlotMaxM = sampled ? max : -1f,
                StationFraction = sampled ? stationDt / sumDt : -1f,
                MinSeparationM = minSeparation == float.MaxValue ? -1f : minSeparation,
                MinSpeedMps = minSpeed == float.MaxValue ? -1f : minSpeed,
                Gcas = gcas,
                Collision = collision,
                FallingBehind = fallingBehind,
                Transitions = transitions,
            };
            float captureSum = 0f;
            for (int i = 0; i < used; i++)
            {
                if (capture[i] < 0f) continue;
                s.CapturedMembers++;
                captureSum += capture[i];
            }
            s.MeanCaptureSeconds = s.CapturedMembers > 0 ? captureSum / s.CapturedMembers : -1f;
            return s;
        }

        private int IndexOf(int member, bool add)
        {
            for (int i = 0; i < used; i++)
                if (ids[i] == member) return i;
            if (!add || used >= Capacity) return -1;
            ids[used] = member;
            capture[used] = -1f;
            return used++;
        }
    }
}
