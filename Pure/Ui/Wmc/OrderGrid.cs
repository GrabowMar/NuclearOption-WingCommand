namespace WingCommand
{
    internal enum GridOrder : byte { Attack, Splash, Engage, Sweep, Scout, Move, Orbit, Cap, Hold, Break, FormUp, Ecm, Detach, Rtb, Refit, Land, Cargo, TakeOff, Rescue, Maneuver }

    /// <summary>What an order needs before it can go: nothing, a map point, an enemy, or an area.</summary>
    internal enum GridInput : byte { Now, Point, Target, Area }

    internal readonly struct GridCell
    {
        public readonly GridOrder Order;
        public readonly string Id, Label, Tip, Pending;
        public readonly GridInput Input;
        public readonly MapMode Map;
        /// <summary>A maneuver's number (the <c>OrderKind.Maneuver</c> order's Number); 0 for the rest.</summary>
        public readonly int Number;

        public GridCell(GridOrder order, string key, string label, GridInput input, MapMode map, string tip, string pending = null,
            string prefix = "tac.orders.", int number = 0)
        {
            Order = order;
            Number = number;
            Id = prefix + key;
            Label = label;
            Input = input;
            Map = map;
            Tip = tip;
            Pending = pending;
        }

        public bool Built => Pending == null;
    }

    /// <summary>TACTICAL's order grid (spec bezel v2 §5 TACTICAL): four labelled rows with a category rail each; an order that
    /// needs a point or an enemy latches and arms the map, the rest act on the scope at once. With helicopters in scope SWEEP
    /// becomes SCOUT and SUPPORT becomes TAKE OFF · RESCUE · LAND · CARGO (RTB stays on every member row). Under it the REACT row
    /// flies the five maneuvers; BREAK therefore reads DISENGAGE.</summary>
    internal static class OrderGrid
    {
        public const int Rows = 4, Columns = 4;
        public static readonly string[] RowLabels = { "OFFENSE", "MOVE", "DEFENSE", "SUPPORT" };
        private static readonly string[] rowRails = { "danger", "info", "warn", "live" };
        public const string ReactLabel = "REACT", ReactRail = "armed";

        /// <summary>The rail class of grid row <paramref name="row"/>; the row's word key says the same.</summary>
        public static string RowRail(int row) => row >= 0 && row < rowRails.Length ? rowRails[row] : "info";

        /// <summary>BRK L · BRK R · PULL UP · SPLIT · BEAM: flown through the pipeline, then back to the slot.</summary>
        public static readonly GridCell[] React =
        {
            new GridCell(GridOrder.Maneuver, "brkl", "BRK L", GridInput.Now, MapMode.Off, "Hard turn 90° left, then back to the slot.", null, "tac.react.", 0),
            new GridCell(GridOrder.Maneuver, "brkr", "BRK R", GridInput.Now, MapMode.Off, "Hard turn 90° right, then back to the slot.", null, "tac.react.", 1),
            new GridCell(GridOrder.Maneuver, "pullup", "PULL UP", GridInput.Now, MapMode.Off, "Climb 500 m straight on, then back to the slot.", null, "tac.react.", 2),
            new GridCell(GridOrder.Maneuver, "split", "SPLIT", GridInput.Now, MapMode.Off,
                "Turn 60° apart: a pair splits, one aircraft turns away from the lead.", null, "tac.react.", 3),
            new GridCell(GridOrder.Maneuver, "beam", "BEAM", GridInput.Now, MapMode.Off,
                "Turn across the nearest air threat's line of sight (from the wing's tracks), then back to the slot.", null, "tac.react.", 4),
        };

        private static readonly GridCell Attack = new GridCell(GridOrder.Attack, "attack", "ATTACK", GridInput.Target, MapMode.Attack,
            "Right-click an enemy on the map to attack it; shift-click adds targets.");
        private static readonly GridCell Splash = new GridCell(GridOrder.Splash, "splash", "SPLASH", GridInput.Now, MapMode.Off,
            "One missile at the nearest enemy aircraft.");
        private static readonly GridCell Engage = new GridCell(GridOrder.Engage, "engage", "ENGAGE", GridInput.Now, MapMode.Off,
            "Fight the enemies in reach, then come back.");
        private static readonly GridCell Sweep = new GridCell(GridOrder.Sweep, "sweep", "SWEEP", GridInput.Area, MapMode.Sweep,
            "Right-click the map: loop round a 12 km area there and fight hostile aircraft found in it; right-drag sets the radius.");
        private static readonly GridCell Scout = new GridCell(GridOrder.Scout, "scout", "SCOUT", GridInput.Now, MapMode.Off,
            "A low route 20 km ahead, reporting contacts.");
        private static readonly GridCell Move = new GridCell(GridOrder.Move, "move", "MOVE", GridInput.Point, MapMode.Move,
            "Right-click the map: fly there, then orbit.");
        private static readonly GridCell Orbit = new GridCell(GridOrder.Orbit, "orbit", "ORBIT", GridInput.Point, MapMode.Orbit,
            "Right-click the map: orbit there. HERE orbits where they are now.");
        private static readonly GridCell Cap = new GridCell(GridOrder.Cap, "cap", "CAP", GridInput.Area, MapMode.Cap,
            "Right-click the map: orbit there and fight hostile aircraft entering 8 km of it; right-drag sets the radius.");
        private static readonly GridCell Hold = new GridCell(GridOrder.Hold, "hold", "HOLD", GridInput.Point, MapMode.Hold,
            "Right-click the map: hold there. HERE holds where they are now.");
        private static readonly GridCell Break = new GridCell(GridOrder.Break, "break", "DISENGAGE", GridInput.Now, MapMode.Off,
            "Stop fighting and rejoin.");
        private static readonly GridCell FormUp = new GridCell(GridOrder.FormUp, "formup", "FORM UP", GridInput.Now, MapMode.Off,
            "Back to formation on you.");
        private static readonly GridCell Ecm = new GridCell(GridOrder.Ecm, "ecm", "ECM", GridInput.Now, MapMode.Off,
            "The scope's jammers on for a minute (only aircraft carrying one); press again to stop.");
        private static readonly GridCell Detach = new GridCell(GridOrder.Detach, "detach", "DETACH", GridInput.Now, MapMode.Off,
            "The selected wingmen orbit where they are, as their own element.");
        private static readonly GridCell Rtb = new GridCell(GridOrder.Rtb, "rtb", "RTB", GridInput.Now, MapMode.Off,
            "Home to the reserve.");
        private static readonly GridCell Refit = new GridCell(GridOrder.Refit, "refit", "REFIT", GridInput.Now, MapMode.Off,
            "Home to refuel and rearm, then back out.");
        private static readonly GridCell Land = new GridCell(GridOrder.Land, "land", "LAND", GridInput.Point, MapMode.Land,
            "Right-click a landing point: helicopters land there.");
        private static readonly GridCell Cargo = new GridCell(GridOrder.Cargo, "cargo", "CARGO", GridInput.Point, MapMode.Cargo,
            "Right-click a drop point: helicopters carrying cargo fly there, land and deliver.");
        private static readonly GridCell TakeOff = new GridCell(GridOrder.TakeOff, "takeoff", "TAKE OFF", GridInput.Now, MapMode.Off,
            "Landed helicopters lift off.");
        private static readonly GridCell Rescue = new GridCell(GridOrder.Rescue, "rescue", "RESCUE", GridInput.Now, MapMode.Off,
            "A helicopter picks up a downed pilot.");

        private static readonly GridCell[] Jets =
        {
            Attack, Splash, Engage, Sweep,
            Move, Orbit, Cap, Hold,
            Break, FormUp, Ecm, Detach,
            Rtb, Refit, Land, Cargo,
        };

        private static readonly GridCell[] Helos =
        {
            Attack, Splash, Engage, Scout,
            Move, Orbit, Cap, Hold,
            Break, FormUp, Ecm, Detach,
            TakeOff, Rescue, Land, Cargo,
        };

        public static GridCell At(int row, int column, bool helos) => (helos ? Helos : Jets)[row * Columns + column];

        /// <summary>Why the cell cannot be pressed now, or null.</summary>
        public static string Why(in GridCell cell, bool canOrder, int members, bool scoped)
        {
            if (!cell.Built) return cell.Pending;
            if (!canOrder) return "Orders are host only for now";
            if (members == 0) return "No wingmen: call or recruit some first";
            if (cell.Order == GridOrder.Detach && !scoped) return "Select the wingmen to detach";
            return null;
        }

        public static bool HasHere(GridOrder order) => order == GridOrder.Orbit || order == GridOrder.Hold;
    }
}
