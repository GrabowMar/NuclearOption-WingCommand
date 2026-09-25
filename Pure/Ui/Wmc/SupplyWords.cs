using System;
using System.Globalization;

namespace WingCommand
{
    /// <summary>SUPPLY's page words (spec WMC rebuild §SUPPLY): tile code and name, step chips, the FIT choice, REQUISITION, the
    /// FUNDS and STOCK captions and the strip hint — each capped at the source, never with "…". The quote, blocker, dispatch
    /// line and tile foot are <see cref="ShopRules"/>' words.</summary>
    internal static class SupplyWords
    {
        public const int ChipChars = 15, CaptionChars = 22, CodeChars = 12, NameChars = 20, FitChars = 16, DispatchChars = 79;

        public const string PilotTitle = "PILOT & CREW", AirframeTitle = "AIRFRAME", FitTitle = "FIT & FUEL", BaseTitle = "LAUNCH BASE",
            InboundTitle = "INBOUND";

        /// <summary>The tile's code line: the game's code, else the name's first word.</summary>
        public static string Code(string code, string unitName)
        {
            string c = !string.IsNullOrEmpty(code) ? code : unitName ?? "";
            int space = c.IndexOf(' ');
            return WmcText.Cut(string.IsNullOrEmpty(code) && space > 0 ? c.Substring(0, space) : c, CodeChars);
        }

        /// <summary>The tile's name line: the unit name without its code ("FS-20 Vortex" → "Vortex").</summary>
        public static string Name(string unitName, string code)
        {
            if (string.IsNullOrEmpty(unitName)) return "";
            string s = unitName;
            if (!string.IsNullOrEmpty(code) && s.Length > code.Length + 1 && s[code.Length] == ' '
                && s.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(code.Length + 1).TrimStart();
            return WmcText.Cut(s, NameChars);
        }

        public static string AirframeChip(int listed) => listed <= 0 ? "NONE LISTED" : N(listed) + " LISTED";

        public static string Fuel(int percent) => "FUEL " + N(percent) + "%";

        public static string BaseChip(int on, int total) =>
            total <= 0 ? "NO FIELD" : on <= 0 ? "ALL OFF" : on == 1 ? "1 BASE ON" : N(on) + " BASES ON";

        /// <summary>The fit's word: AUTO (the game's own pick, the default), YOUR LOADOUT, or a template's name (≤ 16).</summary>
        public static string Fit(string fit, string templateName)
        {
            if (fit == null) return "AUTO";
            if (fit == CallSpec.YourLoadout) return "YOUR LOADOUT";
            return WmcText.Cut(string.IsNullOrEmpty(templateName) ? "TEMPLATE" : templateName.ToUpperInvariant(), FitChars);
        }

        public static string FitButton(string fitWord) => "FIT · " + fitWord + " ›";

        public static string FitDetail(string fit, bool templates)
        {
            if (fit == CallSpec.YourLoadout) return "YOUR LOADOUT: the loadout you set for this airframe, as you would fly it.";
            if (fit != null) return "A template saved on LOADOUT for this airframe.";
            return "AUTO: the game arms it for the mission, as it arms its own AI." + (templates ? "" : " Saved templates arrive with LOADOUT.");
        }

        public static string Requisition(float price) => "REQUISITION · " + Credits.Price(price);

        public static string FundsCaption(bool client, bool sandbox, bool selected, float price)
        {
            if (client) return "YOUR ALLOCATION";
            if (sandbox) return "SANDBOX · FREE";
            if (!selected) return "PICK AN AIRFRAME";
            return "NEXT CALL " + (price <= 0f ? "FREE" : Credits.Short(price) + " CR");
        }

        public static string StockCaption(string code, int stock, bool selected)
        {
            if (!selected) return "PICK AN AIRFRAME";
            return WmcText.Cut(code, CodeChars) + (stock > 0 ? " IN STOCK" : " NONE LEFT");
        }

        public static string InboundChip(int n) => N(n) + " ON THE WAY";

        public static string Hint(bool client, int inbound)
        {
            if (client) return "The host runs SUPPLY; you can look, the host requisitions.";
            if (inbound > 0) return N(inbound) + " inbound · watch LINK";
            return "Pick a pilot, an airframe, its fit and a base; REQUISITION launches it.";
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
