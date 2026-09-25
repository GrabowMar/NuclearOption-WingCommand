using System.Globalization;
using System.Text;

namespace WingCommand
{
    /// <summary>WING's words (spec WMC rebuild §WING): one status word per pilot on the row, the dossier stamp and the tiles; the slot line
    /// that says where the pilot is; the SAR and RELEASE buttons with their reasons; the empty and client states. Capped at the source,
    /// never with "…" or "†".</summary>
    internal static class SquadronWords
    {
        public const int RowChars = 22, StampChars = 11, CaptionChars = 22, SlotChars = 90, SarChars = 14, NameChars = 17;
        public const string ClientWhy = "THE HOST KEEPS THE SQUADRON ROSTER";
        public const string Empty = "NO PILOTS · RECRUIT below, or requisition on SUPPLY";
        public const string NoFocus = "NO PILOT SELECTED";
        public const string StudioWhy = "The pilot studio lives in the planning room's SQUADRON. Arrives in a later update.";

        public static string Row(PilotStatus s, bool next, int number)
        {
            switch (s)
            {
                case PilotStatus.Inbound: return "INBOUND";
                case PilotStatus.Flying: return "FLYING #" + N(number);
                case PilotStatus.Downed: return "DOWNED — SAR";
                case PilotStatus.Rescue: return "SAR #" + N(number) + " GOING";
                case PilotStatus.Missing: return "MIA";
                case PilotStatus.LocalSar: return "LOCAL SAR";
                case PilotStatus.Captured: return "CAPTURED";
                case PilotStatus.Kia: return "KIA";
                default: return next ? "FREE · NEXT UP" : "FREE";
            }
        }

        public static string Stamp(PilotStatus s, bool next, int number)
        {
            switch (s)
            {
                case PilotStatus.Downed: return "DOWNED";
                case PilotStatus.Rescue: return "SAR GOING";
                case PilotStatus.Free: return next ? "NEXT UP" : "FREE";
                default: return Row(s, next, number);
            }
        }

        public static string Rail(PilotStatus s, bool next)
        {
            switch (s)
            {
                case PilotStatus.Free: return next ? "info" : "inert";
                case PilotStatus.Inbound: return "info";
                case PilotStatus.Flying: return "live";
                case PilotStatus.Captured:
                case PilotStatus.Kia: return "danger";
                default: return "warn";
            }
        }

        public static string Level(PilotStatus s) =>
            s == PilotStatus.Flying ? "ok" : s == PilotStatus.Captured || s == PilotStatus.Kia ? "bad" : PilotStatuses.Lost(s) ? "warn" : "";

        public static string Badge(WingRank r) => PilotPerks.RankName(r).Substring(0, 1);

        public static string Head(int pilots, int sar, int kia)
        {
            if (pilots <= 0) return "NO PILOTS";
            var sb = new StringBuilder(N(pilots) + (pilots == 1 ? " PILOT" : " PILOTS"));
            if (sar > 0) sb.Append(" · ").Append(N(sar)).Append(" SAR");
            if (kia > 0) sb.Append(" · ").Append(N(kia)).Append(" KIA");
            return sb.ToString();
        }

        public static string PilotsCaption(int flying, int inbound)
        {
            var sb = new StringBuilder();
            Part(sb, flying, "FLYING");
            Part(sb, inbound, "INBOUND");
            return sb.Length == 0 ? "NONE FLYING" : sb.ToString();
        }

        public static string ReadyCaption(string next) =>
            string.IsNullOrEmpty(next) ? "NEW PILOT AT LAUNCH" : "NEXT " + WmcText.Cut(next, CaptionChars - 5);

        public static string LostCaption(int sar, int kia, int captured)
        {
            var sb = new StringBuilder();
            Part(sb, sar, "SAR");
            Part(sb, kia, "KIA");
            Part(sb, captured, "POW");
            return sb.Length == 0 ? "NONE LOST" : sb.ToString();
        }

        // ---- the pinned AIRFRAME ASSIGNMENT bar

        public static string Airframe(string current, string lastFlew) =>
            !string.IsNullOrEmpty(current) ? current : !string.IsNullOrEmpty(lastFlew) ? "LAST FLEW " + lastFlew : "NO AIRFRAME YET";

        public static string FlyingSlot(int number, string element, string duty) => Cap("#" + N(number) + " · " + element + " · " + duty);

        public static string InboundSlot(string phase, string field) => Cap(phase + (string.IsNullOrEmpty(field) ? "" : " · FROM " + field));

        public static string FreeSlot(bool next) => next ? "NEXT UP · FLIES THE NEXT LAUNCH" : "FREE · SUPPLY SEATS THE NEXT UP FIRST";

        public static string DownedSlot(string localCost) => Cap("DOWNED · AIR SAR SENDS A HELICOPTER · LOCAL SAR " + localCost);

        public static string RescueSlot(int rescuer) => "SAR #" + N(rescuer) + " EN ROUTE";

        public static string MissingSlot(string localCost, string duration) => Cap("MIA · NO SIGNAL · LOCAL SAR " + localCost + " SEARCHES " + duration);

        public static string LocalSarSlot(string countdown) => "LOCAL SAR · BACK IN " + countdown;

        public const string CapturedSlot = "CAPTURED · OUT FOR THIS MISSION";

        public static string KiaSlot(string cause, string killer) =>
            Cap("KIA" + (string.IsNullOrEmpty(cause) ? "" : " · " + cause.ToUpperInvariant())
                + (string.IsNullOrEmpty(killer) ? "" : " · BY " + killer.ToUpperInvariant()));

        // ---- the dossier

        public static string Record(int kills, int sorties) =>
            N(kills) + (kills == 1 ? " KILL · " : " KILLS · ") + N(sorties) + (sorties == 1 ? " SORTIE" : " SORTIES");

        public static string Persona(string persona) => "RADIO · " + WmcText.Cut((persona ?? "STANDARD").ToUpperInvariant(), 24);

        public static string ReleaseLabel(bool asking) => asking ? "RELEASE?" : "RELEASE";

        public static string ReleaseWhy(PilotStatus s, bool client) =>
            client ? ClientWhy : s == PilotStatus.Flying ? null : "Only a flying pilot can be released";

        public static string ReleaseAsk(string callsign, int number) =>
            "Release #" + N(number) + " " + callsign + " to the game's AI? The pilot reads FREE; the jet flies on. Press RELEASE again";

        // ---- search and rescue

        public static string AirLabel(int rescuer) => rescuer > 0 ? "#" + N(rescuer) + " GOING" : "AIR SAR";

        /// <summary>Why AIR SAR cannot go for this pilot (the helicopter and ground checks come from the wing).</summary>
        public static string AirWhy(PilotStatus s, bool client)
        {
            if (client) return ClientWhy;
            if (s == PilotStatus.Downed) return null;
            if (s == PilotStatus.Missing) return "No survivor signal: LOCAL SAR searches";
            if (s == PilotStatus.Rescue) return "A helicopter is already on the way";
            return "Only a pilot down on land can be picked up";
        }

        public static string LocalLabel(bool asking, string countdown) =>
            !string.IsNullOrEmpty(countdown) ? countdown + " LEFT" : asking ? "LOCAL SAR?" : "LOCAL SAR";

        /// <summary>Why LOCAL SAR cannot search for this pilot (funds and the waiting window come from the search itself).</summary>
        public static string LocalWhy(PilotStatus s, bool client)
        {
            if (client) return ClientWhy;
            if (s == PilotStatus.Downed || s == PilotStatus.Missing) return null;
            if (s == PilotStatus.LocalSar) return "A local search is already under way";
            return "Only a missing or downed pilot can be searched for";
        }

        public static string LocalAsk(string callsign, string cost, string duration) =>
            "Local search for " + callsign + ": " + cost + ", back in " + duration + ". Press LOCAL SAR again";

        public static string Alert(PilotStatus s, string callsign) =>
            s == PilotStatus.Downed ? callsign + " DOWNED — AIR SAR OR LOCAL SAR"
            : s == PilotStatus.Missing ? callsign + " MIA — LOCAL SAR CAN SEARCH" : null;

        public static string Hint(bool client, int pilots) =>
            client ? "The host keeps the squadron roster; you can look."
            : pilots == 0 ? "Recruit a pilot, or requisition on SUPPLY: a new pilot is drafted at launch."
            : "A row opens the dossier; SUPPLY's pilot card picks who flies next.";

        public static string Recruited(string callsign, string name) => "Recruited " + callsign + (string.IsNullOrEmpty(name) ? "" : " (" + name + ")");

        private static void Part(StringBuilder sb, int n, string word)
        {
            if (n <= 0) return;
            string part = N(n) + " " + word;
            if (sb.Length + (sb.Length > 0 ? 3 : 0) + part.Length > CaptionChars) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(part);
        }

        private static string Cap(string s) => WmcText.Cut(s, SlotChars);

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
