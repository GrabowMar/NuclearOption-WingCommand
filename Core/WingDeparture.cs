using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
 /// <summary>Tracks released aircraft flying home under native landing AI. Exclude them from squadron
 /// capacity while returning; WingRecovery settles and despawns them on arrival, restoring owned
 /// airframes to stock.</summary>
    internal static class WingDeparture
    {
     /// <summary>Settlement data captured before removing the member.</summary>
        internal sealed class Departing
        {
            public Aircraft Aircraft;
            public PersistentID AircraftId;
            public string Name;
            public bool Owned;
            public bool LoadoutKnown;
            public WingLoadoutChoice Loadout;
        }

        private static readonly List<Departing> outbound = new List<Departing>();

        public static IReadOnlyList<Departing> Outbound => outbound;

     /// <summary>Track a released member's return flight.</summary>
        public static void Begin(WingMember member)
        {
            if (member == null || member.Aircraft == null) return;
            if (Contains(member.Aircraft)) return;

            outbound.Add(new Departing
            {
                Aircraft = member.Aircraft,
                AircraftId = member.Aircraft.persistentID,
                Name = member.Name,
                Owned = WingShop.IsPurchased(member.Aircraft),
                LoadoutKnown = member.LoadoutKnown,
                Loadout = member.Loadout,
            });
        }

     /// <summary>Track an unassigned faction aircraft ordered home.</summary>
        public static void Begin(Aircraft aircraft, string name = null, bool owned = false)
        {
            if (aircraft == null) return;
            if (Contains(aircraft)) return;

            outbound.Add(new Departing
            {
                Aircraft = aircraft,
                AircraftId = aircraft.persistentID,
                Name = name ?? aircraft.unitName ?? "AI",
                Owned = owned || WingShop.IsPurchased(aircraft),
                LoadoutKnown = false,
                Loadout = WingLoadoutChoice.Standard,
            });
        }

     /// <summary>Remove a settled or lost departure from tracking.</summary>
        public static void Forget(Departing departing)
        {
            if (departing != null) outbound.Remove(departing);
        }

     /// <summary>Discard destroyed departures so they no longer reduce the shop's live-aircraft
     /// count.</summary>
        public static void Prune()
        {
            for (int i = outbound.Count - 1; i >= 0; i--)
            {
                Aircraft aircraft = outbound[i].Aircraft;
                if (aircraft == null || aircraft.disabled) outbound.RemoveAt(i);
            }
        }

     /// <summary>Whether the aircraft is a released return flight, for exclusion from shop squadron
     /// capacity.</summary>
        public static bool Contains(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            for (int i = 0; i < outbound.Count; i++)
                if (outbound[i].Aircraft == aircraft) return true;
            return false;
        }

        public static void Reset() => outbound.Clear();
    }
}
