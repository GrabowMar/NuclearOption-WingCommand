namespace WingCommand
{
    /// <summary>One line of TACTICAL's flight list: an element header or a member row (an index into the snapshot rows).</summary>
    internal struct FlightLine
    {
        public bool Header;
        public int Element;
        public int Row;
    }

    /// <summary>TACTICAL's flight list (spec WMC rebuild §TACTICAL): members in element order under their element's header,
    /// every member visible while the lines fit, paged beyond. A page that starts inside an element repeats its header; a
    /// header always comes with its first member, so it is never alone at the foot of a page.</summary>
    internal static class FlightList
    {
        /// <summary>The lines of page <paramref name="page"/> (clamped) that fit <paramref name="capacity"/> px; the pager
        /// takes <see cref="BezelLayout.Pager"/> when there is more than one page. <paramref name="order"/> is scratch.</summary>
        public static int Page(SnapshotMember[] rows, int count, int[] order, float capacity, int page, FlightLine[] into, out int pages)
        {
            ElementGroups.Order(rows, count, order);
            float need = 0f;
            int last = -1;
            for (int k = 0; k < count; k++)
            {
                int e = rows[order[k]].Element;
                if (e != last) need += BezelLayout.HeaderPitch;
                last = e;
                need += BezelLayout.RowPitch;
            }
            float usable = need <= capacity ? capacity : capacity - BezelLayout.Pager;
            pages = Walk(rows, count, order, usable, -1, into, out _);
            if (page < 0) page = 0;
            if (page >= pages) page = pages - 1;
            Walk(rows, count, order, usable, page, into, out int n);
            return n;
        }

        private static int Walk(SnapshotMember[] rows, int count, int[] order, float usable, int fill, FlightLine[] into, out int n)
        {
            n = 0;
            int current = 0, last = -1;
            float used = 0f;
            bool content = false;
            for (int k = 0; k < count; k++)
            {
                int r = order[k], e = rows[r].Element;
                float cost = BezelLayout.RowPitch + (e != last ? BezelLayout.HeaderPitch : 0f);
                if (content && used + cost > usable)
                {
                    current++;
                    used = 0f;
                    last = -1;
                    content = false;
                }
                if (e != last)
                {
                    if (current == fill && n < into.Length) into[n++] = new FlightLine { Header = true, Element = e, Row = -1 };
                    used += BezelLayout.HeaderPitch;
                    last = e;
                }
                if (current == fill && n < into.Length) into[n++] = new FlightLine { Header = false, Element = e, Row = r };
                used += BezelLayout.RowPitch;
                content = true;
            }
            return current + 1;
        }
    }
}
