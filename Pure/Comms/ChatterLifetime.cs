using System;

namespace WingCommand
{
    /// <summary>Checks queued speech against its age and the action that produced it.</summary>
    internal readonly struct ChatterLifetime
    {
        private readonly float queuedAt;
        private readonly bool urgent;
        private readonly Func<bool> isRelevant;

        public ChatterLifetime(float queuedAt, bool urgent, Func<bool> isRelevant)
        {
            this.queuedAt = queuedAt;
            this.urgent = urgent;
            this.isRelevant = isRelevant;
        }

        public bool IsRelevant => isRelevant == null || isRelevant();
        public bool CanStart(float now) => IsRelevant && now - queuedAt < (urgent ? 15f : 6f);
    }
}
