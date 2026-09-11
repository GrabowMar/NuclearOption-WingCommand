using System;

namespace WingCommand.Interop
{
    /// <summary>Public reflection-safe presence API. Companion plugins resolve these by
    /// assembly-qualified type name (<c>Type.GetType("WingCommand.Interop.X, WingCommand")</c>) —
    /// no compiled WingCommand reference, no shared DLL, no shared AppDomain channel.</summary>
    public static class WingPresence
    {
        public static int ApiVersion => 1;
        public const string Guid = "com.marci.wingcommand";
    }

    /// <summary>The recruited wing's live roster, keyed by
    /// <c>Aircraft.persistentID.GetHashCode()</c>. Backed by the in-process wing — see
    /// <see cref="Publish"/>. Boscali Summer reads this so its theater doctrine and sortie
    /// tally leave the player's wingmen alone.</summary>
    public static class WingMembership
    {
        public static int ApiVersion => 1;

        private static readonly int[] Empty = Array.Empty<int>();
        private static int[] ids = Empty;
        private static int count;

        public static int Count => count;

        public static bool Contains(int persistentIdHash)
        {
            for (int i = 0; i < count; i++)
                if (ids[i] == persistentIdHash) return true;
            return false;
        }

        /// <summary>Refresh the published roster from the live wing. Called each frame by
        /// <see cref="WingCommandManager"/>. Reuses the buffer and only grows it.</summary>
        internal static void Publish(WingRegistry wing)
        {
            int n = wing?.Count ?? 0;
            if (n == 0) { count = 0; return; }
            if (ids.Length < n) ids = new int[n];
            for (int i = 0; i < n; i++)
            {
                Aircraft aircraft = wing.Members[i]?.Aircraft;
                ids[i] = aircraft == null ? 0 : aircraft.persistentID.GetHashCode();
            }
            count = n;
        }

        internal static void Clear() => count = 0;
    }

    /// <summary>Wing Command's tactical-map mode. A companion plugin defers its own armed map
    /// gesture while <see cref="GestureArmed"/> is set, so one right-click cannot both move a
    /// wingman and drop a support strike.</summary>
    public static class WingMapMode
    {
        public static int ApiVersion => 1;

        /// <summary>Whether the WMC screen is the active MFD panel on its TACTICAL page.</summary>
        public static bool TacticalCommandActive { get; internal set; }

        /// <summary>Whether a wing point-order is currently armed on the tactical map.</summary>
        public static bool GestureArmed { get; internal set; }
    }
}
