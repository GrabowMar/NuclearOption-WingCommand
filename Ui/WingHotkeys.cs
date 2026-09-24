using System;
using BepInEx.Configuration;

namespace WingCommand
{
    /// <summary>Keyboard shortcuts for every radial command (all unbound by default), joystick buttons for the commands
    /// used in flight (spec M7 §4, through the game's Rewired joysticks), and the radial restore timer.</summary>
    internal sealed class WingHotkeys : IWingService
    {
        public string Name => "Hotkeys";

        private HotasBinding[] bindings;
        private bool[] bound;
        private string[] parsed;
        private bool anyBound, reported;

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
            TickHotas(s);
        }

        private static bool Down(ConfigEntry<KeyboardShortcut> key) =>
            key.Value.MainKey != UnityEngine.KeyCode.None && key.Value.IsDown();

        /// <summary>Spec M7 §4: each bound command on its button's press; the logger when asked.</summary>
        private void TickHotas(WingConfig s)
        {
            Parse(s);
            if (!anyBound && !s.HotasLogButtons.Value) return;
            if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return;
            var joysticks = Rewired.ReInput.controllers.Joysticks;
            if (joysticks == null || joysticks.Count == 0) return;
            for (int j = 0; j < joysticks.Count; j++)
            {
                Rewired.Joystick stick = joysticks[j];
                if (stick == null) continue;
                if (s.HotasLogButtons.Value)
                    for (int b = 0; b < stick.buttonCount; b++)
                        if (stick.GetButtonDown(b)) Plugin.Logger.LogInfo($"[Hotas] {stick.name}: button {b + 1}");
                if (!anyBound) continue;
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!bound[i] || bindings[i].Button > stick.buttonCount || !bindings[i].Matches(stick.name)) continue;
                    if (stick.GetButtonDown(bindings[i].Button - 1)) Run(WingConfig.HotasCommands[i]);
                }
            }
        }

        /// <summary>Parses a binding again only when its text changed; a malformed one is logged once and ignored.</summary>
        private void Parse(WingConfig s)
        {
            int n = s.Hotas.Length;
            if (bindings == null)
            {
                bindings = new HotasBinding[n];
                bound = new bool[n];
                parsed = new string[n];
            }
            for (int i = 0; i < n; i++)
            {
                string text = s.Hotas[i].Value.Value;
                if (ReferenceEquals(text, parsed[i]) || text == parsed[i]) continue;
                parsed[i] = text;
                bound[i] = HotasBinding.TryParse(text, out bindings[i]);
                if (!bound[i] && !HotasBinding.IsUnbound(text))
                    Plugin.Logger.LogWarning($"[Hotas] {s.Hotas[i].Key} = \"{text}\" is not <device>:<button>; ignored");
                reported = false;
            }
            if (reported) return;
            reported = true;
            anyBound = false;
            for (int i = 0; i < n; i++) anyBound |= bound[i];
        }

        private static void Run(string command)
        {
            switch (command)
            {
                case "CallWingman": WingCommands.Call(1); break;
                case "FormUp": WingCommands.FormUp(); break;
                case "NextShape": WingCommands.NextShape(); break;
                case "NextSpacing": WingCommands.CycleSpacing(); break;
                case "Dismiss": WingCommands.Dismiss(); break;
                case "Engage": WingCommands.Engage(); break;
                case "Disengage": WingCommands.Disengage(); break;
                case "AttackTarget": WingCommands.AttackTarget(); break;
                case "Splash": WingCommands.Splash(); break;
                case "ClearMySix": WingCommands.ClearMySix(); break;
                case "BogeyDope": WingCommands.BogeyDope(); break;
                case "Rtb": WingCommands.Rtb(); break;
                case "OrbitHere": WingCommands.OrbitHere(); break;
                case "GoHigh": WingCommands.Stack(WingCommands.GoHighMetres, "Going high"); break;
                case "GoLow": WingCommands.Stack(WingCommands.GoLowMetres, "Going low"); break;
                case "Level": WingCommands.Stack(0f, "Level with you"); break;
                case "ApOff": WingCommands.Autopilot(ApCommand.Off); break;
                default: throw new InvalidOperationException("unknown HOTAS command " + command);
            }
        }
    }
}
