namespace WingCommand
{
    internal enum BehaviourId : byte { Rejoin, StationKeep, HoldOverhead, Trail, Defend }

    internal enum WingEventKind : byte
    {
        BehaviourChanged, FallingBehind, FallingBehindCleared, GcasActivated, CollisionEmergency, AnchorLost, Converted,
        // Ground operations (M3).
        GroundSpawned, Taxiing, HoldingShort, LiningUp, Rolling, Airborne, Rerouted, Relocated, DepartureAborted, Parked, PulledAside,
        Landed, LandingFailed, Serviced, Reserved, Bingo,
        // Tasks (M4).
        TaskStarted, TaskCompleted, TaskCancelled, TaskFailed, WaypointReached,
        // Combat (M5).
        Engaged, Disengaged, Joker,
    }

    internal enum TransitionReason : byte { None, Captured, LostSlot, LeaderSlow, LeaderNotFlying, LeaderLost, LeaderRecovered, Commanded, LeaderFast, FollowOn, NoWing, NoTarget, Fuel, Leash, Winchester, Outnumbered, MissileInbound, MissileClear }

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
        /// <summary>The element the event is about (0 = A).</summary>
        public byte Element;
        /// <summary>The member's aircraft (persistent id) when the event was logged, stamped by the ring from its seat
        /// table; 0 for a wing event or an unknown seat (review P3 I5: seats renumber, aircraft do not).</summary>
        public uint Id;
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

        public const int MaxSeats = 32;
        private readonly uint[] seats = new uint[MaxSeats];

        /// <summary>The aircraft flying in <paramref name="seat"/> from now on (the service calls it whenever seats change).</summary>
        public void Seat(int seat, uint id)
        {
            if (seat >= 0 && seat < MaxSeats) seats[seat] = id;
        }

        public void Push(in WingEvent e)
        {
            items[next] = e;
            if (e.Id == 0u && e.Member >= 0 && e.Member < MaxSeats) items[next].Id = seats[e.Member];
            next = (next + 1) % Capacity;
            if (Count < Capacity) Count++;
            Total++;
        }

        /// <summary>Behaviour transitions retained with <paramref name="reason"/>.</summary>
        public int CountOf(TransitionReason reason)
        {
            int n = 0;
            for (int i = 0; i < Count; i++)
                if (this[i].Kind == WingEventKind.BehaviourChanged && this[i].Reason == reason) n++;
            return n;
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
