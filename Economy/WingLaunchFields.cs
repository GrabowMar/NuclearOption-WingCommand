using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Which friendly fields a requisition may launch from, and whether it pins to the
    /// nearest or takes any free pad.
    ///
    /// Mission-scoped: new airbases default on, unchecked ones stay out until the player
    /// ticks them again, and a mission reset forgets the lot. Not a BepInEx key — the set
    /// of fields is different every mission.
    /// </summary>
    internal static class WingLaunchFields
    {
        public static HangarLaunchMode Mode { get; set; } = HangarLaunchMode.OnlyNearest;

        /// <summary>
        /// Instance ids the player has turned off. Everything else is allowed, so a field
        /// that appears mid-mission launches until someone unchecks it.
        /// </summary>
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

        /// <summary>
        /// Whether this airbase has a live hangar or helipad that can spawn the given airframe,
        /// regardless of whether that pad is free this frame.
        /// </summary>
        public static bool CanProduce(Airbase airbase, AircraftDefinition definition)
        {
            if (airbase == null || airbase.disabled || definition == null) return false;
            IList<Hangar> hangars = airbase.hangars;
            if (hangars == null) return false;

            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar hangar = hangars[i];
                if (hangar == null || hangar.Disabled) continue;
                AircraftDefinition[] types = hangar.GetAvailableAircraft();
                if (types == null) continue;
                for (int j = 0; j < types.Length; j++)
                    if (types[j] == definition) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any of the allowed friendly airbases can launch this airframe.
        /// Surface units spawn directly in water astern of the player without a hangar,
        /// so they always return true.
        /// </summary>
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

        /// <summary>Number of allowed airbases capable of launching this airframe.</summary>
        public static int CountAllowedCanLaunch(FactionHQ hq, AircraftDefinition definition)
        {
            if (definition == null || hq == null) return 0;
            if (WingShop.IsSurfaceDefinition(definition)) return 1;

            int count = 0;
            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled) continue;
                if (!IsAllowed(airbase)) continue;
                if (CanProduce(airbase, definition)) count++;
            }
            return count;
        }

        /// <summary>
        /// Friendly, live fields, nearest first. The Supply pager reads this; delivery
        /// collects its own snapshot so a UI refresh cannot change a spawn decision.
        /// </summary>
        public static void RefreshListing(FactionHQ hq, Vector3 from)
        {
            listing.Clear();
            listingDistSq.Clear();
            if (hq == null) return;

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
        }
    }
}
