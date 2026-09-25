namespace WingCommand
{
    internal enum GridOrder : byte { Attack, Splash, Engage, Sweep, Scout, Move, Orbit, Cap, Hold, Break, FormUp, Ecm, Detach, Rtb, Refit, Land, Cargo, TakeOff, Rescue }

    /// <summary>What an order needs before it can go: nothing, a map point, an enemy, or an area.</summary>
    internal enum GridInput : byte { Now, Point, Target, Area }

    internal readonly struct GridCell
    {
        public readonly GridOrder Order;
        public readonly string Id, Label, Tip, Pending;
        public readonly GridInput Input;
        public readonly MapMode Map;

        public GridCell(GridOrder order, string key, string label, GridInput input, MapMode map, string tip, string pending = null)
        {
            Order = order;
            Id = "tac.orders." + key;
            Label = label;
            Input = input;
            Map = map;
            Tip = tip;
            Pending = pending;
        }

        public bool Built => Pending == null;
    }

    /// <summary>TACTICAL › ORDERS' one grid (spec WMC rebuild §TACTICAL): four labelled rows; an order that needs a point or
    /// an enemy latches and arms the map, the rest act on the scope at once. With helicopters in scope SWEEP becomes SCOUT
    /// and SUPPORT becomes TAKE OFF · RESCUE · LAND · CARGO (RTB stays on every member row).</summary>
    internal static class OrderGrid
    {
        public const int Rows = 4, Columns = 4;
        public static readonly string[] RowLabels = { "OFFENSE", "MOVE", "DEFENSE", "SUPPORT" };
        private const string Later = " Arrives in a later update.";

        private static readonly GridCell Attack = new GridCell(GridOrder.Attack, "attack", "ATTACK", GridInput.Target, MapMode.Attack,
            "Right-click an enemy on the map to attack it; shift-click adds targets.");
        private static readonly GridCell Splash = new GridCell(GridOrder.Splash, "splash", "SPLASH", GridInput.Now, MapMode.Off,
            "One missile at the nearest enemy aircraft.");
        private static readonly GridCell Engage = new GridCell(GridOrder.Engage, "engage", "ENGAGE", GridInput.Now, MapMode.Off,
            "Fight the enemies in reach, then come back.");
        private static readonly GridCell Sweep = new GridCell(GridOrder.Sweep, "sweep", "SWEEP", GridInput.Area, MapMode.Off,
            "Search an area and fight what is found there.", "SWEEP: search an area and fight what is found there." + Later);
        private static readonly GridCell Scout = new GridCell(GridOrder.Scout, "scout", "SCOUT", GridInput.Now, MapMode.Off,
            "A low route 20 km ahead, reporting contacts.");
        private static readonly GridCell Move = new GridCell(GridOrder.Move, "move", "MOVE", GridInput.Point, MapMode.Move,
            "Right-click the map: fly there, then orbit.");
        private static readonly GridCell Orbit = new GridCell(GridOrder.Orbit, "orbit", "ORBIT", GridInput.Point, MapMode.Orbit,
            "Right-click the map: orbit there. HERE orbits where they are now.");
        private static readonly GridCell Cap = new GridCell(GridOrder.Cap, "cap", "CAP", GridInput.Area, MapMode.Off,
            "Orbit a point and fight what enters the radius.", "CAP: orbit a point and fight what enters the radius." + Later);
        private static readonly GridCell Hold = new GridCell(GridOrder.Hold, "hold", "HOLD", GridInput.Point, MapMode.Hold,
            "Right-click the map: hold there. HERE holds where they are now.");
        private static readonly GridCell Break = new GridCell(GridOrder.Break, "break", "BREAK", GridInput.Now, MapMode.Off,
            "Stop fighting and rejoin.");
        private static readonly GridCell FormUp = new GridCell(GridOrder.FormUp, "formup", "FORM UP", GridInput.Now, MapMode.Off,
            "Back to formation on you.");
        private static readonly GridCell Ecm = new GridCell(GridOrder.Ecm, "ecm", "ECM", GridInput.Now, MapMode.Off,
            "Jam the radars locking the scope.", "ECM: jam the radars locking the scope." + Later);
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
