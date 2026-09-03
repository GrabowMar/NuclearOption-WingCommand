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

        public static int Count
        {
            get
            {
                int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
                return ids == null ? 0 : ids.Length;
            }
        }

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
        private static int lastCount = -1;

        public static void Publish(WingRegistry wing)
        {
            PresenceBoard.SetString(PresenceBoard.WingGuid, Interop.WingPresence.Guid);
            Interop.WingMapMode.TacticalCommandActive = WmcScreen.TacticalCommandModeActive;

            if (wing == null || wing.Count == 0)
            {
                if (lastCount != 0)
                {
                    PresenceBoard.SetInts(PresenceBoard.WingMemberIds, Empty);
                    lastCount = 0;
                }
                return;
            }

            var ids = new int[wing.Count];
            for (int i = 0; i < wing.Count; i++)
            {
                Aircraft aircraft = wing.Members[i]?.Aircraft;
                ids[i] = aircraft == null ? 0 : aircraft.persistentID.GetHashCode();
            }

            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, ids);
            lastCount = ids.Length;
        }

        public static void Clear()
        {
            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, Empty);
            PresenceBoard.SetString(PresenceBoard.WingGuid, null);
            Interop.WingMapMode.TacticalCommandActive = false;
            lastCount = -1;
            BezelRegistry.Release(BezelRegistry.Wmc);
            MapPicker.Disarm(MapPicker.WingPoint);
        }
    }
}
