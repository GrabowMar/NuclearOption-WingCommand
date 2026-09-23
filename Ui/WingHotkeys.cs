using BepInEx.Configuration;

namespace WingCommand
{
    /// <summary>Keyboard shortcuts for every radial command (all unbound by default), and the radial restore
    /// timer.</summary>
    internal sealed class WingHotkeys : IWingService
    {
        public string Name => "Hotkeys";

        public void Activate()
        {
        }

        public void Deactivate() => WingRadialMenu.Reset();

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            WingRadialMenu.Tick();
            WingConfig s = Plugin.Settings;
            if (Down(s.KeyCallWingman)) WingCommands.Call(1);
            if (Down(s.KeyFormUp)) WingCommands.FormUp();
            if (Down(s.KeyNextShape)) WingCommands.NextShape();
            if (Down(s.KeyNextSpacing)) WingCommands.CycleSpacing();
            if (Down(s.KeyDismiss)) WingCommands.Dismiss();
            if (Down(s.KeyApLevel)) WingCommands.Autopilot(ApCommand.Level);
            if (Down(s.KeyApHeading)) WingCommands.Autopilot(ApCommand.Heading);
            if (Down(s.KeyApAltitude)) WingCommands.Autopilot(ApCommand.Altitude);
            if (Down(s.KeyApVerticalSpeed)) WingCommands.Autopilot(ApCommand.VerticalSpeed);
            if (Down(s.KeyApSpeed)) WingCommands.Autopilot(ApCommand.Speed);
            if (Down(s.KeyApOff)) WingCommands.Autopilot(ApCommand.Off);
        }

        private static bool Down(ConfigEntry<KeyboardShortcut> key) =>
            key.Value.MainKey != UnityEngine.KeyCode.None && key.Value.IsDown();
    }
}
