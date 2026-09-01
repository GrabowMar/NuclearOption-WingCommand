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
    /// Recovery is the third case. A wingman that completes Return To Base is destroyed, so
    /// its persistentID cannot carry anything across. Its fit moves into the concrete
    /// <see cref="WingSupplyReserve"/> slot instead, alongside that airframe's source and
    /// ownership, rather than into a separate per-type FIFO that can drift out of alignment.
    /// </summary>
    internal static class WingLoadoutBook
    {
        private static readonly Dictionary<PersistentID, WingLoadoutChoice> aboard =
            new Dictionary<PersistentID, WingLoadoutChoice>();

        private static readonly Dictionary<AircraftDefinition, WingLoadoutChoice> planned =
            new Dictionary<AircraftDefinition, WingLoadoutChoice>();

        public static void Reset()
        {
            aboard.Clear();
            planned.Clear();
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

        public static void Forget(PersistentID aircraftId) => aboard.Remove(aircraftId);

    }
}
