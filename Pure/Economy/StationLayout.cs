using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>An airframe's hardpoints as LOADOUT shows them (spec WMC rebuild §LOADOUT; critique 14): a STATION is one table row — a
    /// hardpoint set plus the sets after it that mirror it (SymmetryWithPrev) — counted once everywhere; a PYLON is one physical
    /// hardpoint. Picks follow the game's own editor: every set of the station takes the store, stations the new store blocks are
    /// emptied, and a blocked station takes nothing. <see cref="WillClear"/> predicts what the launch's own check empties.</summary>
    internal sealed class StationLayout
    {
        private readonly string[] names, pairNames;
        private readonly int[] pylons, firstOf, stationOf;
        private readonly int[][] precludes;

        public StationLayout(string[] names, string[] pairNames, bool[] withPrev, int[] pylons, int[][] precludes)
        {
            Sets = names?.Length ?? 0;
            this.names = names ?? new string[0];
            this.pairNames = pairNames ?? new string[Sets];
            this.pylons = pylons ?? new int[Sets];
            this.precludes = precludes ?? new int[Sets][];
            stationOf = new int[Sets];
            var first = new List<int>();
            for (int s = 0; s < Sets; s++)
            {
                bool mirrors = s > 0 && withPrev != null && s < withPrev.Length && withPrev[s];
                if (!mirrors) first.Add(s);
                stationOf[s] = first.Count - 1;
                Pylons += s < this.pylons.Length ? Math.Max(0, this.pylons[s]) : 0;
            }
            firstOf = first.ToArray();
        }

        public int Sets { get; }
        public int Stations => firstOf.Length;
        public int Pylons { get; }

        public int First(int st) => firstOf[st];

        /// <summary>One past the station's last set.</summary>
        public int End(int st) => st + 1 < firstOf.Length ? firstOf[st + 1] : Sets;

        public int StationOf(int set) => stationOf[set];

        public int PylonsAt(int set) => set < pylons.Length ? Math.Max(0, pylons[set]) : 0;

        public int PylonsOf(int st)
        {
            int n = 0;
            for (int s = First(st); s < End(st); s++) n += PylonsAt(s);
            return n;
        }

        /// <summary>The pair's name, else the first set's name without a leading LEFT/RIGHT (a pair's own name is often blank), else
        /// "STATION n".</summary>
        public string Name(int st)
        {
            int s = First(st);
            bool pair = End(st) - s > 1;
            if (pair && !string.IsNullOrWhiteSpace(Get(pairNames, s))) return pairNames[s].Trim();
            string n = (Get(names, s) ?? "").Trim();
            if (pair) n = StripSide(n);
            return n.Length > 0 ? n : "STATION " + (st + 1);
        }

        public string KeyOf(IReadOnlyList<string> keys, int st)
        {
            int s = First(st);
            return keys != null && s < keys.Count ? keys[s] : null;
        }

        /// <summary>Fits <paramref name="key"/> on every set of the station and empties the stations it blocks; returns how many it
        /// emptied, or −1 (and changes nothing) when the station is itself blocked.</summary>
        public int Pick(List<string> keys, int st, string key)
        {
            Normalize(keys);
            if (BlockedBy(keys, st) >= 0) return -1;
            for (int s = First(st); s < End(st); s++) keys[s] = key;
            if (string.IsNullOrEmpty(key)) return 0;
            int emptied = 0;
            for (int o = 0; o < Stations; o++)
            {
                if (o == st || string.IsNullOrEmpty(KeyOf(keys, o)) || !Precludes(o, st)) continue;
                Clear(keys, o);
                emptied++;
            }
            return emptied;
        }

        public void Clear(List<string> keys, int st)
        {
            Normalize(keys);
            for (int s = First(st); s < End(st); s++) keys[s] = null;
        }

        /// <summary>The station whose store keeps this one empty, or −1.</summary>
        public int BlockedBy(IReadOnlyList<string> keys, int st)
        {
            if (keys == null) return -1;
            for (int s = First(st); s < End(st); s++)
            {
                int[] p = Get(precludes, s);
                if (p == null) continue;
                foreach (int j in p)
                    if (j >= 0 && j < keys.Count && j < Sets && StationOf(j) != st && !string.IsNullOrEmpty(keys[j])) return StationOf(j);
            }
            return -1;
        }

        /// <summary>One key per set, and a mirrored pair carrying its first set's store.</summary>
        public void Normalize(List<string> keys)
        {
            while (keys.Count < Sets) keys.Add(null);
            for (int st = 0; st < Stations; st++)
                for (int s = First(st) + 1; s < End(st); s++) keys[s] = keys[First(st)];
        }

        /// <summary>The launch's own check (WingLoadoutCatalog.ClearBlockedMounts): sets in order, a fitted set any fitted set it lists
        /// blocks is emptied at once, repeated until stable — so of two stores that block each other the lower goes. Marks
        /// <paramref name="cleared"/> per set and returns how many.</summary>
        public int WillClear(bool[] fitted, bool[] cleared)
        {
            var live = (bool[])fitted.Clone();
            Array.Clear(cleared, 0, cleared.Length);
            int n = 0;
            bool changed;
            do
            {
                changed = false;
                for (int s = 0; s < Sets && s < live.Length; s++)
                {
                    if (!live[s]) continue;
                    int[] p = Get(precludes, s);
                    if (p == null) continue;
                    foreach (int j in p)
                    {
                        if (j < 0 || j >= live.Length || !live[j]) continue;
                        live[s] = false;
                        cleared[s] = true;
                        n++;
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);
            return n;
        }

        /// <summary>Whether any set of station <paramref name="blocked"/> lists a set of station <paramref name="by"/>.</summary>
        private bool Precludes(int blocked, int by)
        {
            for (int s = First(blocked); s < End(blocked); s++)
            {
                int[] p = Get(precludes, s);
                if (p == null) continue;
                foreach (int j in p)
                    if (j >= 0 && j < Sets && StationOf(j) == by) return true;
            }
            return false;
        }

        private static string StripSide(string n)
        {
            foreach (string side in new[] { "Left ", "Right " })
                if (n.StartsWith(side, StringComparison.OrdinalIgnoreCase)) return n.Substring(side.Length).TrimStart();
            return n;
        }

        private static T Get<T>(T[] a, int i) => a != null && i < a.Length ? a[i] : default;
    }
}
