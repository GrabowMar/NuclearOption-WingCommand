using System;
using System.Collections.Generic;
using UnityEngine;

// Use Unity's random generator for roster creation.
using Random = UnityEngine.Random;

namespace WingCommand
{
 /// <summary>Pilot experience rank, derived from XP.</summary>
    internal enum WingRank
    {
        Rookie,
        Wingman,
        Veteran,
        Ace,
        Legend,
    }

 /// <summary>Squadron pilot record. The roster owns callsign generation; external providers can supply
 /// complete records through Provide.</summary>
    internal sealed class WingPilot
    {
        public string Name;
        public string Callsign;

     /// <summary>Person-specific radio style, independent of airframe and combat tuning.</summary>
        public ChatterPersona Persona;

     /// <summary>Stable dialogue lookup tag, independent of display callsign. Missing tags fall back to
     /// callsign for provider compatibility.</summary>
        public string DialogueTag;

     /// <summary>Flavour text shown on the Wing tab.</summary>
        public string Background;

        public int Xp;
        public int Kills;
        public int Sorties;

     /// <summary>Marks a deceased pilot as permanently unavailable.</summary>
        public bool Lost;

        public WingRank Rank => WingPilotRoster.RankFor(Xp);
    }

 /// <summary>Persistent squadron pilots and XP. Recovery returns pilots to the pool without losing
 /// records. Rank slightly improves shot cadence and weapon envelope; Pilot/RankEffect disables those
 /// effects.</summary>
    internal static class WingPilotRoster
    {
     /// <summary>Maximum attainable pilot rank.</summary>
        public static readonly WingRank TopRank = WingRank.Legend;

        private static readonly List<WingPilot> pool = new List<WingPilot>();
        private static readonly HashSet<WingPilot> reserved = new HashSet<WingPilot>();
        private static readonly Dictionary<PersistentID, WingPilot> assigned =
            new Dictionary<PersistentID, WingPilot>();

     /// <summary>All squadron pilots, including losses. The free pool is separate; lost records remain
     /// visible but unavailable for assignment.</summary>
        private static readonly List<WingPilot> roster = new List<WingPilot>();

     /// <summary>Pilot selected for the next aircraft. Assignment advances to an available successor;
     /// lost pilots cannot be selected.</summary>
        private static WingPilot selectedPilot;

     /// <summary>Current pilot choice for the next aircraft.</summary>
        public static WingPilot Selected => selectedPilot;

     /// <summary>Whether the pilot belongs to the roster.</summary>
        public static bool Contains(WingPilot pilot) => pilot != null && roster.Contains(pilot);

     /// <summary>Whether the roster already contains this callsign.</summary>
        public static bool ContainsCallsign(string callsign) => FindByCallsign(callsign) != null;

     /// <summary>Look up a callsign; return null if absent.</summary>
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

     /// <summary>Whether the pilot is alive and eligible for selection.</summary>
        public static bool IsSelectable(WingPilot pilot) => pilot != null && !pilot.Lost;

     /// <summary>Whether the pilot currently occupies an aircraft.</summary>
        public static bool IsFlying(WingPilot pilot) =>
            pilot != null && assigned.ContainsValue(pilot);

     /// <summary>Whether a pending delivery reserves this pilot.</summary>
        public static bool IsReserved(WingPilot pilot) =>
            pilot != null && reserved.Contains(pilot);

     /// <summary>Whether the pilot can occupy a seat now.</summary>
        public static bool IsFree(WingPilot pilot) =>
            IsSelectable(pilot) && !IsFlying(pilot) && !IsReserved(pilot);

     /// <summary>Choose the next pilot, excluding losses.</summary>
        public static void Select(WingPilot pilot)
        {
            if (!IsSelectable(pilot)) return;
            selectedPilot = pilot;
        }

     /// <summary>Advance with wraparound to a free pilot, or the next selectable pilot if none are
     /// free.</summary>
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

     /// <summary>Reserve the selected or next free pilot for purchase, then advance
     /// selection.</summary>
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

     /// <summary>Release a failed or rolled-back purchase's pilot reservation.</summary>
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

     /// <summary>Replaceable pilot factory; the default generates a random squadron identity. Other
     /// systems obtain pilots through this provider.</summary>
        public static Func<int, WingPilot> Provide = DefaultProvider;

        public static void Reset()
        {
            pool.Clear();
            reserved.Clear();
            assigned.Clear();
            roster.Clear();
            created = 0;
            // Start without a selected pilot; recruitment is manual or triggered by purchase.
            selectedPilot = null;
        }

        private static int created;

        // Pilot assignment.

     /// <summary>Assigned squadron pilot for this aircraft, or null.</summary>
        public static WingPilot Of(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            return assigned.TryGetValue(aircraft.persistentID, out WingPilot pilot) ? pilot : null;
        }

        public static WingPilot Of(WingMember member) =>
            member != null ? Of(member.Aircraft) : null;

     /// <summary>Assign the reserved preferred pilot, otherwise the player's selection, most senior
     /// free pilot, or a new pilot. Advance selection after seating the chosen pilot.</summary>
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

     /// <summary>Release the seat, returning survivors to the pool with their records and marking
     /// losses unavailable.</summary>
        public static void Retire(WingMember member, bool survived)
        {
            if (member == null || member.Aircraft == null) return;
            Retire(member.Aircraft.persistentID, survived);
        }

     /// <summary>Settle by durable aircraft ID after roster removal. Retain the seat assignment during
     /// RTB so the pilot cannot fly two aircraft at once.</summary>
        internal static void Retire(PersistentID id, bool survived)
        {
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
            Plugin.LogVerbose(
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

     /// <summary>Recruit a pilot into the ground roster.</summary>
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

     /// <summary>Add a custom pilot record to the squadron.</summary>
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

        // Experience awards.

     /// <summary>Award XP to an assigned pilot when progression is enabled.</summary>
        public static void Award(Aircraft aircraft, int xp, string reason)
        {
            if (xp <= 0 || !Plugin.Settings.PilotProgression.Value) return;

            WingPilot pilot = Of(aircraft);
            if (pilot == null) return;

            WingRank before = pilot.Rank;
            pilot.Xp += xp;

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose(
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

     /// <summary>Award a completed sortie on base recovery.</summary>
        public static void NoteSortie(Aircraft aircraft)
        {
            WingPilot pilot = Of(aircraft);
            if (pilot == null) return;

            pilot.Sorties++;
            Award(aircraft, WingTuning.XpPerSortie, "sortie");
        }

     /// <summary>Award survival of a missile targeting this aircraft.</summary>
        public static void NoteSurvivedEngagement(Aircraft aircraft) =>
            Award(aircraft, WingTuning.XpPerEngagement, "survived engagement");

        // Rank calculations.

     /// <summary>Triangular XP thresholds share one tuning value so the rank curve scales
     /// consistently.</summary>
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

     /// <summary>Rank bonus above Rookie, scaled by Pilot/RankEffect; disabling effects preserves pilot
     /// records.</summary>
        public static float SkillBonus(Aircraft aircraft)
        {
            if (!Plugin.Settings.PilotProgression.Value) return 0f;

            WingPilot pilot = Of(aircraft);
            if (pilot == null) return 0f;

            float effect = Plugin.Settings != null ? Plugin.Settings.RankEffect.Value : WingTuning.RankEffect;
            float perRank = Mathf.Clamp(effect, 0f, 2f) * 0.06f;
            return (int)pilot.Rank * perRank;
        }

     /// <summary>Pilot weapon-envelope scale, never below 1.</summary>
        public static float EnvelopeScale(Aircraft aircraft) => 1f + SkillBonus(aircraft) * 0.5f;

     /// <summary>Pilot shot-interval scale, never above 1.</summary>
        public static float ReactionScale(Aircraft aircraft) => 1f - SkillBonus(aircraft) * 0.5f;

        // Roster presentation.

     /// <summary>Build a fresh display roster with flying pilots and seniority first, losses last.
     /// Panels refresh this short list as XP changes.</summary>
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

     /// <summary>Selectable pilots in descending seniority.</summary>
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

        // Default identities.

     /// <summary>Default provider for additional pilots, using random names to avoid repeatedly
     /// selecting an existing callsign.</summary>
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
