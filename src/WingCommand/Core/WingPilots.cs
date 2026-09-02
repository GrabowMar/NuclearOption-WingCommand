using System;
using System.Collections.Generic;
using UnityEngine;

// Unity's Random is what the roster generator means; System.Random is imported by the
// using above but never intended here.
using Random = UnityEngine.Random;

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

        /// <summary>
        /// How many pilots the squadron holds at the start of a game.
        ///
        /// The old roster grew one name at a time as aircraft joined the wing, which meant a
        /// single-mission squadron was often one or two people and there was never anyone to
        /// choose between. A pregenerated squad gives the Wing tab a list to page through and
        /// the SUPPLY tab someone to put in the seat.
        /// </summary>
        public const int RosterSize = 8;

        private static readonly List<WingPilot> pool = new List<WingPilot>();
        private static readonly Dictionary<PersistentID, WingPilot> assigned =
            new Dictionary<PersistentID, WingPilot>();

        /// <summary>
        /// Every pilot the squadron has ever had, alive or lost.
        ///
        /// Separate from <see cref="pool"/>, which is only the people free to fly right now.
        /// A lost pilot leaves the pool but stays here, so the Wing tab can still show who
        /// the squadron has lost and the SUPPLY tab knows not to offer them.
        /// </summary>
        private static readonly List<WingPilot> roster = new List<WingPilot>();

        /// <summary>
        /// The pilot picked to fly the next requisition or assignment.
        ///
        /// Deliberately persistent once chosen: changing it is a decision the player made,
        /// not a bookkeeping detail that should silently revert. Defaults to the best
        /// available pilot when a fresh roster is generated. Never reassigned automatically
        /// to a lost pilot.
        /// </summary>
        private static WingPilot selectedPilot;

        /// <summary>The pilot picked to fly the next requisitioned or assigned airframe.</summary>
        public static WingPilot Selected => selectedPilot;

        /// <summary>Whether this pilot is on the squadron list at all.</summary>
        public static bool Contains(WingPilot pilot) => pilot != null && roster.Contains(pilot);

        /// <summary>Whether this pilot may be assigned to an airframe at all.</summary>
        public static bool IsSelectable(WingPilot pilot) => pilot != null && !pilot.Lost;

        /// <summary>Whether this pilot is currently in a seat.</summary>
        public static bool IsFlying(WingPilot pilot) =>
            pilot != null && assigned.ContainsValue(pilot);

        /// <summary>Whether a pilot is available to take a seat right now.</summary>
        private static bool IsFree(WingPilot pilot) =>
            IsSelectable(pilot) && !IsFlying(pilot);

        /// <summary>Choose a different pilot to fly next. A lost pilot cannot be chosen.</summary>
        public static void Select(WingPilot pilot)
        {
            if (!IsSelectable(pilot)) return;
            selectedPilot = pilot;
        }

        /// <summary>
        /// Where a new pilot comes from.
        ///
        /// Replaced wholesale by a future roster system; the default simply names a random
        /// person on the squadron list. Nothing else in the mod constructs a
        /// <see cref="WingPilot"/>.
        /// </summary>
        public static Func<int, WingPilot> Provide = DefaultProvider;

        public static void Reset()
        {
            pool.Clear();
            assigned.Clear();
            roster.Clear();
            created = 0;
            GenerateRoster();
            selectedPilot = TopSelectable();
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
        /// Put someone in the seat.
        ///
        /// The pilot the player picked on the SUPPLY tab flies first, so long as they are not
        /// already in a seat; then the most senior free pilot; then a new name. The picked
        /// pilot stays picked even after they fly — "persist until changed" is a choice,
        /// not a cache — so a busy pick simply lets the next free pilot take the slot.
        /// </summary>
        public static WingPilot Assign(Aircraft aircraft)
        {
            if (aircraft == null) return null;

            PersistentID id = aircraft.persistentID;
            if (assigned.TryGetValue(id, out WingPilot existing)) return existing;

            WingPilot pilot = TakeSelected() ?? TakeFromPool() ?? Create();
            assigned[id] = pilot;
            pool.Remove(pilot);
            return pilot;
        }

        private static WingPilot TakeSelected()
        {
            WingPilot pick = selectedPilot;
            if (!IsFree(pick)) return null;
            return pick;
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
            if (pilot != null) roster.Add(pilot);
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
            Award(aircraft, WingTuning.XpPerKill, "kill");

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
            Award(aircraft, WingTuning.XpPerSortie, "sortie");
        }

        /// <summary>Credit surviving a missile that was actually shot at this aircraft.</summary>
        public static void NoteSurvivedEngagement(Aircraft aircraft) =>
            Award(aircraft, WingTuning.XpPerEngagement, "survived engagement");

        // ------------------------------------------------------------------ rank maths

        /// <summary>
        /// Rank thresholds grow triangularly from one tunable number, so the whole curve
        /// moves together rather than needing five constants that can disagree.
        /// </summary>
        public static int XpForRank(WingRank rank)
        {
            int step = Mathf.Max(1, WingTuning.XpPerRank);
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

            float perRank = Mathf.Clamp01(WingTuning.RankEffect) * 0.06f;
            return (int)pilot.Rank * perRank;
        }

        /// <summary>Weapon envelope multiplier for this pilot. Always at least 1.</summary>
        public static float EnvelopeScale(Aircraft aircraft) => 1f + SkillBonus(aircraft) * 0.5f;

        /// <summary>Shot-cycle multiplier for this pilot. Always at most 1.</summary>
        public static float ReactionScale(Aircraft aircraft) => 1f - SkillBonus(aircraft) * 0.5f;

        // ---------------------------------------------------------- roster generation

        /// <summary>
        /// The squadron as the panels show it: pilots still flying first, most senior on top,
        /// then the lost, so a dead wingman never sits above a living one.
        ///
        /// Built fresh because it is a handful of elements and callers want it re-sorted as
        /// XP changes; the Wing and SUPPLY tabs both read it on their own refresh.
        /// </summary>
        public static List<WingPilot> DisplayRoster()
        {
            var alive = new List<WingPilot>();
            var dead = new List<WingPilot>();
            for (int i = 0; i < roster.Count; i++)
            {
                WingPilot pilot = roster[i];
                if (pilot.Lost) dead.Add(pilot);
                else alive.Add(pilot);
            }

            alive.Sort(CompareByXp);
            alive.AddRange(dead);
            return alive;
        }

        /// <summary>Only the pilots who can be put in a seat, most senior first.</summary>
        public static List<WingPilot> SelectablePilots()
        {
            var list = new List<WingPilot>();
            for (int i = 0; i < roster.Count; i++)
            {
                if (!roster[i].Lost) list.Add(roster[i]);
            }
            list.Sort(CompareByXp);
            return list;
        }

        private static int CompareByXp(WingPilot a, WingPilot b)
        {
            int byXp = b.Xp.CompareTo(a.Xp);
            return byXp != 0 ? byXp : roster.IndexOf(a).CompareTo(roster.IndexOf(b));
        }

        /// <summary>The best available pilot, or null when everyone is lost.</summary>
        private static WingPilot TopSelectable()
        {
            List<WingPilot> alive = SelectablePilots();
            return alive.Count > 0 ? alive[0] : null;
        }

        /// <summary>
        /// Fill the squadron with a fresh set of pilots, each with a distinct callsign and
        /// a spread of experience so a new game starts with some variety of rank.
        /// </summary>
        private static void GenerateRoster()
        {
            string[] sur = Shuffled(Surnames);
            string[] call = Shuffled(Callsigns);
            string[] post = Shuffled(Postings);

            for (int i = 0; i < RosterSize; i++)
            {
                WingPilot pilot = new WingPilot
                {
                    Name = sur[i % sur.Length],
                    Callsign = call[i % call.Length],
                    DialogueTag = call[i % call.Length],
                    Persona = (ChatterPersona)(Random.Range(0, 4)),
                    Background = post[i % post.Length],
                    Xp = StartingXp(i),
                };
                roster.Add(pilot);
                pool.Add(pilot);
            }

            created = RosterSize;
        }

        /// <summary>Yield one settled ace and a spread of newer pilots beneath them.</summary>
        private static int StartingXp(int index)
        {
            if (index == 0) return Random.Range(XpForRank(WingRank.Ace), XpForRank(WingRank.Ace) + 40);
            if (index < 3) return Random.Range(XpForRank(WingRank.Wingman), XpForRank(WingRank.Veteran));
            return Random.Range(0, XpForRank(WingRank.Wingman));
        }

        /// <summary>A random permutation of an array, so the roster never repeats a call.</summary>
        private static string[] Shuffled(string[] source)
        {
            string[] copy = (string[])source.Clone();
            for (int i = copy.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }

        // ---------------------------------------------------------------- default names

        /// <summary>
        /// A random person off the squadron list.
        ///
        /// Used when a roster-capacity spill needs one more name, and as the default
        /// <see cref="Provide"/> implementation. Kept name-random rather than fixed so a
        /// pilot demanded beyond the pregenerated eight does not break the variety of the
        /// initial roster by re-using a callsign that is already in use.
        /// </summary>
        private static WingPilot DefaultProvider(int index)
        {
            _ = index;
            return new WingPilot
            {
                Name = Surnames[Random.Range(0, Surnames.Length)],
                Callsign = Callsigns[Random.Range(0, Callsigns.Length)],
                DialogueTag = Callsigns[Random.Range(0, Callsigns.Length)],
                Persona = (ChatterPersona)(Random.Range(0, 4)),
                Background = Postings[Random.Range(0, Postings.Length)],
                Xp = Random.Range(0, XpForRank(WingRank.Wingman)),
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
            "Former test pilot reassigned after an unauthorized canyon run. Exceptional high-G tolerance and instinctive missile timing.",
            "Veteran of the Northern Basin offensive with 300 combat sorties. Renowned for calm comms under heavy AA fire and lethal radar discipline.",
            "Ex-Navy strike lead with 14 carrier traps in night squalls. Masters CCIP unguided bomb drops and low-level terrain masking.",
            "Weapons school instructor who requested combat reactivation. Encyclopedic knowledge of IR missile tracking and countermeasures.",
            "Tactical reconnaissance specialist turned fighter pilot. Spotter instinct; sniffs out ambush flights before early-warning radar triggers.",
            "Heavy transport veteran who converted to attack craft. Exceptional fuel management and flies damaged airframes home safely.",
            "Fiercely protective wingman with two commendations for escorting stricken flights out of hostile airspace.",
            "Aggressive interceptor doctrine. Prefers high-altitude diving strikes and gun-solution closes over BVR jousts.",
            "Frontline reserve call-up with steady hands. Flown CAS sorties through heavy artillery barrages without flinching.",
            "Former aerobatic display pilot. Superior spatial awareness and energy retention during close-quarters dogfights.",
            "Electronic warfare tech turned combat pilot. Knows exactly how to exploit enemy radar dead zones and burn-through windows.",
            "Coastal patrol veteran who survived a ditching in freezing surf. Relentless hunter of enemy naval and amphibious assets.",
            "Quiet marksman with record gun-camera confirmation rates. Never wastes a burst when deflection angles are tight.",
            "Survivor of the Airfield 4 siege. Specializes in short-field scramble takeoffs under mortar fire.",
            "Night-interception specialist with hundreds of flight hours in zero-visibility storm ceilings.",
            "Seasoned squadron instructor. Anticipates bandit vectors three moves ahead and keeps the flight in tight discipline."
        };
    }
}
