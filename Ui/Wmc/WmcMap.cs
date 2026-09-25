namespace WingCommand
{
    /// <summary>Points the maximized map at a unit (WING CENTER, LOG rows).</summary>
    internal static class WmcMap
    {
        public static void Center(Unit unit)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || unit == null) return;
            map.SetMapTarget(unit.GlobalPosition());
        }
    }
}
