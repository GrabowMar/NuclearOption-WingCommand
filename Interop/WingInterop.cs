using NOAvionics;

namespace WingCommand.Interop
{
    /// <summary>
    /// Public, reflection-safe façade. Boscali Summer must not compile against this
    /// assembly; it reads the same AppDomain keys via its own copy of PresenceBoard,
    /// or probes these types by name when it wants a typed view.
    /// </summary>
    public static class WingPresence
    {
        public static int ApiVersion => 1;
        public const string Guid = "com.marci.wingcommand";
    }

    public static class WingMembership
    {
        public static int ApiVersion => 1;

        public static int Count => PresenceBoard.GetInts(PresenceBoard.WingMemberIds)?.Length ?? 0;

        public static bool Contains(int persistentIdHash) =>
            PresenceBoard.Contains(PresenceBoard.GetInts(PresenceBoard.WingMemberIds), persistentIdHash);
    }

    public static class WingMapMode
    {
        public static int ApiVersion => 1;
        public static bool TacticalCommandActive { get; internal set; }
        public static bool OwnsMapGesture => MapPicker.IsOwner(MapPicker.WingPoint);
    }
}

namespace WingCommand
{
    internal static class WingInteropPush
    {
        private static readonly int[] Empty = System.Array.Empty<int>();
        private static int[] publishedIds;

        public static void Publish(WingRegistry wing)
        {
            if (publishedIds == null)
                PresenceBoard.SetString(PresenceBoard.WingGuid, Interop.WingPresence.Guid);
            Interop.WingMapMode.TacticalCommandActive = WmcScreen.TacticalCommandModeActive;

            int count = wing?.Count ?? 0;
            bool changed = publishedIds == null || publishedIds.Length != count;
            if (changed) publishedIds = count == 0 ? Empty : new int[count];

            for (int i = 0; i < count; i++)
            {
                Aircraft aircraft = wing.Members[i]?.Aircraft;
                int id = aircraft == null ? 0 : aircraft.persistentID.GetHashCode();
                if (publishedIds[i] == id) continue;
                publishedIds[i] = id;
                changed = true;
            }

            // SetInts takes its own immutable snapshot. Reuse our comparison buffer
            // between roster changes, including replacements that preserve the count.
            if (changed) PresenceBoard.SetInts(PresenceBoard.WingMemberIds, publishedIds);
        }

        public static void Clear()
        {
            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, Empty);
            PresenceBoard.SetString(PresenceBoard.WingGuid, null);
            Interop.WingMapMode.TacticalCommandActive = false;
            publishedIds = null;
            BezelRegistry.Release(BezelRegistry.Wmc);
            MapPicker.Disarm(MapPicker.WingPoint);
        }
    }
}
