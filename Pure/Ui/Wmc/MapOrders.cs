namespace WingCommand
{
    /// <summary>The map-order modes armed from TACTICAL's order grid (spec WMC rebuild §TACTICAL). SWEEP and CAP join with
    /// their orders (R8).</summary>
    internal enum MapMode : byte { Off, Move, Route, Orbit, Hold, Attack, Cargo, Land, Cap, Sweep }

    /// <summary>What is under the cursor on a right-click.</summary>
    internal enum MapPointer : byte { Empty, Enemy, Other }

    /// <summary>What a right-click does.</summary>
    internal enum MapClick : byte { None, Move, AddPoint, Orbit, Hold, Attack, AddTarget, Cargo, NeedEnemy, Land, Cap, Sweep }

    /// <summary>Right-click rules of the map layer (spec WMC program §5): an armed mode places its order for the scope; with
    /// no mode and wingmen selected a right-click is MOVE (0.9); otherwise, or while another mod holds the map, the
    /// right-click stays the game's.</summary>
    internal static class MapOrders
    {
        public static string Label(MapMode m) => m == MapMode.Off ? "OFF" : m.ToString().ToUpperInvariant();

        public static bool Consumes(MapMode mode, bool selection, bool otherOwner) => Consumes(mode != MapMode.Off, selection, otherOwner);

        /// <summary>An order mode or a PLAN tool armed, or a selection for MOVE.</summary>
        public static bool Consumes(bool armed, bool selection, bool otherOwner) => !otherOwner && (armed || selection);

        /// <summary>CAP and SWEEP place an area: a right-press dragged sets its radius (spec bezel v2 §6).</summary>
        public static bool IsArea(MapMode mode) => mode == MapMode.Cap || mode == MapMode.Sweep;

        /// <summary>The radius a right-drag from the press point to the release point sets, within the guard's range.</summary>
        public static float DragRadius(float pressX, float pressZ, float releaseX, float releaseZ)
        {
            float dx = releaseX - pressX, dz = releaseZ - pressZ;
            return AreaGuard.Clamp((float)System.Math.Sqrt(dx * dx + dz * dz));
        }

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
                case MapMode.Land: return MapClick.Land;
                case MapMode.Cap: return MapClick.Cap;
                case MapMode.Sweep: return MapClick.Sweep;
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
