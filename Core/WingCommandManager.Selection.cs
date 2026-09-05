using System.Collections.Generic;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        internal void SelectMember(WingMember member, bool toggle)
        {
            Selection.ClickMember(member, toggle, Wing);
            foreach (WingMember candidate in Wing.Members)
                WingMarkers.Repaint(candidate.Aircraft);
        }

        /// <summary>
        /// Set which weapons the current command scope reaches for first.
        ///
        /// Scoped like an order rather than held wing-wide, so a mixed flight can be split
        /// between the air and the ground without changing anyone's rules of engagement.
        /// </summary>
        internal void SetWeaponPreference(WingWeaponPreference preference)
        {
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            if (scope.Count == 0)
            {
                Toast(Wing.Count == 0
                    ? "No wingmen. Requisition on SUPPLY."
                    : "No wingmen selected");
                return;
            }

            foreach (WingMember member in scope) member.WeaponPreference = preference;

            Toast((Selection.IsAll ? "Wing" : scope.Count + " selected") + ": weapons " +
                  WingWeaponPreferences.Label(preference));
        }

        /// <summary>
        /// The preference shared by the current scope, or null when they disagree. The
        /// selector uses this to decide which button to light.
        /// </summary>
        internal WingWeaponPreference? ScopeWeaponPreference()
        {
            List<WingMember> scope = Commands.Scope(wholeWing: false);
            if (scope.Count == 0) return null;

            WingWeaponPreference first = scope[0].WeaponPreference;
            for (int i = 1; i < scope.Count; i++)
            {
                if (scope[i].WeaponPreference != first) return null;
            }
            return first;
        }

        internal void SelectAllMembers()
        {
            Selection.ToggleSelectAll(Wing);
            foreach (WingMember member in Wing.Members) WingMarkers.Repaint(member.Aircraft);
        }

        /// <summary>Drop one member back to the stock AI. Used by the map panel.</summary>
        internal void RemoveMember(WingMember member)
        {
            if (member == null) return;
            string name = member.Name;
            Wing.Remove(member, "removed from the map panel");
            Toast(name + " released - returning to base");
        }

        /// <summary>
        /// Grant or revoke temporary flight lead. Pressing it on the current lead, or on a
        /// second wingman, hands it over cleanly - there is only ever one lead.
        /// </summary>
        internal void ToggleFlightLead(WingMember member)
        {
            if (member == null) return;

            if (Wing.FlightLead == member)
            {
                Wing.ClearFlightLead();
                Toast("Flight lead released - wing forming on you");
                return;
            }

            Toast(Wing.TrySetFlightLead(member, out string reason)
                ? member.Name + " leads the flight - wing forming on them"
                : "Cannot make " + member.Name + " lead: " + reason);
        }

        /// <summary>Assign the current map selection to the wing. Used by the map panel.</summary>
        internal void AddSelectedFromMap()
        {
            mapLayer?.AddSelected();
        }
    }
}
