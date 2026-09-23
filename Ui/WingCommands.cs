using System;

namespace WingCommand
{
    /// <summary>Player commands shared by the radial and the hotkeys.</summary>
    internal static class WingCommands
    {
        public static void Call(int n) => SpawnService.Instance?.Call(n, CallAirframe());

        public static void FormUp()
        {
            if (!Ready(out WingService w)) return;
            w.FormUp();
            WingToast.Show("Form up");
        }

        public static void NextShape()
        {
            if (Ready(out WingService w)) w.NextShape();
        }

        public static void NextFamily()
        {
            if (Ready(out WingService w)) w.NextFamily();
        }

        public static void SetSpacing(SpacingPreset preset)
        {
            if (Ready(out WingService w)) w.SetSpacing(preset);
        }

        public static void CycleSpacing()
        {
            if (Ready(out WingService w)) w.SetSpacing((SpacingPreset)(((int)w.Selection.Spacing + 1) % 4));
        }

        public static void Dismiss()
        {
            if (!Ready(out WingService w)) return;
            if (w.Members.Count == 0)
            {
                WingToast.Show("No wingmen to dismiss");
                return;
            }
            w.Dismiss();
            WingToast.Show("Wing dismissed");
        }

        /// <summary>The configured airframe to call, or null for the player's own type.</summary>
        public static AircraftDefinition CallAirframe()
        {
            string name = Plugin.Settings.CallAirframe.Value;
            if (string.IsNullOrWhiteSpace(name)) return null;
            var catalogue = Encyclopedia.i != null ? Encyclopedia.i.aircraft : null;
            if (catalogue != null)
                foreach (AircraftDefinition d in catalogue)
                    if (d != null && string.Equals(d.unitName, name.Trim(), StringComparison.OrdinalIgnoreCase)) return d;
            WingToast.Show($"Unknown airframe '{name}'; calling your own type");
            return null;
        }

        private static bool Ready(out WingService w)
        {
            w = WingService.Instance;
            if (w?.Selection != null) return true;
            WingToast.Show("Wing Command is not ready");
            return false;
        }
    }
}
