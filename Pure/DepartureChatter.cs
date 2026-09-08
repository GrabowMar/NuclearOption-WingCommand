using System.Collections.Generic;

namespace WingCommand
{
    internal enum DeparturePhase { None, Taxiing, Departing, Airborne }

 /// <summary>Keeps one current departure report per airframe on a rate-limited radio channel.</summary>
    internal sealed class DepartureChatter
    {
        internal const float ChannelSpacingSeconds = 5f;
        internal const float ReportLifetimeSeconds = 20f;

        private sealed class Progress
        {
            internal DeparturePhase Observed;
            internal DeparturePhase Pending;
            internal float PendingAt;
        }

        private readonly Dictionary<int, Progress> progress = new Dictionary<int, Progress>();
        private float nextTransmissionAt;

        internal void Observe(int memberId, DeparturePhase phase, float now)
        {
            if (phase == DeparturePhase.None) return;
            if (!progress.TryGetValue(memberId, out Progress item))
                progress.Add(memberId, item = new Progress());
            if (phase <= item.Observed) return;
            item.Observed = phase;
            // Replace unsaid phases with the latest so taxi cannot be announced after liftoff.
            item.Pending = phase;
            item.PendingAt = now;
        }

        internal bool TryDequeue(float now, bool channelIdle, out int memberId, out DeparturePhase phase)
        {
            memberId = 0;
            phase = DeparturePhase.None;
            if (!channelIdle || now < nextTransmissionAt) return false;

            Progress first = null;
            foreach (var pair in progress)
            {
                Progress item = pair.Value;
                if (item.Pending == DeparturePhase.None) continue;
                if (now - item.PendingAt > ReportLifetimeSeconds)
                {
                    item.Pending = DeparturePhase.None;
                    continue;
                }
                if (first != null && (item.PendingAt > first.PendingAt ||
                    (item.PendingAt == first.PendingAt && pair.Key >= memberId))) continue;
                first = item;
                memberId = pair.Key;
            }

            if (first == null) return false;
            phase = first.Pending;
            first.Pending = DeparturePhase.None;
            nextTransmissionAt = now + ChannelSpacingSeconds;
            return true;
        }

        internal void Silence()
        {
            foreach (Progress item in progress.Values) item.Pending = DeparturePhase.None;
        }

        internal bool HasPending(int memberId, DeparturePhase phase) =>
            progress.TryGetValue(memberId, out Progress item) && item.Pending == phase;

        internal void Forget(int memberId) => progress.Remove(memberId);

        internal void Reset()
        {
            progress.Clear();
            nextTransmissionAt = 0f;
        }
    }
}
