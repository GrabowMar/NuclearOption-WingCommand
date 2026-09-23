using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One airbase's Wing Command traffic (spec M3 §2.2): its graph, the reservations and departures every member
    /// there shares, the departure runway and direction, where each member is, and foreign aircraft standing on the
    /// field (<see cref="Obstacles"/>, refreshed by the engine). Stepped once per tick before its members; it checks
    /// for a reservation deadlock every <see cref="DeadlockPeriod"/> and names the member that must back off.</summary>
    internal sealed class FieldTraffic
    {
        public static float DeadlockPeriod = 1f;

        public readonly AirbaseSample Field;
        public readonly TaxiGraph Graph;
        public readonly TaxiReservations Reservations;
        public readonly DepartureSequencer Departures = new DepartureSequencer();
        public readonly int RunwayIndex;
        public readonly bool Reverse;
        public readonly List<Vec3> Obstacles = new List<Vec3>();
        private readonly Dictionary<int, Vec3> positions = new Dictionary<int, Vec3>();
        private float sinceCheck;

        public FieldTraffic(AirbaseSample field, int runwayIndex, bool reverse)
        {
            Field = field;
            Graph = TaxiGraph.Build(field);
            Reservations = new TaxiReservations(Graph);
            RunwayIndex = runwayIndex;
            Reverse = reverse;
        }

        public RunwaySample Runway => Field.Runways[RunwayIndex];

        /// <summary>The member to back off and reroute out of a deadlock, −1 when there is none.</summary>
        public int Victim { get; private set; } = -1;

        /// <summary>The first runway usable for takeoff (and its direction: the end nearer the field centre starts the roll,
        /// so the taxi is short), or false when the field has none.</summary>
        public static bool TryPickRunway(AirbaseSample field, out int index, out bool reverse)
        {
            for (int r = 0; r < field.Runways.Length; r++)
            {
                RunwaySample runway = field.Runways[r];
                if (!runway.Takeoff) continue;
                index = r;
                reverse = runway.Reversable && (runway.End - field.Center).Horizontal.Length < (runway.Start - field.Center).Horizontal.Length;
                return true;
            }
            index = -1;
            reverse = false;
            return false;
        }

        public void Report(int owner, Vec3 pos) => positions[owner] = pos;

        public bool TryGetPosition(int owner, out Vec3 pos) => positions.TryGetValue(owner, out pos);

        public void Step(float dt)
        {
            sinceCheck += dt;
            if (sinceCheck < DeadlockPeriod) return;
            sinceCheck = 0f;
            Victim = Reservations.FindDeadlock(out int victim) ? victim : -1;
        }

        public void ConsumeVictim(int owner)
        {
            if (Victim == owner) Victim = -1;
        }

        /// <summary>A member done with the field (airborne, dead, released): its claims and its place go.</summary>
        public void Leave(int owner)
        {
            Reservations.ReleaseAll(owner);
            Departures.Remove(owner);
            positions.Remove(owner);
            ConsumeVictim(owner);
        }
    }
}
