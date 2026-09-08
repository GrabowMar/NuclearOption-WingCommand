using UnityEngine;

namespace WingCommand
{
    /// <summary>Pairs native Hover with auto-hover configuration, which enables altitude control and VTOL
    /// nozzle positioning. HasAutoHover identifies capability even for plane-autopilot jets; native
    /// SetAutoHover refuses below 1 m radar altitude.</summary>
    internal static class HoverAssist
    {
        /// <summary>Whether the aircraft supports native hover, including helicopters, tiltwings, and
        /// vectoring jets.</summary>
        public static bool CanHover(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            ControlsFilter filter = aircraft.GetControlsFilter();
            return filter != null && filter.HasAutoHover();
        }

        /// <summary>Reassert native hover configuration each frame; SetAutoHover is idempotent and native
        /// touchdown can clear it.</summary>
        public static void Engage(Aircraft aircraft)
        {
            if (aircraft == null) return;

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter == null || !filter.HasAutoHover()) return;
            if (filter.IsAutoHoverEnabled()) return;

            filter.SetAutoHover(enabled: true);
        }

        /// <summary>Release hover configuration and restore forward thrust so the aircraft can accelerate
        /// into cruise.</summary>
        public static void Release(Aircraft aircraft)
        {
            if (aircraft == null) return;

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter == null || !filter.HasAutoHover()) return;
            if (filter.IsAutoHoverEnabled()) filter.SetAutoHover(enabled: false);

            // Restore vectoring-duct cruise commands even if native code already cleared hover; AI duct
            // systems may retain downward customAxis1. Check actual duct components so unrelated
            // helo/tiltwing axis uses remain untouched.
            if (aircraft.GetComponentInChildren<SwivelDuctSystem>(includeInactive: true) != null ||
                aircraft.GetComponentInChildren<DuctedThrustSystem>(includeInactive: true) != null)
                aircraft.GetInputs().customAxis1 = 1f;
        }

        /// <summary>Configure hover before commanding position hold through the shared path.</summary>
        public static void Hover(Aircraft aircraft, GlobalPosition destination,
                                 float altitudeHold, Vector3 aimDirection)
        {
            if (aircraft == null || aircraft.autopilot == null) return;

            Engage(aircraft);
            aircraft.autopilot.Hover(destination, altitudeHold, aimDirection);
        }
    }
}
