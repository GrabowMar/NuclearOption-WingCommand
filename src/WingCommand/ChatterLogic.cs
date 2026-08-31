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

    /// <summary>
    /// Presentation and line-selection logic with no Unity dependency, so it can be tested
    /// without loading the game. Plot context can later be added beside persona and event.
    /// </summary>
    internal static class ChatterDialogue
    {
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
            int index = seed == int.MinValue ? 0 : Math.Abs(seed) % lines.Length;
            return lines[index];
        }
    }
}
