using System.Globalization;

namespace WingCommand
{
    /// <summary>The bezel header's words (spec WMC rebuild §bezel shell): the title, four chips with their state class
    /// (live, warn, danger, info, inert — the word carries the state, the colour repeats it) and the metric keys per tab.</summary>
    internal static class WmcHeader
    {
        /// <summary>A chip's text budget (97 px label at 10 px).</summary>
        public const int ChipChars = 16;

        private static readonly string[][][] keys =
        {
            new[] { new[] { "FUEL MIN", "%" }, new[] { "AMMO MIN", "%" }, new[] { "THREAT", "" } },
            new[] { new[] { "FUNDS", "CR" }, new[] { "RESERVE", "" }, new[] { "STOCK", "" } },
            new[] { new[] { "STATIONS", "" }, new[] { "MASS", "kg" }, new[] { "ROLE", "" } },
            new[] { new[] { "PILOTS", "" }, new[] { "READY", "" }, new[] { "LOST", "" } },
        };

        public static string Title(int count, int max, int airborne)
        {
            if (count <= 0) return "NO WING";
            return "WING " + N(count) + "/" + N(max) + " · " + (airborne > 0 ? N(airborne) : "NONE") + " AIRBORNE";
        }

        /// <summary>Members reporting against members owned (inbound deliveries included); LOST when a client's copy is stale.</summary>
        public static string Link(int live, int pending, bool client, bool stale, out string state)
        {
            if (client && stale)
            {
                state = "danger";
                return "LINK LOST";
            }
            int total = live + pending;
            state = total == 0 ? "inert" : pending > 0 ? "warn" : "live";
            return "LINK " + N(live) + "/" + N(total);
        }

        public static string Profile(string name, bool mixed, out string state)
        {
            state = mixed ? "warn" : "info";
            return "PROFILE " + (mixed ? "MIXED" : name ?? WmcText.Unknown);
        }

        public static string Reserve(int held, int capacity, bool offline, out string state)
        {
            if (offline)
            {
                state = "inert";
                return "RESV " + WmcText.Unknown;
            }
            state = held > 0 ? "info" : "inert";
            return "RESV " + N(held) + "/" + N(capacity);
        }

        /// <summary>The armed map order, else who runs the wing here.</summary>
        public static string Mode(MapMode armed, bool client, out string state)
        {
            if (armed != MapMode.Off)
            {
                state = "warn";
                return "ARMED " + MapOrders.Label(armed);
            }
            state = client ? "info" : "live";
            return client ? "CLIENT" : "HOST";
        }

        /// <summary>{key, unit} of the three metric tiles on tab <paramref name="tab"/> (0 TACTICAL … 3 WING).</summary>
        public static string[][] Keys(int tab) => keys[tab < 0 ? 0 : tab >= keys.Length ? keys.Length - 1 : tab];

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
