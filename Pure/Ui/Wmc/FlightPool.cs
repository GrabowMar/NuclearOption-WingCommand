using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>What a store is for the flight pool (the 0.9 FLIGHT POOL card).</summary>
    internal enum StoreClass : byte { Other, AirMissile, StrikeMissile, Bomb, Gun, Ecm }

    internal static class StoreClasses
    {
        /// <summary>From the game's weapon flags: guns, jammers, bombs, and missiles by which role they are better at.</summary>
        public static StoreClass Of(bool gun, bool jammer, bool missile, bool bomb, float antiAir, float antiSurface)
        {
            if (gun) return StoreClass.Gun;
            if (jammer) return StoreClass.Ecm;
            if (missile) return antiAir >= antiSurface ? StoreClass.AirMissile : StoreClass.StrikeMissile;
            return bomb ? StoreClass.Bomb : StoreClass.Other;
        }
    }

    internal struct PoolTotals
    {
        public int AirMissiles, Agm, Bombs, GunRounds, Ecm, Aircraft;
    }

    /// <summary>TACTICAL's FLIGHT POOL (spec WMC rebuild §TACTICAL): what the scope still carries — missiles, strike stores,
    /// gun rounds, jammers — or, for one aircraft, its stations.</summary>
    internal static class FlightPool
    {
        public static void Add(ref PoolTotals t, in StoreLine s)
        {
            int left = s.Ammo > 0 ? s.Ammo : 0;
            switch (s.Class)
            {
                case StoreClass.AirMissile: t.AirMissiles += left; break;
                case StoreClass.StrikeMissile: t.Agm += left; break;
                case StoreClass.Bomb: t.Bombs += left; break;
                case StoreClass.Gun: t.GunRounds += left; break;
                case StoreClass.Ecm: t.Ecm += left; break;
            }
        }

        public static void Lines(in PoolTotals t, List<string> into)
        {
            into.Clear();
            if (t.AirMissiles > 0) into.Add("MISSILES  " + N(t.AirMissiles) + " READY");
            if (t.Bombs > 0 || t.Agm > 0)
            {
                string bombs = t.Bombs > 0 ? N(t.Bombs) + " BOMBS" : "", agm = t.Agm > 0 ? N(t.Agm) + " AGM" : "";
                into.Add("STRIKE  " + (bombs.Length > 0 && agm.Length > 0 ? bombs + " · " + agm : bombs + agm));
            }
            if (t.GunRounds > 0) into.Add("GUN  " + N(t.GunRounds) + " RDS");
            if (t.Ecm > 0) into.Add("ECM  " + N(t.Ecm) + " PODS");
            if (into.Count == 0) into.Add("NO STORES");
        }

        public static void StationLines(in MemberDetail d, List<string> into)
        {
            into.Clear();
            int n = d.Stores == null ? 0 : System.Math.Min(d.StoreCount, d.Stores.Length);
            for (int i = 0; i < n; i++)
                into.Add("ST" + N(i + 1) + "  " + d.Stores[i].Name + "  " + N(d.Stores[i].Ammo) + "/" + N(d.Stores[i].Full));
            if (into.Count == 0) into.Add("NO STORES");
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
