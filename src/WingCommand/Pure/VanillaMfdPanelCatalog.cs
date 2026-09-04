using System;

namespace WingCommand
{
    /// <summary>
    /// Stable identities for the stock map MFD surfaces Wing Command replaces.
    ///
    /// The game mutates the faction screens' short names during mission setup, so runtime
    /// binding prefers their controller component. This small pure catalog is the fallback
    /// and the common vocabulary for diagnostics, tests, and the owned panel headers.
    /// </summary>
    internal enum VanillaMfdPanelId
    {
        Unknown,
        Bdf,
        Map,
        Hud,
        Pala,
        Tgt,
        Mis,
    }

    internal static class VanillaMfdPanelCatalog
    {
        public static VanillaMfdPanelId FromShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName)) return VanillaMfdPanelId.Unknown;

            switch (shortName.Trim().ToUpperInvariant())
            {
                case "BDF": return VanillaMfdPanelId.Bdf;
                case "MAP": return VanillaMfdPanelId.Map;
                case "HUD": return VanillaMfdPanelId.Hud;
                case "PALA": return VanillaMfdPanelId.Pala;
                case "TGT": return VanillaMfdPanelId.Tgt;
                case "MIS": return VanillaMfdPanelId.Mis;
                default: return VanillaMfdPanelId.Unknown;
            }
        }

        public static string Label(VanillaMfdPanelId id)
        {
            switch (id)
            {
                case VanillaMfdPanelId.Bdf: return "BDF";
                case VanillaMfdPanelId.Map: return "MAP";
                case VanillaMfdPanelId.Hud: return "HUD";
                case VanillaMfdPanelId.Pala: return "PALA";
                case VanillaMfdPanelId.Tgt: return "TGT";
                case VanillaMfdPanelId.Mis: return "MIS";
                default: return "MFD";
            }
        }
    }
}
