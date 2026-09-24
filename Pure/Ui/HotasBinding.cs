using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>Spec M7 §4: a joystick button bound to a wing command, written <c>device:button</c> — any fragment of the
    /// joystick's name (case-insensitive) or <c>any</c>, and the button counted from 1. A colon inside the name is kept
    /// (the button is after the last one).</summary>
    internal struct HotasBinding
    {
        /// <summary>The name fragment to match, or null for any device.</summary>
        public string Device;
        public int Button;

        public static bool IsUnbound(string text) => string.IsNullOrWhiteSpace(text);

        public static bool TryParse(string text, out HotasBinding binding)
        {
            binding = default;
            if (IsUnbound(text)) return false;
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || colon == text.Length - 1) return false;
            string device = text.Substring(0, colon).Trim();
            if (device.Length == 0) return false;
            if (!int.TryParse(text.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int button) || button < 1)
                return false;
            binding = new HotasBinding
            {
                Device = string.Equals(device, "any", StringComparison.OrdinalIgnoreCase) ? null : device,
                Button = button,
            };
            return true;
        }

        public bool Matches(string deviceName) =>
            Device == null || (deviceName != null && deviceName.IndexOf(Device, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
