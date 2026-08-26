using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Adds two things to the maximised map that the stock game does not have.
    ///
    /// 1. Aircraft tasking. Vanilla right-click order handling only fires when the
    ///    selected icon is an <c>ICommandable</c>, and <c>Aircraft</c> does not implement
    ///    it (only <c>GroundVehicle</c>, <c>Ship</c> and <c>Missile</c> do). Selecting an
    ///    aircraft and right-clicking is therefore a no-op in vanilla, which leaves the
    ///    gesture free for recruiting friendly AI aircraft into the wing.
    ///
    /// 2. Squad groups. Ctrl+1..4 stores the current map selection; 1..4 restores it.
    ///    This works for ground and naval units too, so vanilla move orders can be issued
    ///    to a saved group without re-selecting it by hand every time.
    /// </summary>
    internal class MapCommandLayer
    {
        internal const int GroupCount = 4;

        private readonly WingRegistry wing;
        private readonly List<Unit>[] groups = new List<Unit>[GroupCount];
        private readonly List<Aircraft> recruited = new List<Aircraft>();

        private static readonly KeyCode[] GroupKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
        };

        public MapCommandLayer(WingRegistry wing)
        {
            this.wing = wing;
            for (int i = 0; i < GroupCount; i++)
                groups[i] = new List<Unit>();
        }

        public void Update()
        {
            if (!DynamicMap.mapMaximized) return;

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            HandleGroupKeys(map);
            HandleAircraftTasking(map);
        }

        // ------------------------------------------------------------ squad groups

        private void HandleGroupKeys(DynamicMap map)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            for (int i = 0; i < GroupCount; i++)
            {
                if (!Input.GetKeyDown(GroupKeys[i])) continue;

                if (ctrl) StoreGroup(map, i);
                else RecallGroup(map, i);
                return;
            }
        }

        private void StoreGroup(DynamicMap map, int index)
        {
            groups[index].Clear();

            foreach (MapIcon icon in map.selectedIcons)
            {
                if (icon is UnitMapIcon unitIcon && unitIcon.unit != null && !unitIcon.unit.disabled)
                    groups[index].Add(unitIcon.unit);
            }

            Toast(groups[index].Count > 0
                ? "Group " + (index + 1) + ": stored " + groups[index].Count + " unit(s)"
                : "Group " + (index + 1) + ": cleared");
        }

        private void RecallGroup(DynamicMap map, int index)
        {
            groups[index].RemoveAll(u => u == null || u.disabled);

            if (groups[index].Count == 0)
            {
                Toast("Group " + (index + 1) + " is empty");
                return;
            }

            ClearSelection(map);
            foreach (Unit u in groups[index])
                map.SelectIcon(u);

            Toast("Group " + (index + 1) + ": " + groups[index].Count + " unit(s) selected");
        }

        // Entry points for the map panel's buttons.

        public int GroupSize(int index)
        {
            if (index < 0 || index >= GroupCount) return 0;
            groups[index].RemoveAll(u => u == null || u.disabled);
            return groups[index].Count;
        }

        public void StoreGroupExternal(int index)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && index >= 0 && index < GroupCount) StoreGroup(map, index);
        }

        public void RecallGroupExternal(int index)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && index >= 0 && index < GroupCount) RecallGroup(map, index);
        }

        private static void ClearSelection(DynamicMap map)
        {
            // Copy first: DeselectIcon may mutate the live list.
            var current = new List<MapIcon>(map.selectedIcons);
            foreach (MapIcon icon in current)
            {
                if (icon != null) icon.DeselectIcon();
            }
            map.selectedIcons.Clear();
        }

        // -------------------------------------------------------- aircraft tasking

        private void HandleAircraftTasking(DynamicMap map)
        {
            if (!Input.GetMouseButtonDown(1)) return;

            // A click on the wing panel must not also drop a map order underneath it.
            if (WingMapPanel.MouseOverPanel) return;
            if (map.selectedIcons.Count == 0) return;

            // Only act when the primary selection is an aircraft. Anything else is a
            // vanilla commandable unit and the stock handler owns the gesture.
            if (!(map.selectedIcons[0] is UnitMapIcon primary)) return;
            if (!(primary.unit is Aircraft)) return;

            AddSelected();
        }

        /// <summary>
        /// Assign every eligible friendly AI aircraft in the current map selection to the
        /// wing. Shared by the right-click gesture and the panel's Add Selected button.
        /// </summary>
        public void AddSelected()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;

            if (wing.Leader == null)
            {
                Toast("Not flying - cannot form a wing");
                return;
            }

            if (map.selectedIcons.Count == 0)
            {
                Toast("Nothing selected on the map");
                return;
            }

            recruited.Clear();
            int max = Plugin.Config2.MaxWingSize.Value;

            foreach (MapIcon icon in map.selectedIcons)
            {
                if (wing.Count + recruited.Count >= max) break;
                if (!(icon is UnitMapIcon unitIcon)) continue;
                if (!(unitIcon.unit is Aircraft aircraft)) continue;
                if (aircraft == wing.Leader || aircraft.Player != null) continue;
                if (aircraft.disabled || wing.Contains(aircraft)) continue;
                if (DynamicMap.GetFactionMode(aircraft.NetworkHQ) != FactionMode.Friendly) continue;

                recruited.Add(aircraft);
            }

            // Recruit outside the loop above: Add repaints the icon, and repainting an
            // icon inside a foreach over the live selection list is asking for trouble.
            int added = 0;
            foreach (Aircraft a in recruited)
            {
                if (wing.Add(a) != null) added++;
            }

            ReleaseSelection(map);

            if (added > 0)
                Toast("Wing: " + added + " aircraft assigned (" + wing.Count + " total)");
            else if (wing.Count >= max)
                Toast("Wing is full");
            else
                Toast("No eligible friendly AI aircraft selected");
        }

        /// <summary>
        /// Drop the map selection once the aircraft in it have been recruited.
        ///
        /// The selection was a gesture, not a state the player wanted to keep. Leaving it
        /// standing costs twice over: a selected icon is drawn white by the game's own
        /// highlight, which hides the wing colour on exactly the aircraft that just
        /// earned it, and each selected unit carries a target-marker info card that
        /// clutters the map around the formation.
        ///
        /// While the player is flying, selecting a unit on the map also adds it to their
        /// weapon target list, so the deselect goes through the HUD rather than the map
        /// alone — otherwise the new wingman stays designated as one of the player's own
        /// targets.
        /// </summary>
        private void ReleaseSelection(DynamicMap map)
        {
            if (recruited.Count == 0) return;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;

            foreach (Aircraft a in recruited)
            {
                if (a == null) continue;

                if (flying && hud.GetTargetList().Contains(a)) hud.DeSelectUnit(a);
                else map.DeselectIcon(a);
            }

            recruited.Clear();
        }

        private static void Toast(string message)
        {
            WingCommandManager.Instance?.Toast(message);
        }
    }
}
