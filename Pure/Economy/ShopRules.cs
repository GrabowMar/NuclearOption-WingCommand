using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>What a requisition over the faction's AI aircraft limit does (user decision 2026-09-25, config
    /// Supply/OverLimit): costs three times the value, lets every enemy faction field one more AI aircraft, or sends one of the
    /// faction's own AI to land to make room.</summary>
    public enum OverLimitMode : byte { Surcharge, MatchEnemy, RtbOne }

    /// <summary>NEAREST launches from the picked field when it is ON, else the nearest ON field; ANY takes the nearest ON field
    /// with a hangar ready for the type, else as NEAREST.</summary>
    internal enum LaunchMode : byte { Nearest, Any }

    /// <summary>Why no requisition can go now, for the whole wing: said once, on the DISPATCH card, never on a tile.</summary>
    internal enum WingBlock : byte { None, Client, NotFlying, NoFaction, WingFull, NoBaseOn, NotOffered, NoAiToSendHome }

    /// <summary>Why this airframe cannot go: its tile's own reason.</summary>
    internal enum TileBlock : byte { None, Rank, NoStock, NoBase, Funds }

    /// <summary>The wing's side of a requisition, snapshot once per refresh and again at execution.</summary>
    internal struct ShopWing
    {
        public bool Host, Flying, HasFaction, Sandbox;
        public int Members, Pending, MaxMembers;
        /// <summary>The player's allocation (the game's millions).</summary>
        public float Funds;
        public int PlayerRank;
        /// <summary>The faction's live AI aircraft (our wingmen included) plus launches whose aircraft has not appeared.</summary>
        public int FactionAi;
        /// <summary>The game's limit: AIAircraftLimit + enemy players × add − friendly players × reduce.</summary>
        public float FactionAiLimit;
        public OverLimitMode Mode;
        public int BasesOn;
        /// <summary>RTB ONE has a faction AI aircraft it may send home.</summary>
        public bool RtbCandidate;
    }

    /// <summary>One airframe's side of a requisition.</summary>
    internal struct ShopAirframe
    {
        public float Value;
        public int RankRequired;
        /// <summary>Restricted by the mission, a VTOL (no AI state to adopt), or a placeholder: never listed.</summary>
        public bool Restricted, Vtol, Placeholder;
        public bool Jet;
        /// <summary>The faction's supply lists the type (even at none left).</summary>
        public bool Declared;
        public int FactionStock;
        /// <summary>Airframes of the type kept in the HANGAR store.</summary>
        public int Held;
        /// <summary>ON bases this class can launch from (a jet: no carrier).</summary>
        public int BasesForClass;
    }

    internal struct ShopBase
    {
        public bool On, Carrier, HangarReady, Picked;
        public float DistSq;
    }

    internal struct ShopQuote
    {
        public WingBlock Wing;
        public TileBlock Tile;
        public float Price;
        public bool OverLimit, FromHeld;

        public bool Allowed => Wing == WingBlock.None && Tile == TileBlock.None;
    }

    /// <summary>SUPPLY's rules for one requisition (spec WMC rebuild §SUPPLY; design wmc-rebuild/supply-shop-rules §4 as
    /// amended by supply-critic and the user's 2026-09-25 decisions). The page quotes every refresh and the executor quotes
    /// again before it launches, so both say the same words. Money is the game's millions, read as credits.</summary>
    internal static class ShopRules
    {
        public static float SurchargeMultiplier = 3f;

        public static bool Listed(in ShopAirframe a, bool sandbox) =>
            !a.Restricted && !a.Vtol && !a.Placeholder && (sandbox || a.Declared || a.Held > 0);

        /// <summary>The game deploys no more AI once its count reaches the limit (FactionHQ.DeployAIAircraft): neither do we
        /// without the mode's price.</summary>
        public static bool OverCap(in ShopWing w) => w.FactionAi >= w.FactionAiLimit;

        public static WingBlock WingBlocker(in ShopWing w)
        {
            if (!w.Host) return WingBlock.Client;
            if (!w.Flying) return WingBlock.NotFlying;
            if (!w.HasFaction) return WingBlock.NoFaction;
            if (w.Members + w.Pending >= w.MaxMembers) return WingBlock.WingFull;
            if (w.BasesOn <= 0) return WingBlock.NoBaseOn;
            if (OverCap(w) && w.Mode == OverLimitMode.RtbOne && !w.RtbCandidate) return WingBlock.NoAiToSendHome;
            return WingBlock.None;
        }

        public static ShopQuote Quote(in ShopWing w, in ShopAirframe a)
        {
            // Review R4a: the sandbox never takes a stored airframe (SpawnService launches it from the faction as ever).
            var q = new ShopQuote { Wing = WingBlocker(w), OverLimit = OverCap(w), FromHeld = !w.Sandbox && a.Held > 0 };
            if (q.Wing == WingBlock.None && !Listed(a, w.Sandbox)) q.Wing = WingBlock.NotOffered;
            q.Price = w.Sandbox ? 0f : a.Value * (q.OverLimit && w.Mode == OverLimitMode.Surcharge ? SurchargeMultiplier : 1f);
            if (a.RankRequired > w.PlayerRank) q.Tile = TileBlock.Rank;
            else if (!w.Sandbox && a.FactionStock + a.Held <= 0) q.Tile = TileBlock.NoStock;
            else if (a.BasesForClass <= 0) q.Tile = TileBlock.NoBase;
            else if (w.Funds < q.Price) q.Tile = TileBlock.Funds;
            return q;
        }

        /// <summary>The wing once this call has launched <paramref name="launched"/> aircraft for <paramref name="spent"/> (review
        /// R4a: each aircraft of a call is quoted with the ones before it, so the one that crosses the faction's AI limit pays and
        /// acts as over it; arithmetic, since the game's own count lags a spawn by a tick).</summary>
        public static ShopWing After(in ShopWing w, int launched, float spent)
        {
            ShopWing next = w;
            next.FactionAi += launched;
            next.Pending += launched;
            next.Funds -= spent;
            return next;
        }

        /// <summary>MATCH ENEMY: the enemy AI slots <paramref name="n"/> more aircraft call for, beyond the <paramref name="given"/>
        /// already handed out this mission (review R4a: only what the faction is over by now, so a lost wingman's replacement
        /// never stacks another).</summary>
        public static int EnemyBonus(in ShopWing w, int n, int given)
        {
            int over = w.FactionAi + n - Limit(w);
            return over > given ? over - given : 0;
        }

        public static int PickBase(LaunchMode mode, bool jet, ShopBase[] bases, int count)
        {
            int nearest = -1, picked = -1, ready = -1;
            for (int i = 0; i < count; i++)
            {
                ShopBase b = bases[i];
                if (!b.On || (jet && b.Carrier)) continue;
                if (nearest < 0 || b.DistSq < bases[nearest].DistSq) nearest = i;
                if (b.Picked) picked = i;
                if (b.HangarReady && (ready < 0 || b.DistSq < bases[ready].DistSq)) ready = i;
            }
            if (mode == LaunchMode.Any && ready >= 0) return ready;
            return picked >= 0 ? picked : nearest;
        }

        public static int NextFuel(int percent) => percent == 25 ? 50 : percent == 50 ? 75 : percent == 75 ? 100 : percent == 100 ? 25 : 100;

        // ---- words

        public static string WingReason(WingBlock b, in ShopWing w)
        {
            switch (b)
            {
                case WingBlock.Client: return "HOST ONLY · the host requisitions";
                case WingBlock.NotFlying: return "Not flying";
                case WingBlock.NoFaction: return "No faction";
                case WingBlock.WingFull:
                    return "Wing is full (" + N(w.Members) + "/" + N(w.MaxMembers) + (w.Pending > 0 ? " · " + N(w.Pending) + " inbound" : "") + ")";
                case WingBlock.NoBaseOn: return "No launch base is ON";
                case WingBlock.NotOffered: return "Not offered here any more";
                case WingBlock.NoAiToSendHome: return "Faction AI limit (" + Cap(w) + ") · no AI can go home";
                default: return "";
            }
        }

        /// <summary>The tile's foot: its own reason, else its price and how many are left (≤ 18 characters); in the sandbox,
        /// where stock is not counted, FREE · SANDBOX (review R4a).</summary>
        public static string TileFoot(in ShopQuote q, in ShopAirframe a, bool sandbox)
        {
            switch (q.Tile)
            {
                case TileBlock.Rank: return "RANK " + N(a.RankRequired);
                case TileBlock.NoStock: return "NO STOCK";
                case TileBlock.NoBase: return "NO BASE";
                case TileBlock.Funds: return "NEED " + Credits.Text(q.Price);
                default: return sandbox ? "FREE · SANDBOX" : Credits.Price(q.Price) + " · " + N(a.FactionStock + a.Held) + " LEFT";
            }
        }

        /// <summary>"BLOCKED · …": the wing-wide reason first, else the airframe's own (null when it may go).</summary>
        public static string Blocker(in ShopQuote q, in ShopWing w, in ShopAirframe a)
        {
            if (q.Wing != WingBlock.None) return "BLOCKED · " + WingReason(q.Wing, w);
            switch (q.Tile)
            {
                case TileBlock.Rank: return "BLOCKED · needs rank " + N(a.RankRequired) + " (you are " + N(w.PlayerRank) + ")";
                case TileBlock.NoStock: return "BLOCKED · none left in stock";
                case TileBlock.NoBase: return "BLOCKED · no ON base can launch it";
                case TileBlock.Funds: return "BLOCKED · needs " + Credits.Text(q.Price) + ", you have " + Credits.Text(w.Funds);
                default: return null;
            }
        }

        public static string State(in ShopQuote q, bool selected, int inbound, out string state)
        {
            if (!selected)
            {
                state = "inert";
                return "NO AIRFRAME";
            }
            if (!q.Allowed)
            {
                state = "warn";
                return "BLOCKED";
            }
            if (inbound > 0)
            {
                state = "info";
                return "INBOUND " + N(inbound);
            }
            state = "live";
            return "READY";
        }

        /// <summary>"HATCH · FIT AUTO · FUEL 100% · FROM NORTH BOSCALI AB" (≤ 79 with a 14-character callsign, a 16-character
        /// fit and a 20-character base).</summary>
        public static string DispatchLine(string pilot, string fit, int fuelPercent, string baseName) =>
            (string.IsNullOrEmpty(pilot) ? "NEW PILOT" : pilot) + " · FIT " + fit + " · FUEL " + N(fuelPercent) + "% · "
            + (string.IsNullOrEmpty(baseName) ? "NO BASE" : "FROM " + baseName.ToUpperInvariant());

        /// <summary>The limit and, when over it, what the mode does.</summary>
        public static string OverLimitNote(in ShopWing w)
        {
            if (!OverCap(w)) return "AI " + N(w.FactionAi) + "/" + N(Limit(w));
            switch (w.Mode)
            {
                case OverLimitMode.MatchEnemy: return "OVER LIMIT (" + Cap(w) + ") · ENEMY +1";
                case OverLimitMode.RtbOne: return "OVER LIMIT (" + Cap(w) + ") · 1 AI TO BASE";
                default:
                    // Review R4a: the sandbox charges nothing, the surcharge included.
                    return "OVER LIMIT (" + Cap(w) + ") · " + (w.Sandbox ? "SANDBOX, NO ×" : "×")
                        + SurchargeMultiplier.ToString("0.#", CultureInfo.InvariantCulture) + (w.Sandbox ? "" : " COST");
            }
        }

        // ---- the HANGAR store (user decision 2026-09-25)

        /// <summary>Why STORE cannot take one now; the sandbox never launches from the HANGAR, so it stores nothing (review R4b).</summary>
        public static string StoreBlock(bool host, bool faction, bool sandbox, int count, int capacity, int factionStock)
        {
            if (!host) return "Host only";
            if (!faction) return "No faction";
            if (sandbox) return "The sandbox never uses the HANGAR";
            if (count >= capacity) return "The hangar is full (" + N(count) + "/" + N(capacity) + ") · return one first";
            return factionStock <= 0 ? "None left in the faction's stock" : null;
        }

        public static string ReturnBlock(bool host, bool faction, int stored)
        {
            if (!host) return "Host only";
            if (!faction) return "No faction";
            return stored <= 0 ? "None of this type is in the hangar" : null;
        }

        private static int Limit(in ShopWing w) => (int)Math.Ceiling(w.FactionAiLimit);

        private static string Cap(in ShopWing w) => N(w.FactionAi) + "/" + N(Limit(w));

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
