namespace WingCommand
{
    /// <summary>What TACTICAL's situation area warns about, most urgent first.</summary>
    internal enum AlertKind : byte { Missile, Lost, Damaged, Bingo, Winchester, Joker, Behind }

    internal struct Alert
    {
        public AlertKind Kind;
        public uint Id;
        public int Slot;
    }

    /// <summary>TACTICAL's clickable alerts (spec WMC rebuild §TACTICAL) from the wing's snapshot rows — the host and a client
    /// read the same entries: a missile being defended, bingo (or joker), winchester, falling behind. Lost and damaged come
    /// from events (R3).</summary>
    internal static class AlertList
    {
        public const int Max = 8;

        public static int Fill(SnapshotMember[] rows, int count, Alert[] into)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                SnapshotMember m = rows[i];
                var f = (SnapshotFlags)m.Flags;
                if ((MemberDuty)m.Duty == MemberDuty.Defending) Add(into, ref n, AlertKind.Missile, m);
                if ((f & SnapshotFlags.Bingo) != 0) Add(into, ref n, AlertKind.Bingo, m);
                else if ((f & SnapshotFlags.Joker) != 0) Add(into, ref n, AlertKind.Joker, m);
                if ((f & SnapshotFlags.Winchester) != 0) Add(into, ref n, AlertKind.Winchester, m);
                if ((f & SnapshotFlags.FallingBehind) != 0) Add(into, ref n, AlertKind.Behind, m);
            }
            // Insertion sort by severity, then seat: stable, allocation-free, eight entries at most.
            for (int i = 1; i < n; i++)
            {
                Alert a = into[i];
                int j = i - 1;
                while (j >= 0 && (into[j].Kind > a.Kind || (into[j].Kind == a.Kind && into[j].Slot > a.Slot)))
                {
                    into[j + 1] = into[j];
                    j--;
                }
                into[j + 1] = a;
            }
            return n;
        }

        private static void Add(Alert[] into, ref int n, AlertKind kind, in SnapshotMember m)
        {
            if (n >= into.Length || n >= Max) return;
            into[n++] = new Alert { Kind = kind, Id = m.Id, Slot = m.Slot };
        }

        public static string Word(AlertKind k)
        {
            switch (k)
            {
                case AlertKind.Missile: return "MISSILE";
                case AlertKind.Lost: return "LOST";
                case AlertKind.Damaged: return "DAMAGED";
                case AlertKind.Bingo: return "BINGO";
                case AlertKind.Winchester: return "WINCHESTER";
                case AlertKind.Joker: return "JOKER";
                default: return "BEHIND";
            }
        }

        public static string Detail(AlertKind k)
        {
            switch (k)
            {
                case AlertKind.Missile: return "defending";
                case AlertKind.Lost: return "left the wing";
                case AlertKind.Damaged: return "hit";
                case AlertKind.Bingo: return "bingo fuel";
                case AlertKind.Winchester: return "out of weapons";
                case AlertKind.Joker: return "joker fuel";
                default: return "falling behind";
            }
        }
    }
}
