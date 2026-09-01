using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Keeps a native ECM jammer active by re-deploying it at a fixed cadence.
    ///
    /// <c>RadarJammer.Fire</c> lasts about a tenth of a second, so a single call leaves no
    /// lasting coverage. Both the missile-break defensive state and the standing Jam
    /// Target order need the same "hold the jammer on" behaviour; this is that behaviour,
    /// with its per-aircraft station resolution cached and its errors reported once.
    /// </summary>
    internal sealed class RadarJammerPulser
    {
        // The jammer stays active for 0.1 s after Fire. Pulse just below that lifetime,
        // rather than once per physics tick, for continuous coverage at a known cadence.
        private const float PulseSeconds = 0.075f;

        private int index = -1;
        private bool resolved;
        private bool errorReported;
        private float nextPulse;

        /// <summary>True once a RadarJammer station has been found on this aircraft.</summary>
        public bool HasJammer(Aircraft aircraft)
        {
            Resolve(aircraft);
            return index >= 0;
        }

        /// <summary>
        /// Re-deploy the jammer if the cadence allows. Temporarily selects the jammer
        /// station and restores whatever countermeasure index was selected before, so a
        /// separately held flare/chaff trigger keeps working. Returns true when a pulse
        /// was actually sent this call.
        /// </summary>
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
                // Leaving the jammer selected would silently turn a held flare/chaff
                // trigger into a second ECM driver and stop the expendable releasing.
                manager.activeIndex = previous;
            }
        }

        /// <summary>Forget the cached resolution; call when the aircraft or its fit may have changed.</summary>
        public void Reset()
        {
            index = -1;
            resolved = false;
            errorReported = false;
            nextPulse = 0f;
        }

        private void Resolve(Aircraft aircraft)
        {
            if (resolved) return;

            CountermeasureManager manager = aircraft != null ? aircraft.countermeasureManager : null;
            if (manager == null) return;   // not latched: the manager may not exist yet

            resolved = true;
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
