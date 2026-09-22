using System;

namespace NOAvionics
{
    /// <summary>
    /// Exclusive owner of an armed map gesture. Wing Command point-orders and Boscali
    /// support call-ins both want the same right-click; only one may be armed.
    ///
    /// Stored as BCL values in the AppDomain so two compiled copies of this type agree.
    /// </summary>
    public static class MapPicker
    {
        public const int ApiVersion = 1;

        public const int GestureLeft = 0;
        public const int GestureRight = 1;

        public const string WingPoint = "wingcommand.point";
        public const string Support = "boscali.support";

        private const string DataKey = "NO.Picker.v1";
        private const string LockKey = "NO.Picker.v1";

        public static bool IsBusy
        {
            get
            {
                lock (LockKey) return Owner() != null;
            }
        }

        public static string Prompt
        {
            get
            {
                lock (LockKey)
                {
                    object[] row = Row();
                    return row == null ? null : row[2] as string;
                }
            }
        }

        public static int Gesture
        {
            get
            {
                lock (LockKey)
                {
                    object[] row = Row();
                    if (row == null || !(row[1] is int gesture)) return -1;
                    return gesture;
                }
            }
        }

        public static bool IsOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            lock (LockKey) return string.Equals(Owner(), owner, StringComparison.Ordinal);
        }

        /// <summary>
        /// Arm this owner. Fails if another owner already holds the map. Re-arming the
        /// same owner replaces the prompt and gesture.
        /// </summary>
        public static bool TryArm(string owner, int gesture, string prompt)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            if (gesture != GestureLeft && gesture != GestureRight) return false;
            if (string.IsNullOrEmpty(prompt)) prompt = "ARMED · CLICK MAP";

            lock (LockKey)
            {
                string current = Owner();
                if (current != null && !string.Equals(current, owner, StringComparison.Ordinal))
                    return false;

                AppDomain.CurrentDomain.SetData(DataKey, new object[] { owner, gesture, prompt });
                return true;
            }
        }

        public static void Disarm(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return;
            lock (LockKey)
            {
                if (!string.Equals(Owner(), owner, StringComparison.Ordinal)) return;
                AppDomain.CurrentDomain.SetData(DataKey, null);
            }
        }

        /// <summary>Test seam. Plugins must not call this on a live game.</summary>
        public static void Reset()
        {
            lock (LockKey)
            {
                AppDomain.CurrentDomain.SetData(DataKey, null);
            }
        }

        private static string Owner()
        {
            object[] row = Row();
            return row == null ? null : row[0] as string;
        }

        private static object[] Row()
        {
            return AppDomain.CurrentDomain.GetData(DataKey) as object[];
        }
    }
}
