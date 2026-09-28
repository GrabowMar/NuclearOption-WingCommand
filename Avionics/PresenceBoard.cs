using System;

namespace NOAvionics
{
    /// <summary>
    /// Tiny typed bulletin board for optional cross-mod snapshots. Keys are interned
    /// protocol names; values are BCL arrays so a plugin can read another plugin's
    /// numbers without sharing a type.
    /// </summary>
    public static class PresenceBoard
    {
        public const string WingMemberIds = "NO.Wing.ids.v1";
        public const string WingGuid = "NO.Wing.guid.v1";
        public const string TheaterGuid = "NO.Theater.guid.v1";
        public const string TheaterDoctrine = "NO.Theater.doctrine.v1";
        public const string TheaterPriorityIds = "NO.Theater.priority.v1";

        private const string LockKey = "NO.Presence.v1";

        public static void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (LockKey) AppDomain.CurrentDomain.SetData(key, value);
        }

        public static string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            lock (LockKey) return AppDomain.CurrentDomain.GetData(key) as string;
        }

        public static void SetInts(string key, int[] values)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (LockKey)
            {
                if (values == null || values.Length == 0)
                {
                    AppDomain.CurrentDomain.SetData(key, Array.Empty<int>());
                    return;
                }

                var copy = new int[values.Length];
                Array.Copy(values, copy, values.Length);
                AppDomain.CurrentDomain.SetData(key, copy);
            }
        }

        public static int[] GetInts(string key)
        {
            if (string.IsNullOrEmpty(key)) return Array.Empty<int>();
            lock (LockKey)
            {
                if (!(AppDomain.CurrentDomain.GetData(key) is int[] values) || values.Length == 0)
                    return Array.Empty<int>();
                var copy = new int[values.Length];
                Array.Copy(values, copy, values.Length);
                return copy;
            }
        }

        public static bool Contains(int[] values, int id)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == id) return true;
            return false;
        }

        /// <summary>Test seam.</summary>
        public static void Reset()
        {
            lock (LockKey)
            {
                AppDomain.CurrentDomain.SetData(WingMemberIds, null);
                AppDomain.CurrentDomain.SetData(WingGuid, null);
                AppDomain.CurrentDomain.SetData(TheaterGuid, null);
                AppDomain.CurrentDomain.SetData(TheaterDoctrine, null);
                AppDomain.CurrentDomain.SetData(TheaterPriorityIds, null);
            }
        }
    }
}
