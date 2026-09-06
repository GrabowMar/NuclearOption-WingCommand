using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>
    /// Tracks delivered fits by persistent aircraft ID and future purchase plans by definition.
    /// Recovery transfers the fit to its concrete <see cref="WingSupplyReserve"/> slot
    /// because the recovered aircraft and its ID are destroyed.
    /// </summary>
    internal static class WingLoadoutBook
    {
        private sealed class FittedLoadout
        {
            internal WingLoadoutChoice Choice;
            internal int CaptureAfterFrame;
        }

        private static readonly Dictionary<PersistentID, FittedLoadout> aboard =
            new Dictionary<PersistentID, FittedLoadout>();

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
            if (!aboard.TryGetValue(aircraft.persistentID, out FittedLoadout fitted))
                return WingLoadoutChoice.Standard;
            if (UnityEngine.Time.frameCount > fitted.CaptureAfterFrame &&
                (fitted.CaptureAfterFrame >= 0 || !fitted.Choice.HasSnapshot))
            {
                fitted.Choice = WingLoadoutCatalog.SnapshotFit(aircraft, fitted.Choice);
                if (fitted.Choice.HasSnapshot) fitted.CaptureAfterFrame = -1;
            }
            return fitted.Choice;
        }

        /// <summary>Record what a delivered requisition was actually fitted with.</summary>
        public static void NoteSpawned(Aircraft aircraft, WingLoadoutChoice choice)
        {
            if (aircraft == null) return;
            // Native registration runs before Hangar finishes choosing its standard fit.
            // Even a non-null loadout can still be a placeholder during this callback.
            aboard[aircraft.persistentID] = new FittedLoadout
            {
                Choice = choice,
                CaptureAfterFrame = UnityEngine.Time.frameCount,
            };
        }

        public static void Forget(Aircraft aircraft)
        {
            if (aircraft != null) aboard.Remove(aircraft.persistentID);
        }

        public static void Forget(PersistentID aircraftId) => aboard.Remove(aircraftId);

    }
}
