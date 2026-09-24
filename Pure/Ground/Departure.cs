using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Where a departure group lines up (spec M3 §3.4): up to <see cref="MaxAbreast"/> aircraft per row, as many as
    /// fit the runway width at span + <see cref="SideMargin"/> each (at least one); lanes centred on the centreline;
    /// rows <see cref="RowGap"/> apart, the first row furthest down the runway and the last
    /// <see cref="ThresholdMargin"/> past the threshold.</summary>
    internal static class LineupPlanner
    {
        public static float SideMargin = 10f, RowGap = 30f, ThresholdMargin = 40f;
        public static int MaxAbreast = 4;

        public static int Abreast(float runwayWidth, float span) =>
            Math.Max(1, Math.Min(MaxAbreast, (int)Math.Floor(runwayWidth / Math.Max(1f, span + SideMargin))));

        public static Vec3 Slot(RunwaySample r, bool reverse, int row, int column, int abreast, int rows)
        {
            Vec3 threshold = reverse ? r.End : r.Start;
            Vec3 dir = r.Direction(reverse);
            Vec3 side = Vec3.Cross(Vec3.Up, dir);
            float along = ThresholdMargin + (Math.Max(1, rows) - 1 - row) * RowGap;
            float lane = r.Width / Math.Max(1, abreast);
            float lateral = (column - 0.5f * (abreast - 1)) * lane;
            return threshold + dir * along + side * lateral;
        }
    }

    /// <summary>One field's departures (spec M3 §3.4–3.5). Expected members gather at the hold-short; the group lines
    /// up once all have arrived (or <see cref="GatherTimeout"/> after the first), taking the runway lock (never while a
    /// native landing is pending or a foreign aircraft is on the runway, <see cref="RunwayBusy"/>). The group is the
    /// queue at that moment, its <see cref="Abreast"/> the fewest any of its
    /// types allows; both stay fixed until the lock is released, and a member arriving later waits for the next group.
    /// Members line up one at a time as the previous clears the threshold, whoever of the group reaches the hold-short
    /// next, each into the next slot (the first row furthest down the runway); rows roll in order, each
    /// <see cref="RowInterval"/> after the previous and never into a busy runway; the lock is released when the last
    /// member of the group is airborne. A member removed on the ground (lost, released) never blocks its row.</summary>
    internal sealed class DepartureSequencer
    {
        public static float RowInterval = 10f, GatherTimeout = 90f, ClearHeight = 75f;

        public bool NativeLandingPending, RunwayBusy;
        public bool RunwayLocked { get; private set; }

        private readonly List<int> expected = new List<int>();
        private readonly List<int> queue = new List<int>();
        private readonly List<int> group = new List<int>(), order = new List<int>();
        private readonly Dictionary<int, int> abreastOf = new Dictionary<int, int>();
        private readonly HashSet<int> linedUp = new HashSet<int>(), airborne = new HashSet<int>(), removed = new HashSet<int>();
        private readonly HashSet<int> cleared = new HashSet<int>();
        private readonly Dictionary<int, float> rowRolledAt = new Dictionary<int, float>();
        private float firstArrival = float.NaN;
        private int lockedAbreast = 1;

        /// <summary>Aircraft per row: fixed for the locked group, else the fewest any expected member's type allows.</summary>
        public int Abreast => RunwayLocked ? lockedAbreast : Fewest(expected, queue);

        public int Rows
        {
            get
            {
                List<int> members = RunwayLocked ? group : queue;
                return members.Count == 0 ? 0 : (members.Count - 1) / Abreast + 1;
            }
        }

        /// <summary>A member will depart; <paramref name="abreast"/> is how many of its type fit the runway side by side.</summary>
        public void Expect(int owner, int abreast)
        {
            if (!expected.Contains(owner)) expected.Add(owner);
            abreastOf[owner] = Math.Max(1, abreast);
        }

        /// <summary>The member holds short; returns its place in the queue.</summary>
        public int Enqueue(int owner, float time)
        {
            if (float.IsNaN(firstArrival)) firstArrival = time;
            if (!queue.Contains(owner)) queue.Add(owner);
            return queue.IndexOf(owner);
        }

        public bool MayLineUp(int owner, float time)
        {
            if (!queue.Contains(owner)) return false;
            if (!RunwayLocked)
            {
                if (NativeLandingPending || RunwayBusy) return false;
                if (float.IsNaN(firstArrival)) firstArrival = time;
                bool gathered = true;
                foreach (int o in expected)
                    if (!removed.Contains(o) && !queue.Contains(o)) gathered = false;
                if (!gathered && time - firstArrival < GatherTimeout) return false;
                group.Clear();
                group.AddRange(queue);
                lockedAbreast = Fewest(group, group);
                RunwayLocked = true;
            }
            if (!group.Contains(owner)) return false;
            if (order.Contains(owner)) return true;
            foreach (int o in order)
                if (!cleared.Contains(o) && !removed.Contains(o)) return false;
            order.Add(owner);
            return true;
        }

        /// <summary>The member is well past the threshold on its way to its slot: the next may line up.</summary>
        public void ClearedThreshold(int owner) => cleared.Add(owner);

        /// <summary>The member's slot: its place in the lineup order (the next free slot before it lines up).</summary>
        public void SlotOf(int owner, out int row, out int column)
        {
            int i = order.IndexOf(owner);
            if (i < 0) i = order.Count;
            row = i / Abreast;
            column = i % Abreast;
        }

        public void LinedUp(int owner) => linedUp.Add(owner);

        /// <summary>The member's row is complete on the runway and the previous row rolled at least an interval ago.</summary>
        public bool MayRoll(int owner, float time)
        {
            SlotOf(owner, out int row, out _);
            if (rowRolledAt.ContainsKey(row)) return true;
            if (RunwayBusy) return false;
            int abreast = Abreast, pending = 0;
            foreach (int o in group)
                if (!order.Contains(o) && !removed.Contains(o)) pending++;
            for (int i = row * abreast; i < Math.Min(group.Count, (row + 1) * abreast); i++)
            {
                if (i >= order.Count)
                {
                    // Slots beyond the lineup so far: still to be filled while members are on their way.
                    if (i < order.Count + pending) return false;
                    continue;
                }
                if (!linedUp.Contains(order[i]) && !removed.Contains(order[i])) return false;
            }
            if (row > 0 && (!rowRolledAt.TryGetValue(row - 1, out float previous) || time - previous < RowInterval)) return false;
            rowRolledAt[row] = time;
            return true;
        }

        public void Airborne(int owner)
        {
            airborne.Add(owner);
            ReleaseWhenDone();
        }

        /// <summary>The member will not depart after all (lost, released, recalled): before the lock it is forgotten;
        /// in a locked group it never blocks its row.</summary>
        public void Remove(int owner)
        {
            if (!RunwayLocked || !group.Contains(owner))
            {
                Forget(owner);
                return;
            }
            removed.Add(owner);
            ReleaseWhenDone();
        }

        private int Fewest(List<int> a, List<int> b)
        {
            int fewest = LineupPlanner.MaxAbreast;
            foreach (int o in a)
                if (!removed.Contains(o) && abreastOf.TryGetValue(o, out int n)) fewest = Math.Min(fewest, n);
            foreach (int o in b)
                if (!removed.Contains(o) && abreastOf.TryGetValue(o, out int n)) fewest = Math.Min(fewest, n);
            return Math.Max(1, fewest);
        }

        private void ReleaseWhenDone()
        {
            if (!RunwayLocked) return;
            foreach (int o in group)
                if (!airborne.Contains(o) && !removed.Contains(o)) return;
            RunwayLocked = false;
            foreach (int o in group) Forget(o);
            foreach (int o in removed) Forget(o);
            group.Clear();
            order.Clear();
            linedUp.Clear();
            cleared.Clear();
            airborne.Clear();
            removed.Clear();
            rowRolledAt.Clear();
            firstArrival = float.NaN;
        }

        private void Forget(int owner)
        {
            queue.Remove(owner);
            expected.Remove(owner);
            abreastOf.Remove(owner);
        }
    }
}
