using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>How experienced a wing pilot is. Derived from XP; never set directly.</summary>
    internal enum WingRank
    {
        Rookie,
        Wingman,
        Veteran,
        Ace,
        Legend,
    }

    /// <summary>
    /// One person in the player's squadron.
    ///
    /// The record is deliberately thin and deliberately not generated where it is used. A
    /// later "assign a pilot from a pregenerated pool" feature — portraits, skills, a
    /// selection screen — should be able to hand a fully-formed record to
    /// <see cref="WingPilotRoster.Provide"/> and have every other part of the mod work
    /// unchanged. Nothing outside the roster invents a callsign.
    /// </summary>
    internal sealed class WingPilot
    {
        public string Name;
        public string Callsign;

        /// <summary>
        /// Radio manner only. Kept on the person so future plot and relationship systems can
        /// evolve their dialogue without coupling it to an aircraft, rank, or combat tuning.
        /// </summary>
        public ChatterPersona Persona;

        /// <summary>
        /// Stable selector for dialogue written for this person. Custom pilot providers may
        /// set it independently of the displayed callsign; a missing tag falls back to the
        /// callsign, so existing providers need no changes.
        /// </summary>
        public string DialogueTag;

        /// <summary>One line of flavour. Shown on the Wing tab and nowhere else.</summary>
        public string Background;

        public int Xp;
        public int Kills;
        public int Sorties;

        /// <summary>True once the pilot has been killed; a lost pilot never flies again.</summary>
        public bool Lost;

        public WingRank Rank => WingPilotRoster.RankFor(Xp);

        /// <summary>XP still needed for the next rank, or zero at the top.</summary>
        public int ToNextRank => WingPilotRoster.ToNextRank(Xp);
    }

    /// <summary>
    /// The squadron's people, and the experience they accumulate.
    ///
    /// Pilots belong to the squadron rather than to an airframe: a wingman that recovers at
    /// base releases its pilot back to the pool with their record intact, and the next
    /// requisition is flown by the same people. That is the whole point of tracking XP at
    /// all — an airframe-bound record would reset every time a wingman went home, which is
    /// the one event the player is being encouraged to prefer.
    ///
    /// Rank has a real but small effect, applied in <see cref="WingWeapons"/>: an
    /// experienced pilot gets slightly more out of the same weapon and cycles a shot
    /// slightly faster. It is a backbone for later tuning, not a rebalance, and
    /// <c>Pilot/RankEffect</c> turns it off entirely.
    /// </summary>
    internal static class WingPilotRoster
    {
        /// <summary>Highest rank a pilot can reach.</summary>
        public static readonly WingRank TopRank = WingRank.Legend;

        private static readonly List<WingPilot> pool = new List<WingPilot>();
        private static readonly Dictionary<PersistentID, WingPilot> assigned =
            new Dictionary<PersistentID, WingPilot>();

        /// <summary>
        /// Where a new pilot comes from.
        ///
        /// Replaced wholesale by a future roster system; the default simply names the next
        /// person off the squadron list. Nothing else in the mod constructs a
        /// <see cref="WingPilot"/>.
        /// </summary>
        public static Func<int, WingPilot> Provide = DefaultProvider;

        public static void Reset()
        {
            pool.Clear();
            assigned.Clear();
            created = 0;
        }

        private static int created;

        // ------------------------------------------------------------------ assignment

        /// <summary>The pilot flying this aircraft, or null.</summary>
        public static WingPilot Of(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            return assigned.TryGetValue(aircraft.persistentID, out WingPilot pilot) ? pilot : null;
        }

        public static WingPilot Of(WingMember member) =>
            member != null ? Of(member.Aircraft) : null;

        /// <summary>
        /// Put someone in the seat, reusing a pilot who is between airframes before
        /// bringing a new name onto the squadron list.
        /// </summary>
        public static WingPilot Assign(Aircraft aircraft)
        {
            if (aircraft == null) return null;

            PersistentID id = aircraft.persistentID;
            if (assigned.TryGetValue(id, out WingPilot existing)) return existing;

            WingPilot pilot = TakeFromPool() ?? Create();
            assigned[id] = pilot;
            return pilot;
        }

        /// <summary>
        /// Take a pilot out of the seat when the aircraft leaves the wing.
        ///
        /// A pilot who flew home, or whose aircraft was simply released, goes back on the
        /// list with their record. A pilot who was killed does not: an experience system
        /// where losses cost nothing is a scoreboard, not a squadron.
        /// </summary>
        public static void Retire(WingMember member, bool survived)
        {
            if (member == null || member.Aircraft == null) return;

            PersistentID id = member.Aircraft.persistentID;
            if (!assigned.TryGetValue(id, out WingPilot pilot)) return;
            assigned.Remove(id);

            if (survived)
            {
                pool.Add(pilot);
                return;
            }

            pilot.Lost = true;
            WingCommandManager.Instance?.Toast(
                pilot.Callsign + " (" + pilot.Name + ") was lost - " + RankName(pilot.Rank) +
                ", " + pilot.Kills + " kill(s)");
            Plugin.Logger.LogInfo(
                "[Pilot] " + pilot.Callsign + " lost after " + pilot.Sorties + " sortie(s), " +
                pilot.Xp + " XP");
        }

        private static WingPilot TakeFromPool()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                WingPilot pilot = pool[i];
                if (pilot == null || pilot.Lost) continue;

                pool.RemoveAt(i);
                return pilot;
            }
            return null;
        }

        private static WingPilot Create()
        {
            WingPilot pilot = null;
            try
            {
                pilot = Provide != null ? Provide(created) : null;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Pilot] pilot provider failed: " + e.Message);
            }

            pilot = pilot ?? DefaultProvider(created);
            created++;
            return pilot;
        }

        // -------------------------------------------------------------------- progress

        /// <summary>Credit a pilot, if the aircraft has one and the system is enabled.</summary>
        public static void Award(Aircraft aircraft, int xp, string reason)
        {
            if (xp <= 0 || !Plugin.Settings.PilotProgression.Value) return;

            WingPilot pilot = Of(aircraft);
            if (pilot == null) return;

            WingRank before = pilot.Rank;
            pilot.Xp += xp;

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(
                    "[Pilot] " + pilot.Callsign + " +" + xp + " XP (" + reason + ")");

            if (pilot.Rank == before) return;

            WingCommandManager.Instance?.Toast(
                pilot.Callsign + " promoted to " + RankName(pilot.Rank));
        }

        public static void NoteKill(Aircraft aircraft, Unit victim)
        {
            WingPilot pilot = Of(aircraft);
            if (pilot == null) return;

            pilot.Kills++;
            Award(aircraft, Plugin.Settings.XpPerKill.Value, "kill");

            if (victim != null)
                WingComms.Say(WingCommandManager.Instance?.Wing?.Find(aircraft),
                              WingComms.Call.Splash, victim.unitName);
        }

        /// <summary>Credit a completed sortie when a wingman recovers at base.</summary>
        public static void NoteSortie(Aircraft aircraft)
        {
            WingPilot pilot = Of(aircraft);
            if (pilot == null) return;

            pilot.Sorties++;
            Award(aircraft, Plugin.Settings.XpPerSortie.Value, "sortie");
        }

        /// <summary>Credit surviving a missile that was actually shot at this aircraft.</summary>
        public static void NoteSurvivedEngagement(Aircraft aircraft) =>
            Award(aircraft, Plugin.Settings.XpPerEngagement.Value, "survived engagement");

        // ------------------------------------------------------------------ rank maths

        /// <summary>
        /// Rank thresholds grow triangularly from one tunable number, so the whole curve
        /// moves together rather than needing five constants that can disagree.
        /// </summary>
        public static int XpForRank(WingRank rank)
        {
            int step = Mathf.Max(1, Plugin.Settings.XpPerRank.Value);
            int r = (int)rank;
            return step * r * (r + 1) / 2;
        }

        public static WingRank RankFor(int xp)
        {
            WingRank best = WingRank.Rookie;
            for (WingRank rank = WingRank.Rookie; rank <= TopRank; rank++)
            {
                if (xp >= XpForRank(rank)) best = rank;
            }
            return best;
        }

        public static int ToNextRank(int xp)
        {
            WingRank rank = RankFor(xp);
            if (rank >= TopRank) return 0;
            return Mathf.Max(0, XpForRank(rank + 1) - xp);
        }

        public static string RankName(WingRank rank)
        {
            switch (rank)
            {
                case WingRank.Wingman: return "WINGMAN";
                case WingRank.Veteran: return "VETERAN";
                case WingRank.Ace:     return "ACE";
                case WingRank.Legend:  return "LEGEND";
                default:               return "ROOKIE";
            }
        }

        /// <summary>
        /// How much better than a rookie this pilot is, 0 at Rookie and rising with rank.
        /// Scaled by <c>Pilot/RankEffect</c>, which is what makes the whole mechanical
        /// effect switchable off without removing the record.
        /// </summary>
        public static float SkillBonus(Aircraft aircraft)
        {
            if (!Plugin.Settings.PilotProgression.Value) return 0f;

            WingPilot pilot = Of(aircraft);
            if (pilot == null) return 0f;

            float perRank = Mathf.Clamp01(Plugin.Settings.RankEffect.Value) * 0.06f;
            return (int)pilot.Rank * perRank;
        }

        /// <summary>Weapon envelope multiplier for this pilot. Always at least 1.</summary>
        public static float EnvelopeScale(Aircraft aircraft) => 1f + SkillBonus(aircraft) * 0.5f;

        /// <summary>Shot-cycle multiplier for this pilot. Always at most 1.</summary>
        public static float ReactionScale(Aircraft aircraft) => 1f - SkillBonus(aircraft) * 0.5f;

        // ---------------------------------------------------------------- default names

        /// <summary>
        /// Three named pilots, then generated ones.
        ///
        /// Three because a wing holds three, so an ordinary mission is flown entirely by
        /// people with a written background. Beyond that the generator pairs a surname with
        /// a callsign and a one-line posting, which is enough for the panel to have
        /// something to say and little enough that a real roster system replacing it loses
        /// nothing.
        /// </summary>
        private static WingPilot DefaultProvider(int index)
        {
            switch (index)
            {
                case 0:
                    return new WingPilot
                    {
                        Name = "M. Adeyemi",
                        Callsign = "COBALT",
                        DialogueTag = "COBALT",
                        Persona = ChatterPersona.Professional,
                        Background = "Ex-interceptor squadron. Flies the merge cold and patient.",
                    };
                case 1:
                    return new WingPilot
                    {
                        Name = "R. Vasquez",
                        Callsign = "HATCHET",
                        DialogueTag = "HATCHET",
                        Persona = ChatterPersona.Aggressive,
                        Background = "Came up on close support. Prefers to be under the weather.",
                    };
                case 2:
                    return new WingPilot
                    {
                        Name = "K. Lindqvist",
                        Callsign = "MERIDIAN",
                        DialogueTag = "MERIDIAN",
                        Persona = ChatterPersona.Calm,
                        Background = "Transferred from maritime patrol. Reads a radar picture early.",
                    };
            }

            int n = index - 3;
            return new WingPilot
            {
                Name = Surnames[n % Surnames.Length],
                Callsign = Callsigns[n % Callsigns.Length],
                DialogueTag = Callsigns[n % Callsigns.Length],
                Persona = (ChatterPersona)(n % 4),
                Background = Postings[n % Postings.Length],
            };
        }

        private static readonly string[] Surnames =
        {
            "T. Brennan", "S. Okonkwo", "J. Haldor", "A. Petrov", "L. Mancini",
            "D. Rask", "N. Oyelaran", "P. Steiner",
        };

        private static readonly string[] Callsigns =
        {
            "TALLY", "GRAVEL", "ANVIL", "SABLE", "PICKET", "RIPTIDE", "LANTERN", "DRAYMAN",
        };

        private static readonly string[] Postings =
        {
            "Reserve call-up. Low hours, steady hands.",
            "Two tours on the northern line. Talks little.",
            "Instructor pilot, back on the roster by request.",
            "Came across from transports. Lands anything.",
            "Weapons school graduate. Impatient.",
            "Ferry pilot turned shooter. Knows every field.",
            "Grounded once for low flying. Unrepentant.",
            "Quiet, methodical, never wastes a missile.",
        };
    }
}
