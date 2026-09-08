using System;
using UnityEngine;

namespace WingCommand
{
 /// <summary>Pulses ECM within its 0.1-second lifetime. Caches station indices, refreshes after
 /// possible refits, and logs failures once.</summary>
    internal sealed class RadarJammerPulser
    {
        // Pulse before the 0.1-second ECM lifetime expires to maintain coverage.
        private const float PulseSeconds = 0.075f;

     /// <summary>Seconds before rechecking a station index; registration renumbers stations without an
     /// event.</summary>
        private const float ResolveSeconds = 5f;

        private int index = -1;
        private bool resolved;
        private bool errorReported;
        private float nextPulse;
        private float nextResolve;

     /// <summary>Whether this aircraft has a resolved RadarJammer station.</summary>
        public bool HasJammer(Aircraft aircraft)
        {
            Resolve(aircraft);
            return index >= 0;
        }

     /// <summary>Pulses ECM when due, then restores the selected countermeasure so held chaff/flare
     /// triggers keep working. Returns true if a pulse was sent.</summary>
        public bool Pulse(Aircraft aircraft)
        {
            CountermeasureManager manager = aircraft != null ? aircraft.countermeasureManager : null;
            if (manager == null) return false;

            Resolve(aircraft);
            if (index < 0 || index > byte.MaxValue) return false;
            if (Time.timeSinceLevelLoad < nextPulse) return false;
            nextPulse = Time.timeSinceLevelLoad + PulseSeconds;

            byte previous = manager.activeIndex;
            try
            {
                manager.activeIndex = (byte)index;
                manager.DeployCountermeasure(aircraft);
                return true;
            }
            catch (Exception e)
            {
                Report(aircraft, "Could not activate ECM on ",
                       e.GetType().Name + " - " + e.Message);
                return false;
            }
            finally
            {
                // Restore selection so a held dispense trigger releases chaff or flares, not ECM.
                manager.activeIndex = previous;
            }
        }

     /// <summary>Clear the station cache after an aircraft or loadout change.</summary>
        public void Reset()
        {
            index = -1;
            resolved = false;
            errorReported = false;
            nextPulse = 0f;
        }

     /// <summary>Retry missing managers and refresh indices periodically because rearming sorts
     /// stations by name.</summary>
        private void Resolve(Aircraft aircraft)
        {
            CountermeasureManager manager = aircraft != null ? aircraft.countermeasureManager : null;
            if (manager == null) return;   // Retry while the manager is absent.

            if (resolved && Time.timeSinceLevelLoad < nextResolve) return;

            resolved = true;
            nextResolve = Time.timeSinceLevelLoad + ResolveSeconds;
            index = -1;

            if (!CountermeasureAccess.TryFindRadarJammer(manager, out index, out string reason) &&
                !string.IsNullOrEmpty(reason))
            {
                Report(aircraft, "Could not inspect ECM on ", reason);
            }
        }

        private void Report(Aircraft aircraft, string prefix, string detail)
        {
            if (errorReported) return;
            errorReported = true;
            string name = aircraft != null ? aircraft.unitName : "(unknown)";
            Plugin.Logger.LogWarning("[ECM] " + prefix + name + ": " + detail);
        }
    }
}
