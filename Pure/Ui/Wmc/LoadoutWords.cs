using System;
using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>LOADOUT's words (spec WMC rebuild §LOADOUT; critique 11: one phrase, NO TEMPLATE, everywhere): the build card, the
    /// chain, station and row words, the STATIONS / MASS / ROLE tiles and captions, the template bar's refusals and the hints — each
    /// capped at the source, never with "…".</summary>
    internal static class LoadoutWords
    {
        public const int ChipChars = 11, ValueChars = 7, CaptionChars = 22, StationChars = 40, StoreChars = 48, LiveryChars = 36;
        public const string NoTemplate = "NO TEMPLATE", EmptyStore = "EMPTY", MassCaption = "STORES ONLY", Standard = "STANDARD (FACTION)";
        public const string Unreadable = "This airframe's hardpoints cannot be read here";

        public static string Title(string code, string template) => code + " · " + (template ?? NoTemplate);

        /// <summary>NO TEMPLATE, then EDITED (the NAME field holds text not yet saved), then SUPPLY FIT, then SAVED.</summary>
        public static string Chip(bool hasTemplate, bool editing, bool supplyFit, out string state)
        {
            if (!hasTemplate)
            {
                state = "warn";
                return NoTemplate;
            }
            if (editing)
            {
                state = "warn";
                return "EDITED";
            }
            state = supplyFit ? "info" : "live";
            return supplyFit ? "SUPPLY FIT" : "SAVED";
        }

        public static string Picker(string template) => (template ?? NoTemplate) + " ›";

        public static string Chain(string code, string template, int stations) =>
            template == null ? code + " › " + NoTemplate : code + " › " + template + " › " + Count(stations, "STATION", "STATIONS");

        /// <summary>The station's name and, for a pair or a multi-pylon set, how many pylons it fits ("Fuselage Pylon ×2").</summary>
        public static string StationName(string name, int pylons)
        {
            string suffix = pylons > 1 ? " ×" + N(pylons) : "";
            return WmcText.Cut(name, StationChars - suffix.Length) + suffix;
        }

        public static string Stations(in FitSummary f) => N(f.Fitted) + "/" + N(f.Stations);

        public static string Mass(float kg)
        {
            double v = Math.Round(Math.Max(0f, kg));
            return v < 1_000_000 ? v.ToString("N0", CultureInfo.InvariantCulture) : (v / 1000).ToString("N0", CultureInfo.InvariantCulture) + "t";
        }

        /// <summary>A-A or A-G when one side has half again the other's rounds, MULTI when both fly, else what there is.</summary>
        public static string Role(in FitSummary f)
        {
            int aa = f.Aam, ag = f.Agm + f.Bombs;
            if (aa + ag > 0)
            {
                if (aa > ag * 1.5f) return "A-A";
                if (ag > aa * 1.5f) return "A-G";
                return "MULTI";
            }
            if (f.Cargo > 0) return "CARGO";
            if (f.MslDef > 0) return "MSL DEF";
            if (f.Ecm > 0) return "ECM";
            return "UNARMED";
        }

        /// <summary>The rounds that fly by kind, whole parts only, within 22 characters.</summary>
        public static string RoleCaption(in FitSummary f)
        {
            var sb = new StringBuilder();
            Part(sb, f.Aam, "AAM", "AAM");
            Part(sb, f.Agm, "AGM", "AGM");
            Part(sb, f.Bombs, "BOMB", "BOMBS");
            Part(sb, f.Ecm, "ECM", "ECM");
            Part(sb, f.Cargo, "CARGO", "CARGO");
            Part(sb, f.MslDef, "MSL DEF", "MSL DEF");
            return sb.ToString();
        }

        /// <summary>What does not fly and why, whole parts within 22 characters; ALL FITTED when everything does.</summary>
        public static string StationsCaption(in FitSummary f, bool hasTemplate, int emptyHere)
        {
            if (!hasTemplate) return NoTemplate;
            int empty = Math.Max(0, f.Stations - f.Fitted - f.Blocked - emptyHere);
            var sb = new StringBuilder();
            Part(sb, emptyHere, "EMPTY HERE", "EMPTY HERE");
            Part(sb, f.Blocked, "BLOCKED", "BLOCKED");
            Part(sb, empty, "EMPTY", "EMPTY");
            return sb.Length == 0 ? "ALL FITTED" : sb.ToString();
        }

        public static string RowMass(float kg) => kg <= 0f ? WmcText.Unknown : Math.Round(kg).ToString("N0", CultureInfo.InvariantCulture) + " kg";

        public static string HardpointsNote(int stations, int blocked) =>
            Count(stations, "STATION", "STATIONS") + (blocked > 0 ? " · " + N(blocked) + " BLOCKED" : "");

        /// <summary>The status strip's alert when stations hold stores this mission refuses; null when none.</summary>
        public static string EmptyHereAlert(int n) =>
            n <= 0 ? null : n == 1 ? "1 STATION LAUNCHES EMPTY HERE · clear it or fit another store"
            : N(n) + " STATIONS LAUNCH EMPTY HERE · clear them or fit other stores";

        public static string DeleteLabel(bool asking) => asking ? "DELETE?" : "DELETE";

        public static string DeleteAsk(string name) => "Press DELETE again to delete " + name + " · aircraft in the air keep their fit";

        public static string Deleted(string name, bool wasSupplyFit) => name + " deleted" + (wasSupplyFit ? " · SUPPLY FIT is AUTO again" : "");

        public static string NewWhy(bool readable, int count) =>
            !readable ? Unreadable : count >= TemplateNames.PerAirframe ? Limit : null;

        public static string CopyWhy(bool hasTemplate, int count) =>
            !hasTemplate ? "Pick a template to copy" : count >= TemplateNames.PerAirframe ? Limit : null;

        public static string DeleteWhy(bool hasTemplate) => hasTemplate ? null : "No template to delete";

        public static string LiveryKey(string code) => "LIVERY · " + code;

        public static string Livery(string label) => WmcText.Cut(label, LiveryChars);

        public static string Hint(bool client, bool hasAirframe, bool hasTemplate, string code)
        {
            if (client) return "SAVED ON THIS PC · THE HOST USES ITS OWN templates when you fly for it.";
            if (!hasAirframe) return "Pick an airframe to edit its templates.";
            if (!hasTemplate) return "NEW starts a template for " + code + "; until then SUPPLY FIT offers AUTO and YOUR LOADOUT.";
            return "A saved preset, not the aircraft in the air: SUPPLY FIT flies it. Click a station to fit a store.";
        }

        private static readonly string Limit = N(TemplateNames.PerAirframe) + " templates is this airframe's limit · delete one first";

        private static void Part(StringBuilder sb, int n, string one, string many)
        {
            if (n <= 0) return;
            string part = N(n) + " " + (n == 1 ? one : many);
            int add = (sb.Length > 0 ? 3 : 0) + part.Length;
            if (sb.Length + add > CaptionChars) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(part);
        }

        private static string Count(int n, string one, string many) => N(n) + " " + (n == 1 ? one : many);

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
