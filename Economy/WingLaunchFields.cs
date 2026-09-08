using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
 /// <summary>Mission-scoped launch-field preferences. New fields default enabled; exclusions persist
 /// until re-enabled or mission reset. Controls nearest-field versus any-free-pad routing.</summary>
    internal static class WingLaunchFields
    {
        public static HangarLaunchMode Mode { get; set; } = HangarLaunchMode.OnlyNearest;

     /// <summary>Disabled field IDs; unlisted and newly created fields remain eligible.</summary>
        private static readonly HashSet<int> denied = new HashSet<int>();

        private static readonly List<Airbase> listing = new List<Airbase>();
        private static readonly List<float> listingDistSq = new List<float>();

        public static IReadOnlyList<Airbase> Listing => listing;

        public static bool IsAllowed(Airbase airbase) =>
            airbase != null && !denied.Contains(airbase.GetInstanceID());

        public static void SetAllowed(Airbase airbase, bool allow)
        {
            if (airbase == null) return;
            int id = airbase.GetInstanceID();
            if (allow) denied.Remove(id);
            else denied.Add(id);
        }

        public static bool HasAnyAllowed(FactionHQ hq)
        {
            if (hq == null) return false;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                if (IsAllowed(airbase)) return true;
            }
            return false;
        }

     /// <summary>Whether a live hangar or pad supports this airframe, regardless of current
     /// occupancy.</summary>
        public static bool CanProduce(Airbase airbase, AircraftDefinition definition)
        {
            if (airbase == null || airbase.disabled || definition == null) return false;
            return WingHangarStock.FieldLists(airbase, definition);
        }

     /// <summary>Whether an enabled friendly field can launch this definition. Surface units bypass
     /// hangars and spawn astern in water.</summary>
        public static bool CanAnyAllowedLaunch(FactionHQ hq, AircraftDefinition definition)
        {
            if (definition == null) return false;
            if (WingShop.IsSurfaceDefinition(definition)) return true;
            if (hq == null) return false;

            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                if (!IsAllowed(airbase)) continue;
                if (CanProduce(airbase, definition)) return true;
            }
            return false;
        }

     /// <summary>Refresh live friendly fields nearest first for Supply. Delivery uses its own snapshot
     /// so UI refreshes cannot change spawn decisions.</summary>
        public static void RefreshListing(FactionHQ hq, Vector3 from)
        {
            listing.Clear();
            listingDistSq.Clear();
            if (hq == null) return;

            WingHangarStock.Refresh(hq);
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                listing.Add(airbase);
                listingDistSq.Add((airbase.transform.position - from).sqrMagnitude);
            }

            for (int i = 1; i < listing.Count; i++)
            {
                Airbase airbase = listing[i];
                float dist = listingDistSq[i];
                int j = i - 1;
                while (j >= 0 && listingDistSq[j] > dist)
                {
                    listing[j + 1] = listing[j];
                    listingDistSq[j + 1] = listingDistSq[j];
                    j--;
                }
                listing[j + 1] = airbase;
                listingDistSq[j + 1] = dist;
            }
        }

        public static string DisplayName(Airbase airbase)
        {
            if (airbase == null) return "FIELD";
            string name = airbase.name;
            if (string.IsNullOrEmpty(name)) return "FIELD";
            if (name.EndsWith("(Clone)"))
                name = name.Substring(0, name.Length - 7).TrimEnd();
            return name;
        }

        public static void Reset()
        {
            Mode = HangarLaunchMode.OnlyNearest;
            denied.Clear();
            listing.Clear();
            listingDistSq.Clear();
            WingHangarStock.Reset();
        }
    }
}
