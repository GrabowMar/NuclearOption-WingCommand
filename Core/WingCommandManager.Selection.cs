using System.Collections.Generic;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        internal bool MapOrderArmed => mapLayer != null && mapLayer.PointArmed;
        internal WingOrder ArmedMapOrder => mapLayer != null ? mapLayer.ArmedOrder : default;
        internal float MapMoveAltitude => mapLayer != null ? mapLayer.MoveAltitude : 0f;

        internal void SelectMember(WingMember member, bool toggle)
        {
            Selection.ClickMember(member, toggle, Wing);
            foreach (WingMember candidate in Wing.Members)
                WingMarkers.Repaint(candidate.Aircraft);
        }

     /// <summary>Set weapon preference for the current command scope without changing ROE.</summary>
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

     /// <summary>Shared scope preference, or null for mixed preferences; determines selector
     /// highlighting.</summary>
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

     /// <summary>Release a map-selected member to native AI.</summary>
        internal void RemoveMember(WingMember member)
        {
            if (member == null) return;
            string name = member.Name;
            Wing.Remove(member, "removed from the map panel");
            Toast(name + " released - returning to base");
        }

     /// <summary>Toggle temporary flight lead, transferring from any previous lead so at most one
     /// remains.</summary>
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

     /// <summary>Recruit the current map selection.</summary>
        internal void AddSelectedFromMap()
        {
            mapLayer?.AddSelected();
        }
    }
}
