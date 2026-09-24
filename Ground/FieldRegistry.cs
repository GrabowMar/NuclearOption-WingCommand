using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>One <see cref="FieldTraffic"/> per live airbase that Wing Command aircraft use (spec M3 §2.2). Each field is
    /// stepped once per physics tick before its members; every <see cref="RefreshSeconds"/> it learns the foreign
    /// aircraft standing on it (obstacles for taxi guidance) and whether a native landing is registered on its
    /// departure runway (departures do not line up then). It also answers the runway lock for the native takeoff
    /// check (<see cref="RunwayLockPatch"/>), and while the lock is held one of our aircraft heads the game's takeoff
    /// queue, so native taxiing and crossing traffic sees the runway in use and waits before entering it.</summary>
    internal static class FieldRegistry
    {
        public static float RefreshSeconds = 1f, ObstacleMaxRadarAlt = 3f;

        private sealed class Entry
        {
            public Airbase Airbase;
            public FieldTraffic Traffic;
            public Aircraft Queued;
        }

        private static readonly List<Entry> fields = new List<Entry>();
        private static float sinceRefresh;

        /// <summary>The field's traffic, built on first use; null when it has no runway usable for takeoff.</summary>
        public static FieldTraffic For(Airbase airbase)
        {
            foreach (Entry e in fields)
                if (ReferenceEquals(e.Airbase, airbase)) return e.Traffic;
            AirbaseSample sample = AirbaseAdapter.Read(airbase);
            if (!FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse)) return null;
            var traffic = new FieldTraffic(sample, runway, reverse);
            fields.Add(new Entry { Airbase = airbase, Traffic = traffic });
            Plugin.Logger.LogInfo($"[Ground] {sample.Name}: {traffic.Graph.NodeCount} nodes, {traffic.Graph.EdgeCount} edges, " +
                                  $"{sample.Hangars.Length} hangars, runway {runway}{(reverse ? " reversed" : "")} " +
                                  $"({sample.Runways[runway].Width:0} m wide)");
            return traffic;
        }

        public static void Step(float dt, WingService wing)
        {
            sinceRefresh += dt;
            bool refresh = sinceRefresh >= RefreshSeconds;
            if (refresh) sinceRefresh = 0f;
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                Entry e = fields[i];
                if (e.Airbase == null)
                {
                    fields.RemoveAt(i);
                    continue;
                }
                if (refresh) Refresh(e, wing);
                e.Traffic.Step(dt);
                SyncTakeoffQueue(e, wing);
            }
        }

        /// <summary>Only into an empty queue (a native aircraft at its head already marks the runway in use), so no native
        /// aircraft ever waits behind ours; ours leaves the queue with the lock.</summary>
        private static void SyncTakeoffQueue(Entry e, WingService wing)
        {
            Airbase.Runway runway = RunwayOf(e);
            if (runway == null) return;
            if (!e.Traffic.Departures.RunwayLocked)
            {
                Dequeue(e, runway);
                return;
            }
            if (e.Queued != null && !e.Queued.disabled) return;
            e.Queued = null;
            Aircraft a = wing?.GroundAircraftOn(e.Traffic);
            if (a == null || !runway.ClearForTakeoff(a, false)) return;
            runway.SetUsageDirection(e.Traffic.Reverse);
            runway.QueueTakeoff(a);
            e.Queued = a;
        }

        private static void Dequeue(Entry e, Airbase.Runway runway)
        {
            if (e.Queued == null) return;
            runway.DequeueTakeoff(e.Queued);
            e.Queued = null;
        }

        private static Airbase.Runway RunwayOf(Entry e)
        {
            if (e.Airbase.runways == null) return null;
            foreach (Airbase.Runway r in e.Airbase.runways)
                if (r != null && r.index == e.Traffic.Runway.Index) return r;
            return null;
        }

        /// <summary>True when <paramref name="runway"/> is held by a Wing Command departure and <paramref name="querier"/>
        /// is not one of ours: the native takeoff queue must wait.</summary>
        public static bool LockedAgainst(Airbase.Runway runway, Aircraft querier)
        {
            if (runway == null || runway.airbase == null) return false;
            foreach (Entry e in fields)
                if (ReferenceEquals(e.Airbase, runway.airbase) && e.Traffic.Departures.RunwayLocked && e.Traffic.Runway.Index == runway.index)
                    return WingService.Instance == null || !WingService.Instance.IsMember(querier);
            return false;
        }

        /// <summary>The samples of every field in use this mission (diagnostics: replayed in the FlightSim).</summary>
        public static List<AirbaseSample> Samples()
        {
            var samples = new List<AirbaseSample>();
            foreach (Entry e in fields) samples.Add(e.Traffic.Field);
            return samples;
        }

        public static void Clear()
        {
            foreach (Entry e in fields)
                if (e.Airbase != null)
                {
                    Airbase.Runway runway = RunwayOf(e);
                    if (runway != null) Dequeue(e, runway);
                }
            fields.Clear();
        }

        private static void Refresh(Entry e, WingService wing)
        {
            FieldTraffic t = e.Traffic;
            t.Obstacles.Clear();
            float radius = t.Field.Radius > 0f ? t.Field.Radius : 3000f;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || a.disabled || a.radarAlt > ObstacleMaxRadarAlt || (wing != null && wing.IsMember(a))) continue;
                Vec3 p = a.GlobalPosition().ToVec3();
                if ((p - t.Field.Center).Horizontal.Length <= radius) t.Obstacles.Add(p);
            }
            bool landing = false;
            if (e.Airbase.runways != null)
                foreach (Airbase.Runway r in e.Airbase.runways)
                    if (r != null && r.index == t.Runway.Index && r.LandingsInProgress()) landing = true;
            t.Departures.NativeLandingPending = landing;
        }
    }
}
