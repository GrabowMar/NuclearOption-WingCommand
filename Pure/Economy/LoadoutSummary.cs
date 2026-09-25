namespace WingCommand
{
    /// <summary>What a fitted store is, for the summary and the role.</summary>
    internal enum StoreKind : byte { None, AirToAir, AirToGround, Bomb, Ecm, Cargo, MissileDefence, Other }

    /// <summary>One set's store as the page resolved it: a key at all, one this build knows, and what it is.</summary>
    internal struct StoreFacts
    {
        public bool HasKey, Known;
        /// <summary>A known store this mission refuses (restricted, nuclear not yet, …): it launches empty here.</summary>
        public bool Refused;
        public StoreKind Kind;
        /// <summary>Per pylon (WeaponMount.mass is per hardpoint).</summary>
        public float Mass;
        public int Ammo;
    }

    /// <summary>A fit in numbers: stations (rows), how many carry a store that flies, how many hold one the launch empties
    /// (blocked) or this build does not know, the mass of what flies (every pylon) and its rounds by kind.</summary>
    internal struct FitSummary
    {
        public int Stations, Fitted, Blocked, Unknown, Refused;
        public float Mass;
        public int Aam, Agm, Bombs, Ecm, Cargo, MslDef;
    }

    internal static class LoadoutSummary
    {
        /// <summary>The fit of <paramref name="facts"/> (one per set) on <paramref name="layout"/>, as it would launch.</summary>
        public static FitSummary Of(StationLayout layout, StoreFacts[] facts)
        {
            int sets = layout.Sets;
            var fitted = new bool[sets];
            var cleared = new bool[sets];
            for (int s = 0; s < sets && s < facts.Length; s++) fitted[s] = facts[s].Known && !facts[s].Refused;
            layout.WillClear(fitted, cleared);
            var sum = new FitSummary { Stations = layout.Stations };
            for (int st = 0; st < layout.Stations; st++)
            {
                bool flies = false, blocked = false, unknown = false, refused = false;
                for (int s = layout.First(st); s < layout.End(st) && s < facts.Length; s++)
                {
                    StoreFacts f = facts[s];
                    if (f.HasKey && !f.Known) unknown = true;
                    if (!f.Known) continue;
                    if (f.Refused)
                    {
                        refused = true;
                        continue;
                    }
                    if (cleared[s])
                    {
                        blocked = true;
                        continue;
                    }
                    flies = true;
                    int pylons = layout.PylonsAt(s);
                    sum.Mass += f.Mass * pylons;
                    Add(ref sum, f.Kind, f.Ammo * pylons);
                }
                if (flies) sum.Fitted++;
                else if (blocked) sum.Blocked++;
                else if (refused) sum.Refused++;
                if (unknown) sum.Unknown++;
            }
            return sum;
        }

        private static void Add(ref FitSummary s, StoreKind kind, int rounds)
        {
            switch (kind)
            {
                case StoreKind.AirToAir: s.Aam += rounds; break;
                case StoreKind.AirToGround: s.Agm += rounds; break;
                case StoreKind.Bomb: s.Bombs += rounds; break;
                case StoreKind.Ecm: s.Ecm += rounds; break;
                case StoreKind.Cargo: s.Cargo += rounds; break;
                case StoreKind.MissileDefence: s.MslDef += rounds; break;
            }
        }
    }
}
