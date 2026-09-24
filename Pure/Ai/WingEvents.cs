namespace WingCommand
{
    internal enum BehaviourId : byte { Rejoin, StationKeep, HoldOverhead, Trail }

    internal enum WingEventKind : byte
    {
        BehaviourChanged, FallingBehind, FallingBehindCleared, GcasActivated, CollisionEmergency, AnchorLost, Converted,
        // Ground operations (M3).
        GroundSpawned, Taxiing, HoldingShort, LiningUp, Rolling, Airborne, Rerouted, Relocated, DepartureAborted, Parked, PulledAside,
        Landed, LandingFailed, Serviced, Reserved, Bingo,
        // Tasks (M4).
        TaskStarted, TaskCompleted, TaskCancelled, TaskFailed, WaypointReached,
        // Combat (M5).
        Engaged, Disengaged,
    }

    internal enum TransitionReason : byte { None, Captured, LostSlot, LeaderSlow, LeaderNotFlying, LeaderLost, LeaderRecovered, Commanded, LeaderFast, FollowOn, NoWing, NoTarget, Fuel, Leash, Winchester }

    /// <summary>One entry of the wing log: a behaviour transition with its reason, or a notable event.</summary>
    internal struct WingEvent
    {
        public float Time;
        public int Member;
        public WingEventKind Kind;
        public BehaviourId From, To;
        public TransitionReason Reason;
        /// <summary>The task an event of a task is about.</summary>
        public TaskKind Task;
    }

    /// <summary>Fixed-capacity ring of wing events, oldest first. Push never allocates; when full the oldest
    /// entry is overwritten. Comms, HUD, the AI log and telemetry read it by index.</summary>
    internal sealed class WingEventRing
    {
        public const int Capacity = 256;
        private readonly WingEvent[] items = new WingEvent[Capacity];
        private int next;

        public int Count { get; private set; }
        public long Total { get; private set; }

        /// <summary>Index 0 is the oldest retained event.</summary>
        public WingEvent this[int index] => items[(next - Count + index + Capacity) % Capacity];

        public void Push(in WingEvent e)
        {
            items[next] = e;
            next = (next + 1) % Capacity;
            if (Count < Capacity) Count++;
            Total++;
        }

        public int CountOf(WingEventKind kind, int member = -1)
        {
            int n = 0;
            for (int i = 0; i < Count; i++)
            {
                WingEvent e = this[i];
                if (e.Kind == kind && (member < 0 || e.Member == member)) n++;
            }
            return n;
        }
    }
}
