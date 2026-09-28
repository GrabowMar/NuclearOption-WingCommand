using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    // Filled by the engine's gather (WmcDetail) and by the tests.
#pragma warning disable CS0649
    internal struct StoreLine
    {
        public string Name;
        public int Ammo, Full;
        public StoreClass Class;
    }

    /// <summary>What the deep card knows about one member (spec WMC program §4); NaN, -1 and null are unknown.</summary>
    internal struct MemberDetail
    {
        public float Fuel, Ammo, BingoSeconds, Damage;
        /// <summary>-1 unknown, 0 off, 1 on.</summary>
        public int Radar;
        public string Target, Callsign, Rank, Perks;
        public StoreLine[] Stores;
        public int StoreCount;
    }
#pragma warning restore CS0649

    /// <summary>The deep card's four lines: fuel and bingo, ammo, damage; radar and target; stores; pilot. Unknowns are a
    /// dash, never a made-up zero.</summary>
    internal static class DetailLines
    {
        public const int MaxStores = 6;

        public static void Build(in MemberDetail d, List<string> into)
        {
            into.Clear();
            string bingo = WingHudText.BingoTime(d.BingoSeconds);
            into.Add("FUEL " + WmcText.Percent(d.Fuel) + (bingo.Length > 0 ? "  " + bingo : "") +
                     "   AMMO " + WmcText.Percent(d.Ammo) + "   DMG " + WmcText.Percent(d.Damage));
            into.Add("RADAR " + (d.Radar < 0 ? WmcText.Unknown : d.Radar > 0 ? "ON" : "OFF") +
                     "   TGT " + (string.IsNullOrEmpty(d.Target) ? WmcText.Unknown : d.Target));
            var sb = new StringBuilder();
            int n = d.Stores == null ? 0 : System.Math.Min(System.Math.Min(d.StoreCount, d.Stores.Length), MaxStores);
            for (int i = 0; i < n; i++)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(d.Stores[i].Name).Append(' ')
                  .Append(d.Stores[i].Ammo.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(d.Stores[i].Full.ToString(CultureInfo.InvariantCulture));
            }
            into.Add(sb.Length > 0 ? sb.ToString() : "STORES " + WmcText.Unknown);
            sb.Clear();
            foreach (string part in new[] { d.Callsign, d.Rank, d.Perks })
            {
                if (string.IsNullOrEmpty(part)) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(part);
            }
            into.Add(sb.Length > 0 ? sb.ToString() : "PILOT " + WmcText.Unknown);
        }
    }
}
