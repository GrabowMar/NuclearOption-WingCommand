using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Pilot radio style, independent of rank and flight skill.</summary>
    internal enum ChatterPersona
    {
        Professional,
        Aggressive,
        Calm,
        Dry,
    }

    /// <summary>Ambient flight line with an optional pilot reply.</summary>
    internal readonly struct ChatterExchange
    {
        public readonly string Opening;
        public readonly string Reply;
        public readonly string SpeakerTag;
        public readonly string ReplyTag;

        public ChatterExchange(string opening, string reply = null,
                               string speakerTag = null, string replyTag = null)
        {
            Opening = opening;
            Reply = reply;
            SpeakerTag = speakerTag;
            ReplyTag = replyTag;
        }
    }

    /// <summary>Engine-free radio presentation and dialogue selection.</summary>
    internal static class ChatterDialogue
    {
        // Keep ambient dialogue in static data and within the pilots' world.
        private static readonly ChatterExchange[] ambient =
        {
            new ChatterExchange("If the sky turns orange, I'm blaming the briefing officer.",
                                "Briefing said scattered clouds. Orange is technically scattered."),
            new ChatterExchange("Somebody remind me: are borders the thing we're defending or the thing we're crossing?",
                                "Ask after we land. Preferably very quietly."),
            new ChatterExchange("I count one fighter, two bombers, and at least twelve dramatic backstories.",
                                "Tally the fighters. The backstories will find us."),
            new ChatterExchange("Command says the enemy ace has a personal emblem.",
                                "Great. Aim for the expensive paint."),
            new ChatterExchange("Anyone else smell cordium?",
                                "That's reactor coolant. Cordium isn't real."),
            new ChatterExchange("They promised this sortie would be cost-effective.",
                                "It is. We're spending their aircraft."),
            new ChatterExchange("I spent eight minutes aligning the nav system.",
                                "And how long remembering the master arm?"),
            new ChatterExchange("My checklist says 'fly the aircraft.' That's underlined twice."),
            new ChatterExchange("The radar says that's a tank. My missiles say it's a philosophical question.",
                                "Ask it at high explosive velocity."),
            new ChatterExchange("How did that vehicle survive the first hit?",
                                "It angled its optimism."),
            new ChatterExchange("Ground crew says every aircraft is perfectly balanced.",
                                "On which wing?"),
            new ChatterExchange("If I pull any harder, the maintenance log becomes a confession."),
            new ChatterExchange("Fox three. Because apparently sending one was too subtle."),
            new ChatterExchange("I have visual on the runway and emotional contact with the arresting gear."),
            new ChatterExchange("Who put the nuclear option on the bottom of the checklist?",
                                "The optimist."),
            new ChatterExchange("The fires are ravaging the forests.",
                                "Then let's make sure they don't reach the airfields."),
            new ChatterExchange("Something big is coming.",
                                "Radar is clean. I don't think you mean an aircraft."),
            new ChatterExchange("I can feel the buildings shaking.",
                                "We're ten kilometres out and I can feel it too."),
            new ChatterExchange("They say it's going to be a hot summer.",
                                "With our sortie rate? That's one forecast I trust."),
            new ChatterExchange("Do you ever feel like the missile knows where it is because it knows where it isn't?",
                                "Keep philosophising and it'll know exactly where you are."),
            new ChatterExchange("Good news: the warning light works.",
                                "Bad news: it has several opinions."),
            new ChatterExchange("If we make it home, I'm naming the next manoeuvre after whoever buys the drinks."),
            new ChatterExchange("This valley looked wider on the tactical map.",
                                "So did your wingspan."),
            new ChatterExchange("My flight manual calls this an edge case.",
                                "We're flying along the edge, so that checks out."),
            new ChatterExchange("Check tape on the canopy seal. Last flight whistled in G-minor.",
                                "If it hits high C, check your oxygen."),
            new ChatterExchange("Ground crew swore the radar altimeter was calibrated.",
                                "Calibrated to what? Sea level or wishful thinking?"),
            new ChatterExchange("Look at the smoke over the coastline. Someone had a loud afternoon.",
                                "Let's make sure the return flight doesn't add to it."),
            new ChatterExchange("Notice how the briefing always skips the egress plan?",
                                "Egress is discretionary. Landing gear optional."),
            new ChatterExchange("Thermal bloom on the horizon. Not a flare.",
                                "Cruise missile boost stage. Eyes open."),
            new ChatterExchange("They told us the new ECM pod was field-tested.",
                                "Yeah, in a climate-controlled hangar."),
            new ChatterExchange("Airfield tower said we have priority clearance for landing.",
                                "Priority clearance just means the crash trucks are already waiting."),
            new ChatterExchange("Ever wonder who manufactures all these missiles?",
                                "Contractors who never fly within fifty klicks of here."),
            new ChatterExchange("Watch the ridge line. Radar shadows love hiding SAM batteries.",
                                "Already got my finger on the flare pickle."),
            new ChatterExchange("How many flight hours do you have on this airframe?",
                                "Enough to know which rattle means trouble and which is just character."),
            new ChatterExchange("My trim wheel has been drifting left since sunrise.",
                                "Compensate with aggressive optimism."),
            new ChatterExchange("Fuel flow is running four percent over book values.",
                                "Book was written by accountants who don't pull eight Gs."),
            new ChatterExchange("Remember: altitude is life, airspeed is insurance.",
                                "And the ground is the deductible."),
            new ChatterExchange("Valley fog is rolling in thick below us.",
                                "Good. Makes running home easier if things get crowded."),
            new ChatterExchange("Command wants combat footage for the recruitment reel.",
                                "Tell them high-G grimacing doesn't test well with focus groups."),
            new ChatterExchange("Listening to the turbines sing. Best music on this frequency."),
            new ChatterExchange("Checklist complete, canopy locked. Ready when you are."),
            new ChatterExchange("Checking horizon reference. Clean horizon, clean conscience."),
            new ChatterExchange("RWR chirp... just ground clutter. Keep scanning."),

            // Null tags allow any eligible pilot; named speaker and reply tags independently require
            // those pilots airborne.
            new ChatterExchange("Clean picture. Let's keep it that way.",
                                speakerTag: "COBALT"),
            new ChatterExchange("If it's below the weather, it belongs to me.",
                                speakerTag: "HATCHET"),
            new ChatterExchange("The sea is calm. Radar isn't.",
                                speakerTag: "MERIDIAN"),
            new ChatterExchange("Hatchet, your definition of close support concerns me.",
                                "Nobody complained from the ground.",
                                speakerTag: "COBALT", replyTag: "HATCHET"),
            new ChatterExchange("Cobalt, permission to improve their radar picture?",
                                "Permission to remove it.",
                                speakerTag: "HATCHET", replyTag: "COBALT"),
            new ChatterExchange("Meridian, you always this calm?",
                                "No. Sometimes I'm asleep.",
                                speakerTag: "HATCHET", replyTag: "MERIDIAN"),
            new ChatterExchange("Valkyrie, you're crowding my search sector.",
                                "First to see it gets to kill it, Ghost.",
                                speakerTag: "GHOST", replyTag: "VALKYRIE"),
            new ChatterExchange("Ghost, confirm radar sweep. Thought I saw a faint spike.",
                                "Clean sweep. Must have been a sea bird or a ghost.",
                                speakerTag: "VALKYRIE", replyTag: "GHOST"),
            new ChatterExchange("Spectre, is that jammer humming on our intercom?",
                                "That's the sound of survivability. You're welcome.",
                                speakerTag: "COBALT", replyTag: "SPECTRE"),
            new ChatterExchange("Hatchet, keep your nose up. Terrain clearance looks sporty.",
                                "Rocks are just stationary targets, Cobalt.",
                                speakerTag: "COBALT", replyTag: "HATCHET"),
            new ChatterExchange("Meridian, how's the wind off the water?",
                                "Steady crosswind. Nothing a rudder can't handle.",
                                speakerTag: "VALKYRIE", replyTag: "MERIDIAN"),
            new ChatterExchange("Cobalt, lead us into something loud.",
                                "Orderly engagement only, Valkyrie. Save fireworks for egress.",
                                speakerTag: "VALKYRIE", replyTag: "COBALT"),
            new ChatterExchange("Valkyrie, check master arm. Let's make this count.",
                                speakerTag: "VALKYRIE"),
            new ChatterExchange("Spectre on the wing. Frequency hopping active.",
                                speakerTag: "SPECTRE"),
        };

        private static readonly List<ChatterExchange> customAmbient = new List<ChatterExchange>();

        public static void RegisterCustomAmbient(ChatterExchange exchange)
        {
            if (!string.IsNullOrWhiteSpace(exchange.Opening))
                customAmbient.Add(exchange);
        }

        public static void ClearCustomAmbient()
        {
            customAmbient.Clear();
        }

        public static int AmbientCount => ambient.Length + customAmbient.Count;

        public static ChatterExchange Ambient(int seed, bool repliesAllowed = true)
        {
            int total = AmbientCount;
            if (total == 0) return default;
            int start = Index(seed, total);
            if (repliesAllowed || AmbientAt(start).Reply == null) return AmbientAt(start);

            // For solo flight, select a line that needs no reply.
            for (int offset = 1; offset < total; offset++)
            {
                ChatterExchange candidate = AmbientAt(start + offset);
                if (candidate.Reply == null) return candidate;
            }

            return AmbientAt(start);
        }

        public static ChatterExchange AmbientAt(int index)
        {
            int total = AmbientCount;
            if (total == 0) return default;
            int normalized = Index(index, total);
            if (normalized < ambient.Length)
                return ambient[normalized];
            return customAmbient[normalized - ambient.Length];
        }

        public static string Identity(string name, string callsign)
        {
            string cleanName = string.IsNullOrWhiteSpace(name) ? "UNKNOWN" : name.Trim();
            string cleanCallsign = string.IsNullOrWhiteSpace(callsign)
                ? "NO CALLSIGN"
                : callsign.Trim().ToUpperInvariant();

            int split = cleanName.LastIndexOf(' ');
            if (split <= 0 || split >= cleanName.Length - 1)
                return "\"" + cleanCallsign + "\" " + cleanName.ToUpperInvariant();

            string given = cleanName.Substring(0, split).Trim();
            string surname = cleanName.Substring(split + 1).Trim().ToUpperInvariant();
            return given + " \"" + cleanCallsign + "\" " + surname;
        }

        public static string Acknowledge(ChatterPersona persona, string order, int seed)
        {
            string key = string.IsNullOrWhiteSpace(order) ? "COPY" : order.ToUpperInvariant();
            switch (key)
            {
                case "FORMATION":
                    return Pick(persona, seed,
                        new[] { "Roger. Rejoining formation.", "Copy. Forming up." },
                        new[] { "Copy. Coming back in.", "Fine. Back on your wing." },
                        new[] { "Understood. Rejoining.", "Copy. Sliding into position." },
                        new[] { "Back to formation. Copy.", "Apparently we're being tidy. Rejoining." });
                case "ENGAGE":
                    return Pick(persona, seed,
                        new[] { "Roger. Weapons free.", "Copy. Engaging." },
                        new[] { "Tally. Let's hunt.", "Copy. I'm going in." },
                        new[] { "Understood. Engaging.", "Copy. Taking the fight." },
                        new[] { "Weapons free. That should wake them up.", "Engaging. Try to keep up." });
                case "ATTACK":
                    return Pick(persona, seed,
                        new[] { "Roger. Attacking target.", "Copy. Going in on the attack." },
                        new[] { "Tally! Commencing attack run!", "Copy. Rolling in hot." },
                        new[] { "Understood. Beginning attack run.", "Copy. Moving in on target." },
                        new[] { "Attacking target. Let's see what breaks.", "Copy. Delivering bad news." });
                case "FIREFOREFFECT":
                    return Pick(persona, seed,
                        new[] { "Roger. All weapons on the mark.", "Copy. Firing for effect." },
                        new[] { "Everything we've got. Commencing barrage!", "Copy. Emptying the racks!" },
                        new[] { "Understood. Commencing full bombardment.", "Copy. Massed fire inbound." },
                        new[] { "Subtlety cancelled. Full attack commencing.", "Copy. Making the coordinates disappear." });
                case "RETURNTOBASE":
                    return Pick(persona, seed,
                        new[] { "Roger. Returning to base.", "Copy. RTB." },
                        new[] { "Copy. Heading home.", "RTB. Save me a parking spot." },
                        new[] { "Understood. Returning to base.", "Copy. Egressing for home." },
                        new[] { "RTB. The ground crew wins again.", "Copy. Taking this one home." });
                case "FALLBACK":
                    return Pick(persona, seed,
                        new[] { "Roger. Breaking off.", "Copy. Disengaging." },
                        new[] { "Breaking off. Not finished yet.", "Copy. Coming out hot." },
                        new[] { "Understood. Disengaging.", "Copy. Opening the distance." },
                        new[] { "Disengaging. Temporarily.", "Copy. Leaving them disappointed." });
                case "ORBITHERE":
                    return Pick(persona, seed,
                        new[] { "Roger. Holding here.", "Copy. Taking station." },
                        new[] { "Holding. Call me when it gets interesting.", "Copy. Circling here." },
                        new[] { "Understood. Establishing orbit.", "Copy. Holding station." },
                        new[] { "Orbiting. Round and round we go.", "Copy. I'll keep the seat warm." });
                case "DELIVERCARGO":
                    return Pick(persona, seed,
                        new[] { "Roger. Starting the delivery run.", "Copy. Cargo inbound." },
                        new[] { "Cargo run. I'll put it on the mark.", "Copy. Going in low." },
                        new[] { "Understood. Beginning delivery.", "Copy. Cargo is moving." },
                        new[] { "Delivery run. Very glamorous.", "Copy. Taking the freight in." });
                case "LANDHERE":
                    return Pick(persona, seed,
                        new[] { "Roger. Setting down.", "Copy. Landing at the mark." },
                        new[] { "Going down. Keep the field clear.", "Copy. Putting it on the deck." },
                        new[] { "Understood. Beginning descent.", "Copy. Landing now." },
                        new[] { "Landing there. Looks inviting enough.", "Copy. Wheels down." });
                case "MOVETOPOINT":
                    return Pick(persona, seed,
                        new[] { "Roger. Moving to the waypoint.", "Copy. En route." },
                        new[] { "Moving. I'll get there first.", "Copy. Pushing to the point." },
                        new[] { "Understood. En route.", "Copy. Proceeding to the waypoint." },
                        new[] { "Waypoint received. Off I go.", "Copy. Moving." });
                case "SEEKANDDESTROY":
                    return Pick(persona, seed,
                        new[] { "Roger. Proceeding to the search area.", "Copy. Seek and destroy." },
                        new[] { "Copy. I'll find something to ruin.", "Heading in. I'll take it from there." },
                        new[] { "Understood. Moving to the search area.", "Copy. I'll engage on arrival." },
                        new[] { "Search area received. Let's see what breaks first.", "Copy. Going hunting." });
                case "REFIT":
                    return Pick(persona, seed,
                        new[] { "Roger. Returning for refit.", "Copy. Heading home to rearm." },
                        new[] { "Copy. I want a full rack when I get back.", "Refit run. See you shortly." },
                        new[] { "Understood. Returning to refit.", "Copy. Replenishing and rejoining." },
                        new[] { "Copy. Time to meet the ground crew again.", "Refit it is. I'll be back." });
                case "JAMTARGET":
                    return Pick(persona, seed,
                        new[] { "Roger. Jammer coming up.", "Copy. Working their radar." },
                        new[] { "Copy. I'll blind them.", "Jammer up. Let's make them squint." },
                        new[] { "Understood. Beginning jamming.", "Copy. On the jammer." },
                        new[] { "Jamming. Electrons deployed.", "Copy. Ruining someone's picture." });
                case "MANEUVER":
                    return Pick(persona, seed,
                        new[] { "Roger. Executing.", "Copy. Manoeuvring now." },
                        new[] { "Copy. Watch this.", "On it. Hold my drink." },
                        new[] { "Understood. Beginning the manoeuvre.", "Copy. Executing." },
                        new[] { "Manoeuvre received. Showtime.", "Copy. Being theatrical." });
                case "STANDDOWN":
                    return Pick(persona, seed,
                        new[] { "Roger. Standing down. Holding near friendlies.", "Copy. Cancelling and loitering." },
                        new[] { "Copy. I'll hang out over friendly ground.", "Standing down. Call if you need me." },
                        new[] { "Understood. Task cancelled. Holding nearby.", "Copy. Loitering over friendly territory." },
                        new[] { "Standing down. I'll keep the coffee warm.", "Copy. Out of the fight, still in the air." });
                default:
                    return Pick(persona, seed,
                        new[] { "Roger.", "Copy." },
                        new[] { "Copy. Let's move.", "Roger that." },
                        new[] { "Understood.", "Copy." },
                        new[] { "Copy that.", "Apparently so." });
            }
        }

        /// <summary>Let one element lead acknowledge accepted tactical numbers in a single readable
        /// transmission.</summary>
        public static string GroupAcknowledge(ChatterPersona persona, string order,
                                              string others, int seed)
        {
            string wingmen = string.IsNullOrWhiteSpace(others) ? "everyone" : others;
            string key = string.IsNullOrWhiteSpace(order) ? "COPY" : order.ToUpperInvariant();
            switch (key)
            {
                case "FORMATION":
                    return Pick(persona, seed,
                        new[] { wingmen + ", on me. Forming up.", wingmen + ", close it in." },
                        new[] { wingmen + ", tighten it up. On me.", wingmen + ", get back in here." },
                        new[] { wingmen + ", come on in. Forming up.", wingmen + ", settle into formation." },
                        new[] { wingmen + ", back in the box. Let's go.", wingmen + ", formation. Apparently." });
                case "ENGAGE":
                    return Pick(persona, seed,
                        new[] { wingmen + ", with me. We're going in.", wingmen + ", weapons free. Engage." },
                        new[] { wingmen + ", let's hunt.", wingmen + ", come on. We're going in." },
                        new[] { wingmen + ", take your spacing. Engaging.", wingmen + ", with me. Weapons free." },
                        new[] { wingmen + ", try to keep up. Engaging.", wingmen + ", let's make this quick." });
                case "ATTACK":
                    return Pick(persona, seed,
                        new[] { wingmen + ", split the targets. Going in.", wingmen + ", attack pattern. Execute." },
                        new[] { wingmen + ", pick one and hit it.", wingmen + ", we're going straight through them." },
                        new[] { wingmen + ", sort the contacts. Engage.", wingmen + ", take your targets. Going in." },
                        new[] { wingmen + ", choose something expensive.", wingmen + ", divide and disappoint them." });
                case "FIREFOREFFECT":
                    return Pick(persona, seed,
                        new[] { wingmen + ", all weapons on the mark.", wingmen + ", in hot. Fire for effect." },
                        new[] { wingmen + ", empty the racks on it.", wingmen + ", everything we've got. Now." },
                        new[] { wingmen + ", mass fire on the target.", wingmen + ", commit all weapons." },
                        new[] { wingmen + ", subtlety is cancelled.", wingmen + ", make the target disappear." });
                case "RETURNTOBASE":
                    return Pick(persona, seed,
                        new[] { wingmen + ", we're heading home.", wingmen + ", form on me. RTB." },
                        new[] { wingmen + ", let's get these birds home.", wingmen + ", race you back." },
                        new[] { wingmen + ", turn for home. RTB.", wingmen + ", egress together." },
                        new[] { wingmen + ", home before the paperwork finds us.", wingmen + ", we're done here." });
                case "FALLBACK":
                    return Pick(persona, seed,
                        new[] { wingmen + ", break away together.", wingmen + ", disengage and open the distance." },
                        new[] { wingmen + ", out now. We'll come back.", wingmen + ", break off before they get lucky." },
                        new[] { wingmen + ", defensive spacing. Disengage.", wingmen + ", ease out and regroup." },
                        new[] { wingmen + ", let's leave them wanting more.", wingmen + ", tactical departure. Now." });
                case "ORBITHERE":
                    return Pick(persona, seed,
                        new[] { wingmen + ", take station. Hold here.", wingmen + ", establish the orbit." },
                        new[] { wingmen + ", circle up. Eyes outside.", wingmen + ", hold here and stay sharp." },
                        new[] { wingmen + ", settle into the orbit.", wingmen + ", take spacing and hold." },
                        new[] { wingmen + ", round we go. Take station.", wingmen + ", enjoy the scenery. Hold here." });
                case "DELIVERCARGO":
                    return Pick(persona, seed,
                        new[] { wingmen + ", cargo formation. Inbound.", wingmen + ", take your spacing. Begin delivery." },
                        new[] { wingmen + ", let's put it on the mark.", wingmen + ", low and fast. Cargo in." },
                        new[] { wingmen + ", steady spacing. Begin the run.", wingmen + ", cargo group inbound." },
                        new[] { wingmen + ", freight service is open.", wingmen + ", deliver first, complain later." });
                case "LANDHERE":
                    return Pick(persona, seed,
                        new[] { wingmen + ", landing pattern. Follow me down.", wingmen + ", set down at the mark." },
                        new[] { wingmen + ", clear the deck. We're landing.", wingmen + ", follow me in." },
                        new[] { wingmen + ", take interval. Beginning descent.", wingmen + ", easy on the approach." },
                        new[] { wingmen + ", gravity has the lead.", wingmen + ", landing. Try to keep it elegant." });
                case "MOVETOPOINT":
                    return Pick(persona, seed,
                        new[] { wingmen + ", waypoint received. Move out.", wingmen + ", form on me. En route." },
                        new[] { wingmen + ", push to the point.", wingmen + ", with me. Let's move." },
                        new[] { wingmen + ", take spacing. En route.", wingmen + ", proceed to the waypoint." },
                        new[] { wingmen + ", another waypoint. Come along.", wingmen + ", sightseeing formation. Move." });
                case "SEEKANDDESTROY":
                    return Pick(persona, seed,
                        new[] { wingmen + ", move to the search area. Engage on arrival.", wingmen + ", seek and destroy. Move out." },
                        new[] { wingmen + ", let's go hunting.", wingmen + ", push to the area and find something." },
                        new[] { wingmen + ", take spacing to the search area.", wingmen + ", proceed, then engage at will." },
                        new[] { wingmen + ", go find trouble.", wingmen + ", search area first. Mayhem second." });
                case "REFIT":
                    return Pick(persona, seed,
                        new[] { wingmen + ", turn for home. Refit and rejoin.", wingmen + ", back to base for refit." },
                        new[] { wingmen + ", let's get rearmed.", wingmen + ", race you to the ground crew." },
                        new[] { wingmen + ", return for refit in sequence.", wingmen + ", head home; we'll rejoin when ready." },
                        new[] { wingmen + ", paperwork and rearming. Wonderful.", wingmen + ", off to see the ground crew." });
                case "JAMTARGET":
                    return Pick(persona, seed,
                        new[] { wingmen + ", jammers up. Screen the target.", wingmen + ", work their radar." },
                        new[] { wingmen + ", light the jammers.", wingmen + ", blind them for me." },
                        new[] { wingmen + ", begin jamming. Hold station.", wingmen + ", jammers on the mark." },
                        new[] { wingmen + ", deploy the electrons.", wingmen + ", ruin their picture together." });
                case "MANEUVER":
                    return Pick(persona, seed,
                        new[] { wingmen + ", execute the manoeuvre.", wingmen + ", on my mark. Go." },
                        new[] { wingmen + ", follow me through it.", wingmen + ", try to keep up." },
                        new[] { wingmen + ", begin the manoeuvre.", wingmen + ", execute together." },
                        new[] { wingmen + ", airshow formation. Go.", wingmen + ", be theatrical." });
                case "STANDDOWN":
                    return Pick(persona, seed,
                        new[] { wingmen + ", stand down. Hold over friendlies.", wingmen + ", cancel and loiter." },
                        new[] { wingmen + ", task cancelled. Hang near friendly ground.", wingmen + ", out of the fight. Hold nearby." },
                        new[] { wingmen + ", stand down together. Loiter.", wingmen + ", cancel orders. Hold over friendly territory." },
                        new[] { wingmen + ", coffee break over friendly airspace.", wingmen + ", we're parked in the cheap seats." });
                default:
                    return Pick(persona, seed,
                        new[] { wingmen + ", copy. Move together.", wingmen + ", acknowledge. Let's move." },
                        new[] { wingmen + ", with me. Let's go.", wingmen + ", you heard it. Move." },
                        new[] { wingmen + ", copy. Moving.", wingmen + ", stay together. Executing." },
                        new[] { wingmen + ", apparently that's us. Moving.", wingmen + ", come along then." });
            }
        }

        public static string Event(ChatterPersona persona, string eventName,
                                   string detail, int seed)
        {
            string subject = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
            switch ((eventName ?? string.Empty).ToUpperInvariant())
            {
                case "ENGAGING":
                    if (subject == null) return Acknowledge(persona, "ENGAGE", seed);
                    return persona == ChatterPersona.Aggressive
                        ? Pick(seed, "Tally " + subject + ". I'm going in.",
                                     subject + " is mine.",
                                     "Closing on " + subject + ". Light 'em up!")
                        : persona == ChatterPersona.Dry
                            ? Pick(seed, "Taking " + subject + ".",
                                         "Found " + subject + ". Engaging.",
                                         "Acquired " + subject + ". Providing customer service.")
                            : persona == ChatterPersona.Calm
                                ? Pick(seed, "Visual on " + subject + ". Rolling in.",
                                             "Tracking " + subject + ". Moving to engage.",
                                             "Engaging " + subject + ". Smooth approach.")
                                : Pick(seed, "Engaging " + subject + ".",
                                             "Tally " + subject + ".",
                                             "Intercepting " + subject + ".");
                case "DEFENDING":
                    return Pick(persona, seed,
                        new[] { "Defending.", "Covering the formation." },
                        new[] { "Defensive! Breaking into the threat!", "Taking fire! Defending!" },
                        new[] { "Defending. Holding station.", "Covering the flight. All clear." },
                        new[] { "Defending. Someone wants attention.", "Defensive manoeuvres. How exciting." });
                case "BREAKCALL":
                    return Pick(persona, seed,
                        new[] { "Lead, break break! Missile tracking you!", "Missile inbound! Evade, Lead!", "Spike on your six! Break hard!" },
                        new[] { "Break hard, Lead! Missile closing fast!", "Hard break, Lead! Evade now!", "Spike on your six! Break break!" },
                        new[] { "Lead, break now. Missile tracking on your tail.", "Defensive break, Lead. Track is hot.", "Break, Lead. Countermeasures now." },
                        new[] { "Lead, break hard unless you like shrapnel.", "Missile is very fond of you, Lead. Break break.", "Break, Lead. That spike isn't friendly." });
                case "SPLASH":
                    string target = subject ?? "one";
                    return Pick(persona, seed,
                        new[] { "Splash " + target + ".", subject == null ? "Target down." : subject + " is down.", "Target destroyed. Clean hit." },
                        new[] { "Splash " + target + "! Who's next?", subject == null ? "One less problem!" : subject + " is finished!", "Scratch " + target + "! Good hit!" },
                        new[] { "Splash " + target + ".", subject == null ? "Contact down." : subject + " is down. Clear.", "Target neutralized. Clear." },
                        new[] { "Splash " + target + ". That seemed important.", subject == null ? "Target reconsidered living." : subject + " has left the fight.", "Target confirmed removed." });
                case "WINCHESTER":
                    return Pick(persona, seed,
                        new[] { "Winchester. Returning to base.", "Winchester. I'm out of the fight." },
                        new[] { "Winchester! I used every last one!", "Winchester. Heading home angry." },
                        new[] { "Winchester. Egressing for home.", "Stores empty. Returning to base." },
                        new[] { "Winchester. Strongly worded looks from here.", "Out of ammunition. Sensible exit commencing." });
                case "BINGO":
                    return Pick(persona, seed,
                        new[] { "Bingo fuel. Returning to base.", "Bingo. Turning for home." },
                        new[] { "Bingo! One more minute would've been nice!", "Fuel's gone. Heading home." },
                        new[] { "Bingo fuel. Egressing.", "At bingo. Turning for home." },
                        new[] { "Bingo. Apparently fuel is mandatory.", "Fuel gauge says we're done. RTB." });
                case "REJOINING":
                    return Pick(persona, seed,
                        new[] { "Rejoining.", "Coming back to formation." },
                        new[] { "Rejoining. Give me something else to hit.", "Back on your wing. Let's move." },
                        new[] { "Sliding into position. Rejoining.", "On your wing, Lead. Steady." },
                        new[] { "Rejoining. Did you miss me?", "Back in the box. Try not to scratch the paint." });
                case "TAXIING":
                    return Pick(persona, seed,
                        new[] { "Taxiing to the runway.", "Taxiing out." },
                        new[] { "Taxiing out. Ready to go.", "Rolling out to the runway." },
                        new[] { "Taxiing out. Holding interval.", "Moving to the runway." },
                        new[] { "Taxiing out. One queue at a time.", "Taxiing out. Runway next." });
                case "DEPARTING":
                    return Pick(persona, seed,
                        new[] { "Beginning departure.", "Starting takeoff." },
                        new[] { "Starting takeoff. Let's move.", "Beginning departure. See you up there." },
                        new[] { "Beginning departure.", "Starting takeoff. Coming up." },
                        new[] { "Starting takeoff. Finally.", "Departing. Enough sightseeing." });
                case "AIRBORNE":
                    return Pick(persona, seed,
                        new[] { "Airborne. Proceeding as ordered.", "Off the deck. Continuing on task." },
                        new[] { "Airborne! Wheels up and climbing!", "Off the ground. Let's hunt." },
                        new[] { "Airborne. Settling into climb.", "Off the deck. Proceeding steady." },
                        new[] { "Airborne. Gravity loses again.", "Off the ground. The easy part is over." });
                case "AIRBORNEREJOINING":
                    return Pick(persona, seed,
                        new[] { "Airborne. Joining your wing.", "Off the ground. Forming up." },
                        new[] { "Airborne. Coming to you.", "Off the ground. Catching up." },
                        new[] { "Airborne. Moving into formation.", "Off the ground. Joining up." },
                        new[] { "Airborne. Room for one more?", "Off the ground. Coming to join you." });
                // Use the RTB sign-off for released aircraft returning home.
                case "DETACHED": return Acknowledge(persona, "RETURNTOBASE", seed);
                case "FALLINGBACK":
                    return Pick(persona, seed,
                        new[] { "Breaking off. Falling back.", "Disengaging and opening the distance." },
                        new[] { "Disengaging! We'll come back and finish it!", "Breaking off hot! Regrouping!" },
                        new[] { "Opening distance. Falling back smoothly.", "Disengaging to standoff." },
                        new[] { "Tactical repositioning. Don't call it retreating.", "Falling back. Giving them false hope." });
                case "HOLDING":
                    return Pick(persona, seed,
                        new[] { "Holding at standoff.", "Holding position." },
                        new[] { "Holding at standoff. Call me when targets pop.", "Holding position. Ready to push." },
                        new[] { "Holding steady at standoff.", "Maintaining position. Ready." },
                        new[] { "Holding at standoff. Burning fuel quietly.", "Holding here. Practicing my patience." });
                case "COVERING":
                    return Pick(persona, seed,
                        new[] { "Covering you.", "I've got your back." },
                        new[] { "Covering your six. Anything crosses you dies.", "Got you covered. Take the shot." },
                        new[] { "You're covered, Lead. Take your time.", "Watching your tail. Clear." },
                        new[] { "Covering you. Try not to make it dramatic.", "Got your back. Don't do anything reckless." });
                case "ORBITING":
                    return Pick(persona, seed,
                        new[] { "On station. Orbit established.", "Holding in the orbit." },
                        new[] { "Orbiting. Keep scanning for bogeys.", "On station and ready to roll." },
                        new[] { "Established on station. Orbit steady.", "Holding station smoothly." },
                        new[] { "Orbiting. Scenic route engaged.", "Round and round. Holding station." });
                case "DELIVERING":
                    return Pick(persona, seed,
                        new[] { "Running the cargo in.", "Cargo inbound." },
                        new[] { "Cargo run underway. Coming in fast!", "Delivering the package. Watch the mark." },
                        new[] { "Beginning delivery approach. Steady on course.", "Cargo inbound. Descending on profile." },
                        new[] { "Special delivery inbound. Try to look grateful.", "Freight service arriving. Mind the downdraft." });
                case "DELIVERED":
                    return Pick(persona, seed,
                        new[] { "Cargo away. Delivery complete.", "Load delivered. Egressing." },
                        new[] { "Package dropped! Right on the mark!", "Delivered! Climbing out!" },
                        new[] { "Cargo released cleanly. Beginning egress.", "Drop complete. Turning for home." },
                        new[] { "Package delivered. Five-star rating expected.", "Cargo is their problem now. Egressing." });
                case "NODROPOFF":
                    return Pick(persona, seed,
                        new[] { "No drop-off available. Bringing the cargo back." },
                        new[] { "Drop zone is unworkable. Hauling cargo home." },
                        new[] { "No landing zone clear. Returning with cargo intact." },
                        new[] { "Nobody home at the drop zone. Free return shipping." });
                case "FIREFOREFFECT":
                    if (subject == null)
                    {
                        return Pick(persona, seed,
                            new[] { "In hot. Commencing full attack.", "Firing for effect." },
                            new[] { "In hot! Splashing 'em all!", "Dumping the whole rack on them!" },
                            new[] { "Commencing bombardment run.", "Coordinated strike underway." },
                            new[] { "Full barrage inbound. Erasing coordinates.", "Deploying maximum persuasion." });
                    }
                    return Pick(persona, seed,
                        new[] { "In hot on " + subject + ".", "All weapons on " + subject + "." },
                        new[] { "Emptying racks into " + subject + "!", "Wiping " + subject + " off the map!" },
                        new[] { "Concentrating fire on " + subject + ".", "Heavy ordnance inbound on " + subject + "." },
                        new[] { "Delivering bad news to " + subject + ".", "Cancelling " + subject + "'s warranty." });
                case "EXPENDED":
                    return Pick(persona, seed,
                        new[] { "Rounds complete. Off target.", "Expended. Coming off target." },
                        new[] { "Racks empty! Climbing out!", "All ordnance away! Off target!" },
                        new[] { "Stores expended cleanly. Pulling off target.", "Delivery complete. Off target." },
                        new[] { "Off target. That was expensive.", "Stores expended. Standing by for review." });
                case "OUTOFAMMO":
                    return Pick(persona, seed,
                        new[] { "Winchester. Forming back up.", "Ammo's gone. Rejoining formation." },
                        new[] { "Winchester! Coming back to the wing.", "Bone dry. Forming up on you." },
                        new[] { "Winchester. Returning to formation.", "Empty. Rejoining." },
                        new[] { "Winchester. Nothing left but harsh language. Forming up.",
                                "Out of ammunition. Tucking back in." });
                case "DOWN":
                    return Pick(persona, seed,
                        new[] { "On the deck.", "Down safely." },
                        new[] { "Wheels on the deck! Good sortie!", "Touchdown! Field is secure." },
                        new[] { "Firm touchdown. Rolling out safely.", "Down and rolling. Smooth landing." },
                        new[] { "Down in one piece. Ground crew's turn.", "Gravity won, but gracefully. Down safe." });
                case "UNABLE":
                    return Pick(persona, seed,
                        new[] { "Unable to maintain station. Disengaging for RTB." },
                        new[] { "Aircraft can't take this pace. Heading home angry." },
                        new[] { "Unable to keep parameters. Returning to base." },
                        new[] { "Airframe has filed a formal objection. RTB." });
                case "SLOWLEADER":
                    return Pick(persona, seed,
                        new[] { "Leader too slow for close formation. Holding wide until you accelerate." },
                        new[] { "Pick up the speed, Lead! Holding wide so I don't stall out!" },
                        new[] { "Holding wide, Lead. Accelerate when ready and I'll tuck in." },
                        new[] { "Lead, are we flying or parking? Holding wide." });
                case "PANIC":
                    if (subject == null)
                    {
                        return Pick(persona, seed,
                            new[] { "Missile! Defensive!", "Missile warning! Breaking!" },
                            new[] { "Missile launch! Breaking hard!", "SAM tracking! Evading!" },
                            new[] { "Missile inbound. Going defensive.", "Threat launch detected. Breaking." },
                            new[] { "RWR is screaming. Defensive.", "Warning lights are unanimous. Breaking!" });
                    }
                    return Pick(persona, seed,
                        new[] { "Missile " + subject + "! Defensive!", subject + " missile! Breaking!" },
                        new[] { subject + " launched! Breaking hard!", "Incoming from " + subject + "! Evading!" },
                        new[] { "Tracking launch from " + subject + ". Defensive.", subject + " firing. Breaking clean." },
                        new[] { subject + " has opinions. Defensive.", "Missile from " + subject + ". How rude." });
                case "DEFENSIVECLEAR":
                    return Pick(persona, seed,
                        new[] { "Threat clear. Resuming.", "Missile defeated. Back on task." },
                        new[] { "Tricked it! Threat defeated!", "Lost that one! Back on the attack!" },
                        new[] { "Threat clear. Settling back on heading.", "Missile defeated. All clear." },
                        new[] { "Missile reconsidered. Resuming.", "Another false alarm for my obituary. Back on task." });
                case "FOX1":
                    if (subject != null)
                    {
                        return Pick(persona, seed,
                            new[] { "Fox one on " + subject + ".", "Fox one, " + subject + "." },
                            new[] { "Fox one on " + subject + "! Keep him painted!", "Fox one! " + subject + " is locked!" },
                            new[] { "Fox one on " + subject + ". Guiding.", "Fox one, " + subject + ". Maintaining lock." },
                            new[] { "Fox one on " + subject + ". Illuminating.", "Fox one on " + subject + ". Stay in the beam." });
                    }
                    return Pick(persona, seed,
                        new[] { "Fox one.", "Fox one! Tracking.", "Fox one away." },
                        new[] { "Fox one! Stick with it!", "Fox one away! Keep him painted!" },
                        new[] { "Fox one. Guiding.", "Fox one away. Maintaining lock." },
                        new[] { "Fox one away. Hope you enjoy the radar beam.", "Fox one. Illuminating." });
                case "FOX2":
                    if (subject != null)
                    {
                        return Pick(persona, seed,
                            new[] { "Fox two on " + subject + ".", "Fox two, " + subject + "." },
                            new[] { "Fox two on " + subject + "! Burn 'em!", "Fox two, " + subject + "! Eat heat!" },
                            new[] { "Fox two on " + subject + ". Good track.", "Fox two, " + subject + ". Seeker locked." },
                            new[] { "Fox two on " + subject + ". Someone's about to get very warm.", "Fox two, " + subject + ". Following the exhaust." });
                    }
                    return Pick(persona, seed,
                        new[] { "Fox two.", "Fox two! Heater away.", "Fox two away." },
                        new[] { "Fox two! Eat heat!", "Fox two away! Burn 'em!" },
                        new[] { "Fox two. Heat seeker off the rail.", "Fox two away. Good track." },
                        new[] { "Fox two. Someone's about to get very warm.", "Fox two. Following the exhaust." });
                case "FOX3":
                    if (subject != null)
                    {
                        return Pick(persona, seed,
                            new[] { "Fox three on " + subject + ".", "Fox three, " + subject + "." },
                            new[] { "Fox three on " + subject + "! Pitbull!", "Fox three, " + subject + "! Fire and forget!" },
                            new[] { "Fox three on " + subject + ". Active off the rail.", "Fox three, " + subject + ". Clean release." },
                            new[] { "Fox three on " + subject + ". Tracking on its own dime.", "Fox three on " + subject + ". Good luck dodging that." });
                    }
                    return Pick(persona, seed,
                        new[] { "Fox three.", "Fox three away.", "Fox three! Pitbull active." },
                        new[] { "Fox three! Fire and forget, baby!", "Fox three away! Find him!" },
                        new[] { "Fox three. Active off the rail.", "Fox three away. Clean release." },
                        new[] { "Fox three. Tracking on its own dime.", "Fox three away. Good luck dodging that." });
                case "MAGNUM":
                    if (subject != null)
                    {
                        return Pick(persona, seed,
                            new[] { "Magnum on " + subject + ".", "Magnum, " + subject + "." },
                            new[] { "Magnum on " + subject + "! Silence that battery!", "Magnum, " + subject + "! Kill their radar!" },
                            new[] { "Magnum on " + subject + ". Riding the beam down.", "Magnum, " + subject + ". SEAD inbound." },
                            new[] { "Magnum on " + subject + ". Compliments to their radar crew.", "Magnum on " + subject + ". Time to turn their screen off." });
                    }
                    return Pick(persona, seed,
                        new[] { "Magnum.", "Magnum! Anti-radiation away.", "Magnum on the emitter." },
                        new[] { "Magnum! Silence that battery!", "Magnum away! Kill their radar!" },
                        new[] { "Magnum. Riding the beam down.", "Magnum away. SEAD inbound." },
                        new[] { "Magnum. Compliments to their radar crew.", "Magnum away. Time to turn their screen off." });
                case "RIFLE":
                    if (subject != null)
                    {
                        return Pick(persona, seed,
                            new[] { "Rifle on " + subject + ".", "Rifle, " + subject + "." },
                            new[] { "Rifle on " + subject + "! Package inbound!", "Rifle, " + subject + "! Hammering 'em!" },
                            new[] { "Rifle on " + subject + ". Air-to-ground away.", "Rifle, " + subject + ". Guiding on mark." },
                            new[] { "Rifle on " + subject + ". Structural advice incoming.", "Rifle on " + subject + ". Package delivered to their doorstep." });
                    }
                    return Pick(persona, seed,
                        new[] { "Rifle.", "Rifle away.", "Rifle! Surface missile away." },
                        new[] { "Rifle! Target is getting hammered!", "Rifle away! Package inbound!" },
                        new[] { "Rifle. Air-to-ground away.", "Rifle away. Guiding on mark." },
                        new[] { "Rifle away. Structural advice incoming.", "Rifle away. Someone down there has a problem." });
                case "JAMMING":
                    if (subject == null)
                    {
                        return Pick(persona, seed,
                            new[] { "Jammer's up.", "Buzzing their radar." },
                            new[] { "Jammer blazing! Blinding the scope!", "Lighting up the ECM!" },
                            new[] { "Jammer active. Screening the flight.", "Emitting. Radar picture screened." },
                            new[] { "Deploying electrons. Ruining their screen.", "Jammer up. Enjoy the snowstorm." });
                    }
                    return Pick(persona, seed,
                        new[] { "Jamming " + subject + ".", "Working " + subject + "'s radar." },
                        new[] { "Burning through " + subject + "'s radar!", "Blinding " + subject + " now!" },
                        new[] { "Jamming active on " + subject + ".", "Screening " + subject + "'s emitters." },
                        new[] { "Filling " + subject + "'s scope with static.", "Decorating " + subject + "'s radar screen." });
                case "JAMMINGOFF":
                    return Pick(persona, seed,
                        new[] { "Jammer's cold. Rejoining.", "Off the jammer. Back on your wing." },
                        new[] { "Jammer dark! Ready to fight!", "Ceasing ECM. Rejoining!" },
                        new[] { "Jammer silent. Rejoining formation.", "Powering down ECM. Returning to position." },
                        new[] { "Jammer cold. Back to honest flying.", "Electrons recalled. Rejoining." });
                case "MANEUVERING":
                    return Pick(persona, seed,
                        new[] { subject != null ? subject + ", executing." : "Manoeuvring." },
                        new[] { subject != null ? subject + ", watch this!" : "Watch this!" },
                        new[] { subject != null ? "Beginning " + subject + "." : "Beginning manoeuvre. Smooth." },
                        new[] { subject != null ? subject + ". Being theatrical." : "Manoeuvring. Look away if you get airsick." });
                case "MANEUVERDONE":
                    return Pick(persona, seed,
                        new[] { "Rolling out. Back on your wing.", "Manoeuvre complete. Rejoining." },
                        new[] { "Nailed it! Back in position!", "Done! Back on your six." },
                        new[] { "Rolling out smoothly. Re-establishing formation.", "Manoeuvre finished. Back in position." },
                        new[] { "Done showing off. Rejoining.", "Surviving the aerobatics. Returning to wing." });
                case "DAMAGED":
                    return persona == ChatterPersona.Aggressive
                        ? Pick(seed, "I'm hit. Still fighting!", "Took a hit! I'm not done with them!")
                        : persona == ChatterPersona.Calm
                            ? Pick(seed, "Taking fire. Controls are responding.", "Airframe damage noted. Systems holding steady.")
                            : persona == ChatterPersona.Dry
                                ? Pick(seed, "I've acquired some extra ventilation.", "Hit, but the important pieces remain.")
                                : Pick(seed, "I'm hit. Flight controls responsive.", "Taking damage. Still operational.");
                case "CRITICAL":
                    return persona == ChatterPersona.Aggressive
                        ? Pick(seed, "Heavy damage! I need a way out!", "I'm coming apart here! Clear my tail!")
                        : persona == ChatterPersona.Calm
                            ? Pick(seed, "Multiple system failures. Assessing recovery options.", "Heavy structural damage. Looking for egress vector.")
                            : persona == ChatterPersona.Dry
                                ? Pick(seed, "This aircraft is becoming theoretical.", "Critical damage. An ejection seat is looking fashionable.")
                                : Pick(seed, "Critical damage. I may not make it back.", "Aircraft critical. Requesting cover.");
                case "PILOTKILLED": return subject == null
                    ? "We've lost a pilot."
                    : Pick(seed, subject + " is down!", "We lost " + subject + "!");
                case "EJECTED": return subject == null
                    ? "Pilot punched out."
                    : Pick(seed, subject + " punched out!", subject + " ejected. Mark the position.");
                case "AIRFRAMELOST": return subject == null
                    ? "Aircraft down."
                    : Pick(seed, subject + " is going down!", "We've lost " + subject + "'s aircraft.");
                case "RECOVERED":
                    return Pick(persona, seed,
                        new[] { "Down and shut down. Airframe recovered.", "Recovered. Airframe is back in the pool." },
                        new[] { "Parked and ready for turnaround! Fuel me up!", "Recovered! Get this bird rearmed!" },
                        new[] { "Engines spooling down. Airframe recovered cleanly.", "Safe on the pad. Ready for servicing." },
                        new[] { "Shut down complete. Time to inspect the coffee machine.", "Recovered. Maintenance can start complaining." });
                case "UNABLEORDER":
                    return Pick(persona, seed,
                        new[] { "Unable.", "Negative. Unable to comply." },
                        new[] { "Negative! Can't do that right now!", "No chance. Aircraft won't let me." },
                        new[] { "Unable to comply under current conditions.", "Negative. Outside safe parameters." },
                        new[] { "I'd love to, but physics says no.", "Negative. Even my simulator couldn't pull that off." });
                default: return Acknowledge(persona, "COPY", seed);
            }
        }

        private static string Pick(ChatterPersona persona, int seed, string[] professional,
                                   string[] aggressive, string[] calm, string[] dry)
        {
            switch (persona)
            {
                case ChatterPersona.Aggressive: return Pick(aggressive, seed);
                case ChatterPersona.Calm: return Pick(calm, seed);
                case ChatterPersona.Dry: return Pick(dry, seed);
                default: return Pick(professional, seed);
            }
        }

        private static string Pick(int seed, params string[] lines) => Pick(lines, seed);

        private static string Pick(string[] lines, int seed)
        {
            if (lines == null || lines.Length == 0) return "Copy.";
            return lines[Index(seed, lines.Length)];
        }

        private static int Index(int seed, int count) =>
            seed == int.MinValue || count <= 0 ? 0 : Math.Abs(seed) % count;
    }
}
