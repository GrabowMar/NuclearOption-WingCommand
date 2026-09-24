namespace WingCommand
{
    /// <summary>The map-order modes of the ORDERS strip (spec WMC program §5). SWEEP and CAP join with their orders (P9).</summary>
    internal enum MapMode : byte { Off, Move, Route, Orbit, Hold, Attack, Cargo }

    /// <summary>What is under the cursor on a right-click.</summary>
    internal enum MapPointer : byte { Empty, Enemy, Other }

    /// <summary>What a right-click does.</summary>
    internal enum MapClick : byte { None, Move, AddPoint, Orbit, Hold, Attack, AddTarget, Cargo, NeedEnemy }

    /// <summary>Right-click rules of the map layer (spec WMC program §5): an armed mode places its order for the scope; with
    /// no mode and wingmen selected a right-click is MOVE (0.9); otherwise, or while another mod holds the map, the
    /// right-click stays the game's.</summary>
    internal static class MapOrders
    {
        public static readonly MapMode[] Strip = { MapMode.Move, MapMode.Route, MapMode.Orbit, MapMode.Hold, MapMode.Attack, MapMode.Cargo };

        public static string Label(MapMode m) => m == MapMode.Off ? "OFF" : m.ToString().ToUpperInvariant();

        public static bool Consumes(MapMode mode, bool selection, bool otherOwner) => !otherOwner && (mode != MapMode.Off || selection);

        public static MapClick Resolve(MapMode mode, bool selection, MapPointer pointer, bool shift)
        {
            switch (mode)
            {
                case MapMode.Off: return selection ? MapClick.Move : MapClick.None;
                case MapMode.Move: return MapClick.Move;
                case MapMode.Route: return MapClick.AddPoint;
                case MapMode.Orbit: return MapClick.Orbit;
                case MapMode.Hold: return MapClick.Hold;
                case MapMode.Cargo: return MapClick.Cargo;
                case MapMode.Attack:
                    return pointer != MapPointer.Enemy ? MapClick.NeedEnemy : shift ? MapClick.AddTarget : MapClick.Attack;
                default: return MapClick.None;
            }
        }

        public static string Prompt(MapMode mode, string scope)
        {
            switch (mode)
            {
                case MapMode.Off: return null;
                case MapMode.Attack: return "ATTACK · " + scope + " · RIGHT-CLICK AN ENEMY (SHIFT ADDS)";
                case MapMode.Route: return "ROUTE · " + scope + " · RIGHT-CLICK TO ADD POINTS, THEN SEND";
                default: return Label(mode) + " · " + scope + " · RIGHT-CLICK THE MAP";
            }
        }
    }
}
