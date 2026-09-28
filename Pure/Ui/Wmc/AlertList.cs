namespace WingCommand
{
    /// <summary>What TACTICAL's situation area warns about, most urgent first.</summary>
    internal enum AlertKind : byte { Missile, Lost, Damaged, Bingo, Winchester, Joker, Behind }

    internal struct Alert
    {
        public AlertKind Kind;
        public uint Id;
        public int Slot;
        /// <summary>How a LOST member was lost (Killed, Ejected, Gone).</summary>
        public TransitionReason Why;
    }

    /// <summary>TACTICAL's clickable alerts (spec WMC rebuild §TACTICAL) from the wing's snapshot rows — a missile being
    /// defended, damage, bingo (or joker), winchester, falling behind — and, on the host, the members lost in the last
    /// <see cref="LostSeconds"/> from the wing's event ring. When more than <see cref="Max"/> hold, the most severe are kept.</summary>
    internal static class AlertList
    {
        public const int Max = 8;
        public const float LostSeconds = 20f;

        public static int Fill(SnapshotMember[] rows, int count, Alert[] into) => Fill(rows, count, null, 0f, into);

        public static int Fill(SnapshotMember[] rows, int count, WingEventRing events, float now, Alert[] into)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                SnapshotMember m = rows[i];
                var f = (SnapshotFlags)m.Flags;
                if ((MemberDuty)m.Duty == MemberDuty.Defending) Insert(into, ref n, AlertKind.Missile, m.Id, m.Slot, TransitionReason.None);
                if ((f & SnapshotFlags.Damaged) != 0) Insert(into, ref n, AlertKind.Damaged, m.Id, m.Slot, TransitionReason.None);
                if ((f & SnapshotFlags.Bingo) != 0) Insert(into, ref n, AlertKind.Bingo, m.Id, m.Slot, TransitionReason.None);
                else if ((f & SnapshotFlags.Joker) != 0) Insert(into, ref n, AlertKind.Joker, m.Id, m.Slot, TransitionReason.None);
                if ((f & SnapshotFlags.Winchester) != 0) Insert(into, ref n, AlertKind.Winchester, m.Id, m.Slot, TransitionReason.None);
                if ((f & SnapshotFlags.FallingBehind) != 0) Insert(into, ref n, AlertKind.Behind, m.Id, m.Slot, TransitionReason.None);
            }
            if (events != null)
                for (int i = events.Count - 1; i >= 0; i--)
                {
                    WingEvent e = events[i];
                    if (e.Time < now - LostSeconds) break;
                    if (e.Kind != WingEventKind.MemberLost || !MemberLoss.IsLoss(e.Reason)) continue;
                    // Back in the wing (a re-adopted aircraft): not lost.
                    if (e.Id != 0u && WingRows.IndexOf(rows, count, e.Id) >= 0) continue;
                    Insert(into, ref n, AlertKind.Lost, e.Id, e.Member, e.Reason);
                }
            return n;
        }

        /// <summary>A sorted insert by severity, then seat; when full the least severe drops (never a missile for a joker).</summary>
        private static void Insert(Alert[] into, ref int n, AlertKind kind, uint id, int slot, TransitionReason why)
        {
            int cap = into.Length < Max ? into.Length : Max;
            int at = n;
            while (at > 0 && (into[at - 1].Kind > kind || (into[at - 1].Kind == kind && into[at - 1].Slot > slot))) at--;
            if (at >= cap) return;
            int last = n < cap ? n : cap - 1;
            for (int j = last; j > at; j--) into[j] = into[j - 1];
            into[at] = new Alert { Kind = kind, Id = id, Slot = slot, Why = why };
            if (n < cap) n++;
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
                case AlertKind.Lost: return "lost";
                case AlertKind.Damaged: return "hit";
                case AlertKind.Bingo: return "bingo fuel";
                case AlertKind.Winchester: return "out of weapons";
                case AlertKind.Joker: return "joker fuel";
                default: return "falling behind";
            }
        }

        public static string Detail(in Alert a) => a.Kind == AlertKind.Lost ? LossWords(a.Why) : Detail(a.Kind);

        /// <summary>A loss in words (the alert and the log share them).</summary>
        public static string LossWords(TransitionReason why)
        {
            switch (why)
            {
                case TransitionReason.Killed: return "shot down";
                case TransitionReason.Ejected: return "ejected";
                case TransitionReason.Released: return "left the wing";
                default: return "lost";
            }
        }
    }
}
