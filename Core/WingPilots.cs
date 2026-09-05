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
        private static readonly HashSet<WingPilot> reserved = new HashSet<WingPilot>();
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
        /// Defaults to the best available pilot when a fresh roster is generated. Automatically
        /// moves to the next available pilot when one is chosen for an airframe requisition or
        /// assignment. Never reassigned automatically to a lost pilot.
        /// </summary>
        private static WingPilot selectedPilot;

        /// <summary>The pilot picked to fly the next requisitioned or assigned airframe.</summary>
        public static WingPilot Selected => selectedPilot;

        /// <summary>Whether this pilot is on the squadron list at all.</summary>
        public static bool Contains(WingPilot pilot) => pilot != null && roster.Contains(pilot);

        /// <summary>Whether a pilot with this callsign is already on the squadron list.</summary>
        public static bool ContainsCallsign(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign)) return false;
            for (int i = 0; i < roster.Count; i++)
            {
                if (string.Equals(roster[i]?.Callsign, callsign, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Finds a pilot in the roster by callsign, or null if not found.</summary>
        public static WingPilot FindByCallsign(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign)) return null;
            for (int i = 0; i < roster.Count; i++)
            {
                if (string.Equals(roster[i]?.Callsign, callsign, StringComparison.OrdinalIgnoreCase))
                    return roster[i];
            }
            return null;
        }

        public static int RosterCount => roster.Count;

        /// <summary>Whether this pilot may be assigned to an airframe at all.</summary>
        public static bool IsSelectable(WingPilot pilot) => pilot != null && !pilot.Lost;

        /// <summary>Whether this pilot is currently in a seat.</summary>
        public static bool IsFlying(WingPilot pilot) =>
            pilot != null && assigned.ContainsValue(pilot);

        /// <summary>Whether this pilot is currently earmarked for a pending delivery.</summary>
        public static bool IsReserved(WingPilot pilot) =>
            pilot != null && reserved.Contains(pilot);

        /// <summary>Whether a pilot is available to take a seat right now.</summary>
        public static bool IsFree(WingPilot pilot) =>
            IsSelectable(pilot) && !IsFlying(pilot) && !IsReserved(pilot);

        /// <summary>Choose a different pilot to fly next. A lost pilot cannot be chosen.</summary>
        public static void Select(WingPilot pilot)
        {
            if (!IsSelectable(pilot)) return;
            selectedPilot = pilot;
        }

        /// <summary>
        /// Automatically move the selected pilot to the next free pilot, wrapping around.
        /// If no pilot is free, advances to the next selectable pilot in sequence.
        /// </summary>
        public static void AdvanceSelected(WingPilot from = null)
        {
            List<WingPilot> selectable = SelectablePilots();
            if (selectable.Count == 0)
            {
                selectedPilot = null;
                return;
            }

            WingPilot reference = from ?? selectedPilot;
            int startIndex = reference != null ? selectable.IndexOf(reference) : -1;
            int nextIndex = PilotSelectionPolicy.NextIndex(startIndex, selectable.Count, i => IsFree(selectable[i]));
            selectedPilot = nextIndex >= 0 && nextIndex < selectable.Count ? selectable[nextIndex] : null;
        }

        /// <summary>
        /// Reserve the currently selected pilot (or next available free pilot) for a pending
        /// requisition, and automatically advance Selected to the next pilot.
        /// </summary>
        public static WingPilot ReserveForRequisition()
        {
            WingPilot pick = selectedPilot;
            if (pick == null || !IsFree(pick))
            {
                pick = NextFreePilot(selectedPilot);
            }
            if (pick == null)
            {
                pick = Create();
            }

            if (pick != null)
            {
                reserved.Add(pick);
                AdvanceSelected(pick);
            }
            return pick;
        }

        private static WingPilot NextFreePilot(WingPilot from)
        {
            List<WingPilot> selectable = SelectablePilots();
            if (selectable.Count == 0) return null;

            int startIndex = from != null ? selectable.IndexOf(from) : -1;
            if (startIndex < 0) startIndex = 0;

            for (int i = 1; i <= selectable.Count; i++)
            {
                int index = (startIndex + i) % selectable.Count;
                WingPilot candidate = selectable[index];
                if (IsFree(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Release a reservation if a purchase failed or rolled back.</summary>
        public static void ReleaseReservation(WingPilot pilot, bool restoreSelection = false)
        {
            if (pilot == null) return;
            reserved.Remove(pilot);
            if (!IsFlying(pilot) && !pilot.Lost && !pool.Contains(pilot))
            {
                pool.Add(pilot);
            }
            if (restoreSelection && !IsFlying(pilot) && !pilot.Lost)
            {
                selectedPilot = pilot;
            }
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
            reserved.Clear();
            assigned.Clear();
            roster.Clear();
            created = 0;
            // Pregenerated pilots at mission start removed: player recruits manually or auto-recruits on purchase.
            selectedPilot = null;
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
        /// If a preferred pilot was reserved (e.g. by a requisition transaction), that pilot
        /// takes the seat; otherwise the player's pick on the SUPPLY tab flies first, followed
        /// by the most senior free pilot, then a new name. Automatically advances the selected
        /// pilot when the current selection is placed in a seat.
        /// </summary>
        public static WingPilot Assign(Aircraft aircraft, WingPilot preferred = null)
        {
            if (aircraft == null) return null;

            PersistentID id = aircraft.persistentID;
            if (assigned.TryGetValue(id, out WingPilot existing)) return existing;

            WingPilot pilot = (preferred != null && !preferred.Lost && !IsFlying(preferred))
                ? preferred
                : TakeSelected() ?? TakeFromPool() ?? Create();

            reserved.Remove(pilot);
            assigned[id] = pilot;
            pool.Remove(pilot);

            if (selectedPilot == pilot || !IsFree(selectedPilot))
            {
                AdvanceSelected(pilot);
            }

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
            if (selectedPilot == pilot)
            {
                AdvanceSelected(pilot);
            }
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
                if (pilot == null || pilot.Lost || !IsFree(pilot)) continue;

                pool.RemoveAt(i);
                return pilot;
            }
            return null;
        }

        /// <summary>
        /// Recruits a pilot manually to the squadron on the ground.
        /// </summary>
        public static WingPilot RecruitManual()
        {
            WingPilot pilot = Create();
            if (pilot != null)
            {
                if (!pool.Contains(pilot)) pool.Add(pilot);
                if (selectedPilot == null) selectedPilot = pilot;
            }
            return pilot;
        }

        /// <summary>
        /// Import a custom pilot record directly into the squadron roster.
        /// </summary>
        public static WingPilot ImportCustom(CustomPilotRecord record)
        {
            if (record == null) return null;
            if (ContainsCallsign(record.Callsign)) return null;

            var pilot = new WingPilot
            {
                Name = record.Name,
                Callsign = record.Callsign,
                DialogueTag = record.ResolvedDialogueTag,
                Persona = record.Persona,
                Background = record.Background,
                Xp = record.Xp,
                Kills = record.Kills,
                Sorties = record.Sorties,
            };

            roster.Add(pilot);
            pool.Add(pilot);
            if (selectedPilot == null) selectedPilot = pilot;
            created++;
            return pilot;
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
            if (pilot != null)
            {
                roster.Add(pilot);
                created++;
            }
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

            float effect = Plugin.Settings != null ? Plugin.Settings.RankEffect.Value : WingTuning.RankEffect;
            float perRank = Mathf.Clamp(effect, 0f, 2f) * 0.06f;
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
