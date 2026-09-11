using System;

namespace WingCommand
{
    internal static class PilotIdentity
    {
        public static string Name(Func<int, int> next) =>
            ((char)('A' + next(26))) + ". " + Surnames[next(Surnames.Length)];

        public static string Callsign(Func<int, int> next, Func<string, bool> taken)
        {
            int start = next(Callsigns.Length);
            for (int i = 0; i < Callsigns.Length; i++)
            {
                string candidate = Callsigns[(start + i) % Callsigns.Length];
                if (!taken(candidate)) return candidate;
            }
            string stem = Callsigns[start];
            int suffix = 2;
            while (taken(stem + "-" + suffix)) suffix++;
            return stem + "-" + suffix;
        }

        public static string Background(Func<int, int> next, ChatterPersona persona) =>
            Postings[next(Postings.Length)] + " " + Habits[next(Habits.Length)] + " " +
            RadioHabits[(int)persona][next(RadioHabits[(int)persona].Length)];

        private static readonly string[] Surnames =
        {
            "Brennan", "Okonkwo", "Haldor", "Petrov", "Mancini", "Rask", "Oyelaran", "Steiner",
            "Adeyemi", "Alvarez", "Andersen", "Arslan", "Bae", "Baptiste", "Bauer", "Bennett",
            "Carvalho", "Chen", "Costa", "Dahl", "Dawson", "Demir", "Diallo", "Duarte",
            "Eriksson", "Farouk", "Fischer", "Flores", "Fontaine", "Gallo", "Garcia", "Ghosh",
            "Haddad", "Han", "Hayashi", "Herrera", "Ibrahim", "Ivanov", "Jensen", "Kaur",
            "Keller", "Kim", "Kovacs", "Kowalski", "Larsen", "Laurent", "Leclerc", "Lehtinen",
            "Lindholm", "Malik", "Marin", "Mensah", "Meyer", "Moreau", "Nakamura", "Navarro",
            "Nguyen", "Novak", "Nwosu", "Orlov", "Park", "Patel", "Pereira", "Quinn",
            "Reyes", "Rossi", "Sato", "Shah", "Silva", "Singh", "Sorensen", "Sullivan",
            "Tanaka", "Torres", "Tran", "Varga", "Vega", "Voss", "Weber", "Zielinski",
        };

        private static readonly string[] Callsigns =
        {
            "TALLY", "GRAVEL", "ANVIL", "SABLE", "PICKET", "RIPTIDE", "LANTERN", "DRAYMAN",
            "BADGER", "BANDSAW", "BELL", "BLACKBIRD", "BLUEJAY", "BOLT", "BRASS", "BREAKER",
            "BRICK", "BUCKLER", "CABLE", "CAIRN", "CANDLE", "CHALK", "CINDER", "CIRRUS",
            "COBALT", "COPPER", "CROW", "DASH", "DEADEYE", "DECOY", "DINGO", "DOWNSHIFT",
            "DRIFTER", "DUSTOFF", "ECHO", "EMBER", "FABLE", "FALCON", "FERRITE", "FLINT",
            "FOGHORN", "FOXGLOVE", "FROST", "GADFLY", "GANNET", "GASKET", "GHOST", "GLINT",
            "GOSHAWK", "GROUSE", "HALO", "HATCH", "HERON", "HUSH", "IBIS", "JACKDAW",
            "JAVELIN", "KESTREL", "KITE", "LATCH", "LOCKSTEP", "MAGPIE", "MARLIN", "MICA",
            "MOTH", "NEEDLE", "NICKEL", "NOMAD", "OARLOCK", "ONYX", "OSPREY", "PATCH",
            "PEREGRINE", "PIPER", "QUARRY", "QUILL", "RATCHET", "REDTAIL", "RELAY", "RIVET",
            "ROOK", "RUDDER", "SALT", "SHINGLE", "SLATE", "SPARROW", "SPECTER", "SPOOL",
            "STITCH", "TANGENT", "THIMBLE", "THISTLE", "TORCH", "TRACER", "WREN", "ZIPPER",
        };

        private static readonly string[] Postings =
        {
            "Transferred from a coastal patrol unit after its airstrip closed.",
            "Learned to fly supply runs between isolated mountain settlements.",
            "Former flight-test technician who traded the telemetry desk for a cockpit.",
            "Joined the squadron after a conversion course from heavy transports.",
            "Came through the reserve program while working nights in an engine shop.",
            "Former reconnaissance pilot with a habit of noticing the second road out.",
            "Trained at an inland academy where crosswinds were part of every checkride.",
            "Flew civilian survey routes before volunteering for military conversion.",
            "Returned to flying after a posting at a remote radar station.",
            "Moved from maritime search and rescue into armed escort work.",
            "Grew up beside a freight airfield and paid for lessons loading cargo.",
            "Qualified on helicopters before moving to fixed-wing operations.",
            "Former ferry pilot accustomed to unfamiliar cockpits and short briefings.",
            "Finished flight school on a dispersed strip with a temporary control tower.",
            "Left an aerobatic team when its aircraft were requisitioned.",
            "Began as a crew chief and still knows the maintenance staff by name.",
            "Transferred from a night-navigation training detachment.",
            "Flew medical supplies along the coast before joining the reserves.",
            "Arrived from an electronic-warfare unit with a battered notebook of radar signatures.",
            "Was assigned here after an advanced interception course.",
            "Learned instrument flying in weather that kept the rest of the school grounded.",
            "Spent a previous posting testing emergency procedures in a simulator.",
            "Entered flight training through an engineering scholarship.",
            "Former agricultural pilot with an unusually good eye for wires and pylons.",
            "Requested a frontline transfer after a quiet posting as a liaison pilot.",
            "Flew geological surveys over the northern plateau before enlisting.",
            "Completed conversion training with a unit that no longer exists.",
            "Served as a navigation instructor before returning to an operational squadron.",
            "Learned formation discipline towing gliders at a small civilian club.",
            "Transferred from a transport wing during the latest reorganization.",
            "Flew border observation routes from a succession of temporary airfields.",
            "Earned a commission after years servicing aircraft on the night shift.",
        };

        private static readonly string[] Habits =
        {
            "Draws escape routes on the back of every briefing sheet.",
            "Checks the fuel figures twice before discussing anything else.",
            "Keeps a folded map in a pocket even when the navigation system works.",
            "Collects unit patches but refuses to sew them onto a flight suit.",
            "Carries a cheap watch set to the airfield's clock.",
            "Writes letters home after every flight and rarely mentions the flying.",
            "Can identify most engines before the aircraft comes into view.",
            "Always brings an extra pencil to the briefing.",
            "Keeps the same dented mug on the ready-room shelf.",
            "Studies weather reports with more interest than the mission board.",
            "Marks safe forced-landing sites on routine transit flights.",
            "Plays correspondence chess with someone at a distant base.",
            "Repairs old radios during stand-downs.",
            "Remembers ground crews' coffee orders better than their ranks.",
            "Saves every handwritten note from the maintenance team.",
            "Has a tiny sketchbook full of unfamiliar coastlines.",
            "Counts aircraft returning to the pattern before leaving the apron.",
            "Refuses to change a pair of badly faded flying gloves.",
            "Reads detective novels with the last page carefully folded shut.",
            "Makes elaborate breakfasts whenever the alert schedule allows.",
            "Keeps a list of good landing strips and bad vending machines.",
            "Practices recognition silhouettes on scraps of packaging.",
            "Tunes the ready-room guitar but almost never plays it.",
            "Records a short voice diary after debriefing.",
            "Labels personal tools so carefully that nobody borrows them twice.",
            "Still carries a lucky token from the first solo flight.",
            "Takes the long way to the hangar to check the windsock.",
            "Trades paperback books with transport crews.",
            "Knows the distance to home from every base on the route map.",
            "Prefers a quiet walk around the aircraft to a motivational speech.",
            "Keeps a photograph tucked inside the checklist cover.",
            "Treats every debrief as a chance to learn one useful thing.",
        };

        private static readonly string[][] RadioHabits =
        {
            new[]
            {
                "Radio calls are precise, even off duty.",
                "Reads back the details everyone else assumes.",
                "Likes a clear plan and a clean handover.",
                "Leaves room on the frequency for the next voice.",
                "Corrects mistakes quietly and records them honestly.",
                "Has a checklist for the checklist.",
            },
            new[]
            {
                "Sounds impatient whenever the flight is holding.",
                "Volunteers first and asks about the weather second.",
                "Treats the training scoreboard as unfinished business.",
                "Keeps radio calls short and full of urgency.",
                "Wants the first slot on the launch schedule.",
                "Turns almost every ready-room game into a competition.",
            },
            new[]
            {
                "Speaks at the same measured pace during an alarm as at breakfast.",
                "Can make a long holding pattern sound uneventful.",
                "Waits for the frequency to clear before answering.",
                "Is usually the person who reminds the flight to breathe.",
                "Never raises a voice to win an argument.",
                "Makes time to check on the newest member of the flight.",
            },
            new[]
            {
                "Delivers jokes so flatly that new arrivals miss them.",
                "Describes alarming weather as a minor scheduling problem.",
                "Has an unflattering nickname for every aircraft flown.",
                "Keeps the best remarks for the moment after the danger passes.",
                "Answers compliments with a complaint about the coffee.",
                "Insists the aircraft is the difficult half of the partnership.",
            },
        };
    }
}
