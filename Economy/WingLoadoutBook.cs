using System.Collections.Generic;

namespace WingCommand
{
 /// <summary>Tracks fitted loadouts by aircraft ID and future plans by definition. Recovery moves fits
 /// to reserve slots before aircraft IDs disappear.</summary>
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

        // Purchase planning.

     /// <summary>Planned fit for the next purchase of this definition.</summary>
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

        // Fitted loadouts.

     /// <summary>Whether this aircraft has a recorded fit.</summary>
        public static bool IsKnown(Aircraft aircraft) =>
            aircraft != null && aboard.ContainsKey(aircraft.persistentID);

     /// <summary>Recorded fit, or Standard for aircraft the mod did not configure, including recruited
     /// mission aircraft.</summary>
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

     /// <summary>Record a delivered aircraft's actual fit.</summary>
        public static void NoteSpawned(Aircraft aircraft, WingLoadoutChoice choice)
        {
            if (aircraft == null) return;
            // Native registration may precede final hangar fitting; even a non-null loadout can be
            // temporary.
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
