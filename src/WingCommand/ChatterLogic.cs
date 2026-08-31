using System;

namespace WingCommand
{
    /// <summary>
    /// A pilot's radio manner. This is deliberately separate from rank and combat skill:
    /// future story work can change what a pilot says without changing how the aircraft flies.
    /// </summary>
    internal enum ChatterPersona
    {
        Professional,
        Aggressive,
        Calm,
        Dry,
    }

    /// <summary>A rare bit of ambient flight banter, optionally answered by another pilot.</summary>
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

        public bool SpeakerMatches(string tag) => Matches(SpeakerTag, tag);
        public bool ReplyMatches(string tag) => Matches(ReplyTag, tag);

        private static bool Matches(string required, string actual) =>
            required == null || string.Equals(required, actual,
                                               StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Presentation and line-selection logic with no Unity dependency, so it can be tested
    /// without loading the game. Plot context can later be added beside persona and event.
    /// </summary>
    internal static class ChatterDialogue
    {
        // Static data keeps the ambient path allocation-free. The references are phrased as
        // things pilots in this world might actually say, rather than breaking character to
        // name another game.
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
            new ChatterExchange("The fires are ravenging the forests",
                                "Then let's make sure they don't reach the airfields."),
            new ChatterExchange("Something big is coming",
                                "Radar is clean. I don't think you mean an aircraft."),
            new ChatterExchange("I can feel the buildings shaking",
                                "We're ten kilometres out and I can feel it too."),
            new ChatterExchange("They say it will be hot summer",
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

            // Pilot-specific seam. A null tag means "any pilot"; a named tag makes the
            // exchange eligible only while that person is actually airborne. ReplyTag can
            // independently require a particular second pilot.
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
        };

        public static int AmbientCount => ambient.Length;

        public static ChatterExchange Ambient(int seed, bool repliesAllowed = true)
        {
            int start = Index(seed, ambient.Length);
            if (repliesAllowed || AmbientAt(start).Reply == null) return AmbientAt(start);

            // A lone wingman gets a line that does not hang as an unanswered question.
            for (int offset = 1; offset < ambient.Length; offset++)
            {
                ChatterExchange candidate = AmbientAt(start + offset);
                if (candidate.Reply == null) return candidate;
            }

            return AmbientAt(start);
        }

        public static ChatterExchange AmbientAt(int index) =>
            ambient[Index(index, ambient.Length)];

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
                default:
                    return Pick(persona, seed,
                        new[] { "Roger.", "Copy." },
                        new[] { "Copy. Let's move.", "Roger that." },
                        new[] { "Understood.", "Copy." },
                        new[] { "Copy that.", "Apparently so." });
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
                                     subject + " is mine.")
                        : persona == ChatterPersona.Dry
                            ? Pick(seed, "Taking " + subject + ".", "Found " + subject + ". Engaging.")
                            : Pick(seed, "Engaging " + subject + ".", "Tally " + subject + ".");
                case "DEFENDING": return Pick(seed, "Defending.", "Covering the formation.");
                case "SPLASH": return subject == null
                    ? Pick(seed, "Splash one.", "Target down.")
                    : Pick(seed, "Splash " + subject + ".", subject + " is down.");
                case "WINCHESTER": return Pick(seed, "Winchester. Returning to base.",
                                                      "Winchester. I'm out of the fight.");
                case "BINGO": return Pick(seed, "Bingo fuel. Returning to base.",
                                                 "Bingo. Turning for home.");
                case "REJOINING": return Pick(seed, "Rejoining.", "Coming back to formation.");
                // A released wingman signs off exactly as one ordered home does: it is
                // the same thing happening to it, arrived at from the other direction.
                case "DETACHED": return Acknowledge(persona, "RETURNTOBASE", seed);
                case "BREAKING": return subject == null
                    ? Pick(seed, "Breaking!", "Breaking off!")
                    : Pick(seed, "Breaking " + subject + "!", "Defensive, " + subject + "!");
                case "FALLINGBACK": return Pick(seed, "Breaking off. Falling back.",
                                                       "Disengaging and opening the distance.");
                case "HOLDING": return Pick(seed, "Holding at standoff.", "Holding position.");
                case "COVERING": return Pick(seed, "Covering you.", "I've got your back.");
                case "ORBITING": return Pick(seed, "On station. Orbit established.",
                                                    "Holding in the orbit.");
                case "DELIVERING": return Pick(seed, "Running the cargo in.", "Cargo inbound.");
                case "DELIVERED": return Pick(seed, "Cargo away. Delivery complete.",
                                                     "Load delivered. Egressing.");
                case "NODROPOFF": return "No drop-off available. Bringing the cargo back.";
                case "FIREFOREFFECT": return subject == null
                    ? Pick(seed, "In hot. Splashing 'em.", "Commencing full attack.")
                    : Pick(seed, "In hot on " + subject + ". Splashing 'em.",
                                 "All weapons on " + subject + ".");
                case "EXPENDED": return Pick(seed, "Rounds complete. Off target.",
                                                    "Expended. Coming off target.");
                case "DOWN": return Pick(seed, "On the deck.", "Down safely.");
                case "UNABLE": return "Unable to keep up. Returning to base.";
                case "PANIC": return subject == null
                    ? Pick(seed, "Missile! Defensive!", "Missile warning! Breaking!")
                    : Pick(seed, "Missile " + subject + "! Defensive!",
                                 subject + " missile! Breaking!");
                case "DEFENSIVECLEAR": return Pick(seed, "Threat clear. Resuming.",
                                                          "Missile defeated. Back on task.");
                case "RECOVERED": return Pick(seed, "Down and shut down. Airframe recovered.",
                                                     "Recovered. Airframe is back in the pool.");
                case "UNABLEORDER": return Pick(seed, "Unable.", "Negative. Unable to comply.");
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
