using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>What the map and HUD mark (spec WMC program §5): wing members in their element's colour with a badge
    /// (element letter + number, e.g. "B4"), the targets the wing is attacking, and downed wing pilots. Polled at 4 Hz;
    /// a unit that loses its mark gets the game's own colours back.</summary>
    internal static class WingMarkers
    {
        public enum Role { None, Member, Target, Downed }

        private struct Mark
        {
            public Unit Unit;
            public Role Role;
            public int Element, Number;
        }

        public static readonly Color TargetColor = new Color(1f, 0.69f, 0.13f), DownedColor = new Color(1f, 0.22f, 0.18f);
        private static readonly List<Mark> marks = new List<Mark>(), previous = new List<Mark>();
        private static readonly List<Unit> downed = new List<Unit>();
        private static readonly string[,] badges = new string[ElementRoster.MaxElements, 40];
        private static float nextPoll;

        public static void Reset()
        {
            marks.Clear();
            previous.Clear();
            nextPoll = 0f;
        }

        public static void Tick(WingService w)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + WingFidelity.Interval(0.25f);
            previous.Clear();
            previous.AddRange(marks);
            marks.Clear();
            HighlightMode mode = Plugin.Settings.MapMarkers.Value;
            if (w != null)
            {
                if (mode != HighlightMode.Off)
                {
                    downed.Clear();
                    PersonnelFacade.SearchAndRescue.CollectDowned(downed);
                    foreach (Unit u in downed) Add(u, Role.Downed, 0, 0);
                }
                // Members are always known: WMC's selection brackets show with marks off (rings and badges do not).
                foreach (WingMember m in w.Members)
                    if ((object)m.Aircraft != null && m.Alive && !m.Released) Add(m.Aircraft, Role.Member, w.ElementOf(m), m.Number);
                if (mode == HighlightMode.WingAndTargets)
                    foreach (WingMember m in w.Members)
                        if (m.Alive && !m.Released) Add(TargetOf(m), Role.Target, 0, 0);
            }
            // A unit that lost its mark gets the game's colours back; the marked are reasserted (icons are recreated and
            // repainted by the game at will).
            foreach (Mark p in previous)
                if (IndexOf(p.Unit) < 0) Restore(p.Unit);
            foreach (Mark m in marks)
            {
                WingMapTint.Reassert(m.Unit);
                WingHudTint.Reassert(m.Unit);
            }
        }

        private static void Add(Unit u, Role role, int element, int number)
        {
            if (u == null || u.disabled || IndexOf(u) >= 0) return;
            marks.Add(new Mark { Unit = u, Role = role, Element = element, Number = number });
        }

        private static int IndexOf(Unit u)
        {
            for (int i = 0; i < marks.Count; i++)
                if (ReferenceEquals(marks[i].Unit, u)) return i;
            return -1;
        }

        /// <summary>The explicit target first; while fighting, what the weapons are on.</summary>
        private static Unit TargetOf(WingMember m)
        {
            Unit t = m.AssignedTarget != null && !m.AssignedTarget.disabled ? m.AssignedTarget : m.StandingTarget;
            if (t != null && !t.disabled) return t;
            if (!m.Engaged || m.Aircraft == null || m.Aircraft.weaponManager == null) return null;
            List<Unit> list = m.Aircraft.weaponManager.GetTargetList();
            return list != null && list.Count > 0 ? list[0] : null;
        }

        public static Role RoleOf(Unit unit, out int element, out int number)
        {
            int i = unit != null ? IndexOf(unit) : -1;
            element = i >= 0 ? marks[i].Element : 0;
            number = i >= 0 ? marks[i].Number : 0;
            return i >= 0 ? marks[i].Role : Role.None;
        }

        public static Color ColorOf(Role role, int element) =>
            role == Role.Member ? WmcMapOverlay.ElementColor(element) : role == Role.Downed ? DownedColor : TargetColor;

        /// <summary>"B4": the element's letter and the member's number, built once per pair.</summary>
        public static string Badge(int element, int number)
        {
            if (element < 0 || element >= ElementRoster.MaxElements || number < 0 || number >= badges.GetLength(1)) return null;
            return badges[element, number] ?? (badges[element, number] = ElementRoster.Letter(element) + number);
        }

        public static string DownedLabel(Unit unit)
        {
            WingPilot pilot = PersonnelFacade.SearchAndRescue.PilotOf(unit as PilotDismounted);
            return pilot != null ? "SAR · " + pilot.Callsign : "SAR";
        }

        private static void Restore(Unit unit)
        {
            WingMapTint.Restore(unit);
            WingHudTint.Restore(unit);
        }
    }
}
