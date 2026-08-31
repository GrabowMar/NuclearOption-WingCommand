using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// Which loadout every wing aircraft is actually carrying, and which one the next
    /// requisition of a type will carry.
    ///
    /// Live state is keyed by <c>Aircraft.persistentID</c>, exactly as
    /// <see cref="WingShop"/> keys ownership and over-limit slots. A choice made for one
    /// VT-7 belongs to that airframe and to no other, including the next VT-7 the player
    /// buys.
    ///
    /// The plan is necessarily keyed by definition instead, because at the moment the
    /// player chooses it the aircraft does not exist yet. It is a purchase order, not a
    /// fleet setting: it is read once, when a requisition is delivered, and copied onto
    /// that specific airframe.
    ///
    /// Recovery is the third case. A wingman that completes Return To Base is destroyed and
    /// its airframe becomes an anonymous count in <see cref="WingSupplyReserve"/>, so a
    /// persistentID cannot carry anything across. The loadout is parked here beside the
    /// reserve instead, per type and in recovery order, and the next requisition that
    /// consumes one of those slots collects it. That is what makes a recovered airframe fly
    /// again as it was configured rather than resetting to the stock fit.
    /// </summary>
    internal static class WingLoadoutBook
    {
        private static readonly Dictionary<PersistentID, WingLoadoutChoice> aboard =
            new Dictionary<PersistentID, WingLoadoutChoice>();

        private static readonly Dictionary<AircraftDefinition, WingLoadoutChoice> planned =
            new Dictionary<AircraftDefinition, WingLoadoutChoice>();

        private static readonly Dictionary<AircraftDefinition, List<WingLoadoutChoice>> reserved =
            new Dictionary<AircraftDefinition, List<WingLoadoutChoice>>();

        public static void Reset()
        {
            aboard.Clear();
            planned.Clear();
            reserved.Clear();
        }

        // -------------------------------------------------------------------- planning

        /// <summary>What the next requisition of this airframe will be fitted with.</summary>
        public static WingLoadoutChoice PlannedFor(AircraftDefinition definition)
        {
            if (definition == null) return WingLoadoutChoice.Standard;
            return planned.TryGetValue(definition, out WingLoadoutChoice choice)
                ? choice
                : WingLoadoutChoice.Standard;
        }

        public static void Plan(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            if (definition == null) return;
            planned[definition] = choice;
        }

        // ----------------------------------------------------------------------- aboard

        /// <summary>True when this mod knows what the aircraft is carrying.</summary>
        public static bool IsKnown(Aircraft aircraft) =>
            aircraft != null && aboard.ContainsKey(aircraft.persistentID);

        /// <summary>
        /// What the aircraft is carrying. Standard for anything this mod did not fit —
        /// including an active mission aircraft the player assigned, which arrives with
        /// whatever the mission gave it.
        /// </summary>
        public static WingLoadoutChoice AboardOf(Aircraft aircraft)
        {
            if (aircraft == null) return WingLoadoutChoice.Standard;
            return aboard.TryGetValue(aircraft.persistentID, out WingLoadoutChoice choice)
                ? choice
                : WingLoadoutChoice.Standard;
        }

        /// <summary>Record what a delivered requisition was actually fitted with.</summary>
        public static void NoteSpawned(Aircraft aircraft, WingLoadoutChoice choice)
        {
            if (aircraft == null) return;
            aboard[aircraft.persistentID] = choice;
        }

        public static void Forget(Aircraft aircraft)
        {
            if (aircraft != null) aboard.Remove(aircraft.persistentID);
        }

        // ---------------------------------------------------------------------- reserve

        /// <summary>Park a recovered airframe's loadout beside its reserve slot.</summary>
        public static void StoreReserved(AircraftDefinition definition, WingLoadoutChoice choice)
        {
            if (definition == null) return;

            if (!reserved.TryGetValue(definition, out List<WingLoadoutChoice> list))
            {
                list = new List<WingLoadoutChoice>();
                reserved.Add(definition, list);
            }
            list.Add(choice);
        }

        /// <summary>The loadout the next reserve launch of this type would carry.</summary>
        public static bool PeekReserved(AircraftDefinition definition, out WingLoadoutChoice choice)
        {
            choice = WingLoadoutChoice.Standard;
            if (definition == null) return false;
            if (!reserved.TryGetValue(definition, out List<WingLoadoutChoice> list) ||
                list.Count == 0)
                return false;

            choice = list[0];
            return true;
        }

        /// <summary>Consume the oldest parked loadout, once its reserve slot is spent.</summary>
        public static void PopReserved(AircraftDefinition definition) => DropReserved(definition);

        /// <summary>
        /// Drop one parked loadout. Also called when a reserve slot is released back to
        /// faction stock, so the book never outlives the airframes it describes.
        /// </summary>
        public static void DropReserved(AircraftDefinition definition)
        {
            if (definition == null) return;
            if (!reserved.TryGetValue(definition, out List<WingLoadoutChoice> list) ||
                list.Count == 0)
                return;

            list.RemoveAt(0);
            if (list.Count == 0) reserved.Remove(definition);
        }

        /// <summary>How many recovered loadouts are parked for this airframe.</summary>
        public static int ReservedCount(AircraftDefinition definition) =>
            definition != null && reserved.TryGetValue(definition, out List<WingLoadoutChoice> list)
                ? list.Count
                : 0;
    }
}
