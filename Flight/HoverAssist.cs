using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// The half of a hover command the mod was leaving out.
    ///
    /// <c>Autopilot.Hover</c> looks like a complete position hold, and for a conventional
    /// helicopter it very nearly is: it drives pitch, roll, yaw and collective from the slot
    /// error and then calls <c>Aircraft.FilterInputs</c>. What it does not do is put the
    /// airframe into its hovering configuration, and every stock state that calls it turns
    /// that on first. <c>AIHeloLandingState</c> makes the dependency explicit — it hovers
    /// only inside <c>if (aircraft.IsAutoHoverEnabled())</c> and flies <c>AutoAim</c>
    /// otherwise — and <c>AIHeloTransportState</c>, <c>AIPilotLandingState</c>
    /// and <c>AIPilotShortLandingState</c> all do the same.
    ///
    /// The setting matters most on the airframes that need it most. <c>ControlsFilter</c>
    /// runs its own <c>AutoHover</c> pass inside <c>FilterInputs</c>, and that pass returns
    /// immediately unless auto-hover is active — so the altitude hold and the commanded
    /// nozzle position never ran. On a thrust-vectoring aircraft that is the whole problem:
    /// <c>SwivelDuctSystem</c> selects its Hover mode, and with it <c>customAxis1 = 0</c>,
    /// only while <c>IsAutoHoverEnabled()</c>. Worse, it forces itself to Manual for any
    /// aircraft with no player aboard, and auto-hover is the single thing that overrides
    /// that. A wingman told to hover therefore held a hover attitude with its nozzles still
    /// pointing aft, which is not a hover — it is a stall with extra steps.
    ///
    /// Two useful facts fall out of the game's own accessors:
    /// <list type="bullet">
    /// <item><c>HasAutoHover()</c> is a real capability test — whether this airframe has a
    /// hover controller at all — and is true for thrust-vectoring jets that fly an
    /// <c>AutopilotPlane</c> and would fail an "is it a helicopter" test.</item>
    /// <item><c>SetAutoHover</c> refuses below one metre of radar altitude, so a landed
    /// aircraft cannot be commanded back into a hover by accident.</item>
    /// </list>
    /// </summary>
    internal static class HoverAssist
    {
        /// <summary>
        /// True when this airframe can hover at all — a helicopter, a tiltwing, or a
        /// thrust-vectoring jet.
        ///
        /// Preferred over an autopilot-type test for anything that asks "can it hold a
        /// position", because a VTOL jet flies <c>AutopilotPlane</c> and hovers perfectly
        /// well.
        /// </summary>
        public static bool CanHover(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            ControlsFilter filter = aircraft.GetControlsFilter();
            return filter != null && filter.HasAutoHover();
        }

        /// <summary>
        /// Put the aircraft into its hovering configuration and hold it there.
        ///
        /// Safe to call every frame: <c>SetAutoHover</c> is idempotent, and the state is
        /// re-asserted rather than latched because the stock <c>AutoHover</c> switches
        /// itself off on touchdown and a state that is still descending needs it back.
        /// </summary>
        public static void Engage(Aircraft aircraft)
        {
            if (aircraft == null) return;

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter == null || !filter.HasAutoHover()) return;
            if (filter.IsAutoHoverEnabled()) return;

            filter.SetAutoHover(enabled: true);
        }

        /// <summary>
        /// Leave the hovering configuration, so the nozzles swing forward again and the
        /// aircraft can accelerate.
        ///
        /// The counterpart to <see cref="Engage"/>, and the reason a wingman that hovers
        /// beside a stopped leader can still chase it when it goes. Without this the duct
        /// system stays in its Hover mode indefinitely — it only leaves on speed, which is
        /// not a thing an aircraft with its nozzles pointing down is going to reach.
        /// </summary>
        public static void Release(Aircraft aircraft)
        {
            if (aircraft == null) return;

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter == null || !filter.HasAutoHover()) return;
            if (!filter.IsAutoHoverEnabled()) return;

            filter.SetAutoHover(enabled: false);
        }

        /// <summary>
        /// Hover toward a point, having first configured the aircraft to be able to.
        ///
        /// Every hover in this mod goes through here so that the pairing cannot be
        /// forgotten again at a new call site.
        /// </summary>
        public static void Hover(Aircraft aircraft, GlobalPosition destination,
                                 float altitudeHold, Vector3 aimDirection)
        {
            if (aircraft == null || aircraft.autopilot == null) return;

            Engage(aircraft);
            aircraft.autopilot.Hover(destination, altitudeHold, aimDirection);
        }
    }
}
