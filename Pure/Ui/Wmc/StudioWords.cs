using System.Globalization;

namespace WingCommand
{
    /// <summary>Where the studio's draft stands against the saved pilots.</summary>
    internal enum DraftState : byte { New, Saved, Edited, NotSaved }

    /// <summary>The SQUADRON notch's words (research squadron-studio §2.3–§2.5; critic resolution 1: R6's SquadronWords are WING's): the
    /// chip, the list head, the SERVICE record, why a pilot cannot be discharged, the look steppers' values, the bio counter, asks,
    /// toasts and the empty and client states. Never "…", "†", U+25xx shapes or arrows.</summary>
    internal static class StudioWords
    {
        public const int ButtonChars = 11;
        public const string Title = "PILOTS", StudioTitle = "STUDIO", RecordTitle = "RECORD", ServiceTitle = "SERVICE", MissionTitle = "THIS MISSION";
        public const string PerksTitle = "PERKS", AppearanceTitle = "APPEARANCE", IdentityTitle = "IDENTITY", BioTitle = "BIO";
        public const string NotSaved = "NOT SAVED · SAVE keeps this pilot for every mission";
        public const string NotInMission = "NOT IN THIS MISSION · RECRUIT adds them now";
        public const string ClientRecord = "THE HOST KEEPS THIS MISSION'S ROSTER · your saved pilots are this machine's";
        public const string UnsavedAsk = "UNSAVED EDITS · click the row again to drop them";
        public const string Hint = "A row opens a pilot; SAVE keeps identity and look for every mission. Every pilot starts a mission a ROOKIE.";
        public const string NoPilot = "NO PILOT · NEW makes one";

        public static string Chip(DraftState s, out string rail)
        {
            switch (s)
            {
                case DraftState.New: rail = "info"; return "NEW";
                case DraftState.Saved: rail = "live"; return "SAVED";
                case DraftState.Edited: rail = "warn"; return "EDITED";
                default: rail = "inert"; return "NOT SAVED";
            }
        }

        public static string ListHead(int saved, int live)
        {
            if (saved <= 0 && live <= 0) return "NONE SAVED";
            string s = N(saved) + " SAVED";
            return live > 0 ? s + " · " + N(live) + " THIS MISSION" : s;
        }

        public static string Service(int missions, int sorties, int kills) =>
            Count(missions, "MISSION", "MISSIONS") + " · " + Count(sorties, "SORTIE", "SORTIES") + " · " + Count(kills, "KILL", "KILLS");

        public static string BestRank(int missions, int bestXp) =>
            missions <= 0 ? "NO MISSIONS YET" : "BEST RANK " + PilotPerks.RankName(PilotPerks.RankFor(bestXp));

        /// <summary>Why this pilot cannot leave the squadron now, or null when it is free.</summary>
        public static string DischargeWhy(PilotStatus s)
        {
            string w;
            switch (s)
            {
                case PilotStatus.Free: return null;
                case PilotStatus.Flying: w = "IN THE AIR"; break;
                case PilotStatus.Inbound: w = "ON A LAUNCH"; break;
                case PilotStatus.Downed:
                case PilotStatus.Rescue: w = "DOWNED — SAR"; break;
                case PilotStatus.Missing: w = "MIA"; break;
                case PilotStatus.LocalSar: w = "LOCAL SAR"; break;
                case PilotStatus.Captured: w = "CAPTURED"; break;
                default: w = "KIA"; break;
            }
            return "Only a free pilot can be discharged: " + w;
        }

        public static string RecruitLabel(bool onRoster, bool asking) => !onRoster ? "RECRUIT" : asking ? "DISCHARGE?" : "DISCHARGE";

        public const string RecruitTip = "Add this saved pilot to this mission now, a ROOKIE with 0 XP.";
        public const string DischargeTip = "Take this pilot off this mission's roster (press twice). A saved pilot stays saved.";

        public static string Hair(int h) => h <= 0 ? "BALD" : N(h) + "/" + N(PilotPortraitGenerator.HairCount - 1);

        public static string Face(int f) => N(f + 1) + "/" + N(PilotPortraitGenerator.FacesPerBody);

        public static string Scene(int s) => N(s + 1) + "/" + N(PilotPortraitGenerator.BackdropCount);

        public static string BioCounter(int n) => N(n) + "/" + N(PilotText.BioChars);

        public static string Radio(ChatterPersona p) => p.ToString().ToUpperInvariant();

        public static string DeleteAsk(string callsign) => "Delete " + callsign + " from the saved pilots? Press DELETE again (this mission keeps them)";

        public static string ImportToast(int added, int skipped, int files) =>
            "IMPORT · " + N(added) + " added, " + N(skipped) + " skipped from " + Count(files, "file", "files");

        public static string ExportToast(int pilots) => "EXPORT · " + Count(pilots, "pilot", "pilots") + " to Pilots/exported_pilots.json";

        public static string EmptyList(int files) =>
            "NO SAVED PILOTS · NEW makes one" + (files > 0 ? "; IMPORT reads " + Count(files, "file", "files") : "");

        public static string Saved(string callsign) => "Saved " + callsign;

        public static string SaveFailed => "Could not write pilots.user.json: the pilot is not saved";

        private static string Count(int n, string one, string many) => N(n) + " " + (n == 1 ? one : many);

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
