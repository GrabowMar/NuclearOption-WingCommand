using System.Globalization;

namespace WingCommand
{
    /// <summary>The wing's vitals for the header chips: the minimum fuel and ammo, and the first member (by slot order in the rows)
    /// with each call.</summary>
    internal struct WingVitals
    {
        public int Count;
        public float MinFuel, MinAmmo;
        public int BingoSlot, JokerSlot, WinchesterSlot, DefendingSlot;

        public static WingVitals Of(SnapshotMember[] rows, int count)
        {
            var v = new WingVitals { MinFuel = float.NaN, MinAmmo = float.NaN, BingoSlot = -1, JokerSlot = -1, WinchesterSlot = -1, DefendingSlot = -1 };
            for (int i = 0; i < count && rows != null && i < rows.Length; i++)
            {
                SnapshotMember m = rows[i];
                v.Count++;
                float fuel = m.Fuel / 255f, ammo = m.Ammo / 255f;
                if (float.IsNaN(v.MinFuel) || fuel < v.MinFuel) v.MinFuel = fuel;
                if (float.IsNaN(v.MinAmmo) || ammo < v.MinAmmo) v.MinAmmo = ammo;
                var flags = (SnapshotFlags)m.Flags;
                if ((flags & SnapshotFlags.Bingo) != 0 && v.BingoSlot < 0) v.BingoSlot = m.Slot;
                if ((flags & SnapshotFlags.Joker) != 0 && v.JokerSlot < 0) v.JokerSlot = m.Slot;
                if ((flags & SnapshotFlags.Winchester) != 0 && v.WinchesterSlot < 0) v.WinchesterSlot = m.Slot;
                if ((MemberDuty)m.Duty == MemberDuty.Defending && v.DefendingSlot < 0) v.DefendingSlot = m.Slot;
            }
            return v;
        }
    }

    /// <summary>The bezel header's words (spec bezel v2 §3): the title and four vitals chips — FUEL, AMMO, THREAT, MODE — with their
    /// state class (live, warn, danger, info, inert). The word carries the state; the colour repeats it.</summary>
    internal static class WmcHeader
    {
        /// <summary>A chip's text budget (97 px label, autosized down to 10 px).</summary>
        public const int ChipChars = 15;

        public const string None = "—";

        /// <summary>The same line on every tab: the wing, who is up and who is coming; a client says whose wing it shows.</summary>
        public static string Title(int count, int max, int airborne, int inbound, bool client, bool stale, out string state)
        {
            string wing = "WING " + N(count) + "/" + N(max);
            if (client)
            {
                state = stale ? "danger" : "info";
                return stale ? "LINK LOST · " + wing : "CLIENT · HOST'S " + wing;
            }
            if (count <= 0 && inbound <= 0)
            {
                state = "inert";
                return "NO WING";
            }
            state = "live";
            string up = count > 0 ? " · " + (airborne > 0 ? N(airborne) : "NONE") + " AIRBORNE" : "";
            return wing + up + (inbound > 0 ? " · " + N(inbound) + " INBOUND" : "");
        }

        public static string Fuel(in WingVitals v, out string state)
        {
            if (v.Count <= 0 || float.IsNaN(v.MinFuel))
            {
                state = "inert";
                return "FUEL " + None;
            }
            if (v.BingoSlot >= 0)
            {
                state = "danger";
                return "BINGO " + WingRows.Number(v.BingoSlot);
            }
            if (v.JokerSlot >= 0)
            {
                state = "warn";
                return "JOKER " + WingRows.Number(v.JokerSlot);
            }
            state = "live";
            return "FUEL MIN " + WmcText.Percent(v.MinFuel);
        }

        public static string Ammo(in WingVitals v, out string state)
        {
            if (v.Count <= 0 || float.IsNaN(v.MinAmmo))
            {
                state = "inert";
                return "AMMO " + None;
            }
            if (v.WinchesterSlot >= 0)
            {
                state = "warn";
                return "WINCHESTER " + WingRows.Number(v.WinchesterSlot);
            }
            state = "live";
            return "AMMO MIN " + WmcText.Percent(v.MinAmmo);
        }

        /// <summary>A member defending a missile first, then hostile aircraft inside the leash (<paramref name="hostiles"/> &lt; 0:
        /// not known here, as on a client).</summary>
        public static string Threat(in WingVitals v, int hostiles, out string state)
        {
            if (v.DefendingSlot >= 0)
            {
                state = "danger";
                return "MISSILE " + WingRows.Number(v.DefendingSlot);
            }
            if (hostiles < 0)
            {
                state = "inert";
                return "THREAT " + None;
            }
            if (hostiles == 0)
            {
                state = "live";
                return "THREAT CLEAR";
            }
            state = "warn";
            return "THREAT " + (hostiles > 99 ? "99+" : N(hostiles)) + " AIR";
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

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
