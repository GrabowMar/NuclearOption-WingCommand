using System.Globalization;

namespace WingCommand
{
    /// <summary>The WING tab's words for a member, from its snapshot entry (spec M7b §3): the host and a client read the
    /// same entry.</summary>
    internal static class WingRows
    {
        /// <summary>"#2" for slot 0 (the leader is #1), as the HUD numbers members.</summary>
        public static string Number(int slot) => "#" + (slot + 2).ToString(CultureInfo.InvariantCulture);

        /// <summary>FIGHT, RTB, DEFEND, GROUND or LANDED by duty; in formation the HUD phase (JOIN, SLOT, HOLD, TRAIL,
        /// BEHIND).</summary>
        public static string State(in SnapshotMember m)
        {
            switch ((MemberDuty)m.Duty)
            {
                case MemberDuty.Engaged: return "FIGHT";
                case MemberDuty.Recovering: return "RTB";
                case MemberDuty.Defending: return "DEFEND";
                case MemberDuty.Grounded: return "GROUND";
                case MemberDuty.Settled: return "LANDED";
                default:
                    return WingHudText.Phase((BehaviourId)m.Behaviour, (m.Flags & (byte)SnapshotFlags.FallingBehind) != 0);
            }
        }

        /// <summary>BINGO over JOKER, then WINCHESTER; empty when none.</summary>
        public static string Flags(byte flags)
        {
            var f = (SnapshotFlags)flags;
            string fuel = (f & SnapshotFlags.Bingo) != 0 ? "BINGO" : (f & SnapshotFlags.Joker) != 0 ? "JOKER" : "";
            string ammo = (f & SnapshotFlags.Winchester) != 0 ? "WINCHESTER" : "";
            return fuel.Length == 0 ? ammo : ammo.Length == 0 ? fuel : fuel + " " + ammo;
        }

        public static float Fraction(byte b) => b / 255f;

        /// <summary>The metric row: the lowest fuel and ammo (NaN for nobody) and whether anyone is at bingo.</summary>
        public static WingSummary Summary(SnapshotMember[] rows, int count)
        {
            var s = new WingSummary { Count = count, MinFuel = float.NaN, MinAmmo = float.NaN };
            for (int i = 0; i < count; i++)
            {
                float fuel = Fraction(rows[i].Fuel), ammo = Fraction(rows[i].Ammo);
                if (float.IsNaN(s.MinFuel) || fuel < s.MinFuel) s.MinFuel = fuel;
                if (float.IsNaN(s.MinAmmo) || ammo < s.MinAmmo) s.MinAmmo = ammo;
                if ((rows[i].Flags & (byte)SnapshotFlags.Bingo) != 0) s.Bingo = true;
            }
            return s;
        }
    }

    internal struct WingSummary
    {
        public int Count;
        public float MinFuel, MinAmmo;
        public bool Bingo;
    }
}
