using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>One PERKS card on the dossier.</summary>
    internal struct PerkCard
    {
        public string Title, Line, Tip;
        public bool Owned, Locked, Inactive;
    }

    /// <summary>The dossier's PERKS 2×2 (spec WMC rebuild §WING; critique §8.5): owned perks in order with a short line (the full description
    /// in the tooltip), the ones not wired in 1.0 marked, locked slots naming the rank and XP that earn them (slot n opens at rank n, or at
    /// once with Talented), and a fourth card that lists the rest by name when a pilot has more than four.</summary>
    internal static class PerkCards
    {
        public const int LineChars = 64, Slots = 4;

        public static bool Active(PilotPerk p) => !PilotPerks.Description(p).StartsWith("Not active", StringComparison.Ordinal);

        /// <summary>The description's first sentence, cut at a word to fit two card lines.</summary>
        public static string Short(PilotPerk p)
        {
            string d = PilotPerks.Description(p);
            int end = d.IndexOf(". ", StringComparison.Ordinal);
            if (end < 0 && d.EndsWith(".", StringComparison.Ordinal)) end = d.Length - 1;
            string first = end > 0 ? d.Substring(0, end) : d;
            int colon = first.IndexOf(": ", StringComparison.Ordinal);
            if (!Active(p) && colon > 0) first = first.Substring(0, colon);
            return WmcText.Cut(first, LineChars);
        }

        public static PerkCard For(int slot, IReadOnlyList<PilotPerk> owned, WingRank rank)
        {
            int count = owned != null ? owned.Count : 0;
            if (count > Slots && slot == Slots - 1)
            {
                var names = new StringBuilder();
                for (int i = Slots - 1; i < count; i++) names.Append(i > Slots - 1 ? ", " : "").Append(PilotPerks.Name(owned[i]));
                return new PerkCard
                {
                    Title = "+" + N(count - (Slots - 1)) + " MORE", Line = WmcText.Cut(names.ToString(), LineChars), Tip = names.ToString(), Owned = true,
                };
            }
            if (slot < count)
            {
                PilotPerk p = owned[slot];
                return new PerkCard
                {
                    Title = PilotPerks.Name(p).ToUpperInvariant(), Line = Short(p), Tip = PilotPerks.Description(p), Owned = true, Inactive = !Active(p),
                };
            }
            bool talented = owned != null && Contains(owned, PilotPerk.Talented);
            int at = Math.Max(0, slot + 1 - (talented ? 3 : 0));
            if (at > (int)WingRank.Legend) at = (int)WingRank.Legend;
            var earn = (WingRank)at;
            return new PerkCard
            {
                Title = "SLOT " + N(slot + 1) + " · " + PilotPerks.RankName(earn), Line = "EARNED AT " + PilotPerks.XpForRank(earn).ToString("N0",
                    CultureInfo.InvariantCulture) + " XP", Tip = "A perk comes with each rank; this slot opens at " + PilotPerks.RankName(earn) + ".",
                Locked = true,
            };
        }

        public static string Head(int owned, WingRank rank, bool kia, bool off)
        {
            if (off) return "OFF IN SETTINGS";
            if (kia) return "RECORD CLOSED";
            string head = N(owned) + " OF " + N(Math.Max(Slots, owned));
            return rank >= WingRank.Legend ? head
                : head + " · NEXT AT " + PilotPerks.XpForRank(rank + 1).ToString("N0", CultureInfo.InvariantCulture) + " XP";
        }

        private static bool Contains(IReadOnlyList<PilotPerk> owned, PilotPerk p)
        {
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] == p) return true;
            return false;
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
