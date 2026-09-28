using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Who each attack order has (spec WMC rebuild §AI R8, A3: attack sets per element): up to <see cref="MaxSets"/> orders at
    /// once, each claiming the members it engaged. The newest claim on a member wins — it leaves any older set — and a set left with
    /// nobody ends. With every set in use, a new claim takes the oldest set's place. No allocation after construction.</summary>
    internal sealed class AttackClaims
    {
        public const int MaxSets = WingScope.MaxElements, MaxMembers = 32;
        private readonly uint[,] members = new uint[MaxSets, MaxMembers];
        private readonly int[] count = new int[MaxSets];
        private readonly long[] made = new long[MaxSets];
        private long clock;

        public bool Active(int set) => set >= 0 && set < MaxSets && count[set] > 0;

        public int Count(int set) => set >= 0 && set < MaxSets ? count[set] : 0;

        public uint Member(int set, int i) => members[set, i];

        /// <summary>The set that has member <paramref name="id"/>, or -1.</summary>
        public int SetOf(uint id)
        {
            for (int s = 0; s < MaxSets; s++)
                for (int i = 0; i < count[s]; i++)
                    if (members[s, i] == id) return s;
            return -1;
        }

        /// <summary>A new order's members (they leave any older set): its set, or -1 for nobody.</summary>
        public int Claim(IReadOnlyList<uint> ids)
        {
            if (ids == null || ids.Count == 0) return -1;
            Release(ids);
            int set = -1;
            for (int s = 0; s < MaxSets && set < 0; s++)
                if (count[s] == 0) set = s;
            if (set < 0)
            {
                set = 0;
                for (int s = 1; s < MaxSets; s++)
                    if (made[s] < made[set]) set = s;
                count[set] = 0;
            }
            foreach (uint id in ids)
                if (count[set] < MaxMembers) members[set, count[set]++] = id;
            made[set] = ++clock;
            return set;
        }

        /// <summary>These members are in no set any more (they fight on their own choices, or were taken back).</summary>
        public void Release(IReadOnlyList<uint> ids)
        {
            if (ids == null) return;
            foreach (uint id in ids) Release(id);
        }

        public void Release(uint id)
        {
            for (int s = 0; s < MaxSets; s++)
                for (int i = 0; i < count[s]; i++)
                    if (members[s, i] == id)
                    {
                        members[s, i] = members[s, --count[s]];
                        break;
                    }
        }

        public void End(int set)
        {
            if (set >= 0 && set < MaxSets) count[set] = 0;
        }

        public void ClearAll() => Array.Clear(count, 0, count.Length);
    }
}
