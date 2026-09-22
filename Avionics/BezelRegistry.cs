using System;
using System.Collections;

namespace NOAvionics
{
    /// <summary>
    /// Cross-assembly occupancy of maximised-map MFD bezel slots.
    ///
    /// Each plugin compiles its own copy of this type. They talk through
    /// <see cref="AppDomain.SetData"/> so there is no shared DLL and no hard
    /// dependency. Values are BCL-only; never store a custom type in the domain.
    /// </summary>
    public static class BezelRegistry
    {
        public const int ApiVersion = 1;

        public const string Wmc = "WMC";
        public const string Ops = "OPS";
        public const string Rad = "RAD";
        public const string Str = "STR";
        public const string Set = "SET";

        public const string PreferredLeft = "left";
        public const string PreferredRight = "right";

        private const string DataKey = "NO.Bezel.v1";
        private const string LockKey = "NO.Bezel.v1";

        public delegate bool SlotFree(bool leftColumn, int index);

        /// <summary>
        /// Reserve a physical slot. <paramref name="isFree"/> is the caller's view of
        /// the live VirtualMFD lists; this registry additionally skips slots another
        /// plugin has already committed, which is what stops a same-frame double claim.
        /// </summary>
        public static bool TryClaim(
            string id, bool preferLeft, int leftCount, int rightCount,
            SlotFree isFree, out bool left, out int slot)
        {
            left = preferLeft;
            slot = -1;
            if (string.IsNullOrEmpty(id) || isFree == null) return false;
            if (leftCount < 0) leftCount = 0;
            if (rightCount < 0) rightCount = 0;

            lock (LockKey)
            {
                Hashtable table = Table();
                if (TryRead(table, id, out left, out slot)) return true;

                if (TryOccupy(table, id, preferLeft, preferLeft ? leftCount : rightCount, isFree,
                    out left, out slot))
                    return true;

                if (TryOccupy(table, id, !preferLeft, preferLeft ? rightCount : leftCount, isFree,
                    out left, out slot))
                    return true;

                return false;
            }
        }

        public static void Release(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (LockKey)
            {
                Hashtable table = Table();
                table.Remove(id);
            }
        }

        public static bool IsClaimed(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (LockKey)
            {
                return Table().ContainsKey(id);
            }
        }

        /// <summary>Test seam. Plugins must not call this on a live game.</summary>
        public static void Reset()
        {
            lock (LockKey)
            {
                AppDomain.CurrentDomain.SetData(DataKey, new Hashtable());
            }
        }

        private static bool TryOccupy(
            Hashtable table, string id, bool left, int count, SlotFree isFree,
            out bool resultLeft, out int slot)
        {
            resultLeft = left;
            slot = -1;
            for (int i = 0; i < count; i++)
            {
                if (Taken(table, left, i)) continue;
                if (!isFree(left, i)) continue;
                table[id] = Encode(left, i);
                slot = i;
                return true;
            }
            return false;
        }

        private static bool Taken(Hashtable table, bool left, int slot)
        {
            foreach (DictionaryEntry entry in table)
            {
                if (!(entry.Value is string encoded)) continue;
                if (!TryDecode(encoded, out bool claimedLeft, out int claimedSlot)) continue;
                if (claimedLeft == left && claimedSlot == slot) return true;
            }
            return false;
        }

        private static bool TryRead(Hashtable table, string id, out bool left, out int slot)
        {
            left = false;
            slot = -1;
            if (!(table[id] is string encoded)) return false;
            return TryDecode(encoded, out left, out slot);
        }

        private static string Encode(bool left, int slot) =>
            (left ? PreferredLeft : PreferredRight) + ":" + slot.ToString();

        private static bool TryDecode(string encoded, out bool left, out int slot)
        {
            left = false;
            slot = -1;
            if (string.IsNullOrEmpty(encoded)) return false;
            int colon = encoded.IndexOf(':');
            if (colon <= 0 || colon == encoded.Length - 1) return false;
            string column = encoded.Substring(0, colon);
            if (!int.TryParse(encoded.Substring(colon + 1), out slot) || slot < 0) return false;
            if (column == PreferredLeft) { left = true; return true; }
            if (column == PreferredRight) { left = false; return true; }
            return false;
        }

        private static Hashtable Table()
        {
            if (AppDomain.CurrentDomain.GetData(DataKey) is Hashtable existing)
                return existing;
            var created = new Hashtable();
            AppDomain.CurrentDomain.SetData(DataKey, created);
            return AppDomain.CurrentDomain.GetData(DataKey) as Hashtable ?? created;
        }
    }
}
