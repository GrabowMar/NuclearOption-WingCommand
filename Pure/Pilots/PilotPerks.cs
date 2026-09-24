using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum WingRank { Rookie, Wingman, Veteran, Ace, Legend }
    internal enum PilotRecoveryStatus { None, Downed, Missing, Captured }
    internal enum PilotPerk
    {
        // Core Surviving
        Toughness, Commando, NotchExpert, FastLearner, QuickDraw, Standoff,
        // Merged & Special
        Countermeasures, Ghost, Talented,
        // 16 New Tactical Combat Skills
        Marksman, Snapshot, LeadPursuit, HeadOnJoust, EnergyFighter,
        ApexHunter, Bombardier, SalvoSpecialist, WildWeasel, TargetMaster,
        WingmanInstinct, CombatSpread, TerrainHugger, BreakTurn, EarlyWarning, Burnthrough
    }

    /// <summary>Engine-free progression and survival balance. Each promotion grants one unique perk, with
    /// Talented expanding available slots.</summary>
    internal static class PilotPerks
    {
        public static readonly int Count = Enum.GetValues(typeof(PilotPerk)).Length;
        public const float GuidanceOffset = 350f;
        public const float CommandoChance = 0.60f;
        public const float EscapeDelay = 90f;

        public static int XpForRank(WingRank rank) =>
            WingTuning.XpPerRank * (int)rank * ((int)rank + 1) / 2;

        public static WingRank RankFor(int xp)
        {
            WingRank rank = WingRank.Rookie;
            while (rank < WingRank.Legend && xp >= XpForRank(rank + 1)) rank++;
            return rank;
        }

        public static int AddXp(int current, int award) =>
            (int)Math.Min(int.MaxValue, (long)Math.Max(0, current) + Math.Max(0, award));

        public static int DesiredPerkCount(List<PilotPerk> owned, WingRank rank)
        {
            int extra = (owned != null && owned.Contains(PilotPerk.Talented)) ? 3 : 0;
            return Math.Min((int)rank + extra, Count);
        }

        public static void GrantThroughRank(List<PilotPerk> owned, WingRank rank, Func<int, int> random)
        {
            int desired = DesiredPerkCount(owned, rank);
            while (owned.Count < desired)
            {
                var available = new List<PilotPerk>();
                for (int i = 0; i < Count; i++)
                    if (!owned.Contains((PilotPerk)i)) available.Add((PilotPerk)i);
                if (available.Count == 0) return;
                owned.Add(available[random(available.Count)]);
            }
        }

        public static string Name(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Toughness: return "TOUGHNESS";
                case PilotPerk.Commando: return "COMMANDO";
                case PilotPerk.NotchExpert: return "NOTCH EXPERT";
                case PilotPerk.FastLearner: return "FAST LEARNER";
                case PilotPerk.QuickDraw: return "QUICK DRAW";
                case PilotPerk.Standoff: return "STANDOFF";
                case PilotPerk.Countermeasures: return "COUNTERMEASURES";
                case PilotPerk.Ghost: return "GHOST";
                case PilotPerk.Talented: return "TALENTED";
                case PilotPerk.Marksman: return "MARKSMAN";
                case PilotPerk.Snapshot: return "SNAPSHOT";
                case PilotPerk.LeadPursuit: return "LEAD PURSUIT";
                case PilotPerk.HeadOnJoust: return "HEAD-ON JOUST";
                case PilotPerk.EnergyFighter: return "ENERGY FIGHTER";
                case PilotPerk.ApexHunter: return "APEX HUNTER";
                case PilotPerk.Bombardier: return "BOMBARDIER";
                case PilotPerk.SalvoSpecialist: return "SALVO SPECIALIST";
                case PilotPerk.WildWeasel: return "WILD WEASEL";
                case PilotPerk.TargetMaster: return "TARGET MASTER";
                case PilotPerk.WingmanInstinct: return "WINGMAN INSTINCT";
                case PilotPerk.CombatSpread: return "COMBAT SPREAD";
                case PilotPerk.TerrainHugger: return "TERRAIN HUGGER";
                case PilotPerk.BreakTurn: return "BREAK TURN";
                case PilotPerk.EarlyWarning: return "EARLY WARNING";
                case PilotPerk.Burnthrough: return "BURNTHROUGH";
                default: return "UNKNOWN";
            }
        }

        public static string Description(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Toughness: return "50% less seated-pilot damage; does not strengthen the airframe.";
                case PilotPerk.Commando: return "One 60% escape chance after landing alive; return after 90 seconds if still free and alive.";
                case PilotPerk.NotchExpert: return "40% guidance-error chance against radar missiles first encountered within 15 degrees of a beam approach, while moving over 30m/s.";
                case PilotPerk.FastLearner: return "50% more XP from every award, reaching ranks and survival perks sooner.";
                case PilotPerk.QuickDraw: return "40% shorter interval between wing standing-fire shots from formation.";
                case PilotPerk.Standoff: return "30% longer reach for wing standing fire from formation; never beyond a missile's own range.";
                case PilotPerk.Countermeasures: return "Halves dispenser burst intervals and doubles ECM jamming intensity.";
                case PilotPerk.Ghost: return "35% guidance disruption chance against all incoming guided missiles (ARH, SARH, and IR seekers).";
                case PilotPerk.Talented: return "Natural aviation prodigy. Grants 3 additional skill slots, immediately unlocking extra abilities.";
                case PilotPerk.Marksman: return "Not active in 1.0 yet: cannon engagements are flown by the game's combat AI.";
                case PilotPerk.Snapshot: return "40% wider off-boresight tolerance for standing fire and Splash from formation.";
                case PilotPerk.LeadPursuit: return "Not active in 1.0 yet: dogfights are flown by the game's combat AI.";
                case PilotPerk.HeadOnJoust: return "Standing fire and Splash launch from 25% farther when the target closes head-on faster than 350 m/s.";
                case PilotPerk.EnergyFighter: return "Preserves airspeed above corner speed in sustained maneuvers, trading altitude to prevent dogfight stalls.";
                case PilotPerk.ApexHunter: return "Radar missiles in standing fire and Splash launch from 40% farther above 4,000 m; a little more aggressive flying.";
                case PilotPerk.Bombardier: return "Not active in 1.0 yet: bombing runs are flown by the game's combat AI.";
                case PilotPerk.SalvoSpecialist: return "Splash fires two missiles from this pilot instead of one.";
                case PilotPerk.WildWeasel: return "Not active in 1.0 yet: target choice in a fight is the game's combat AI's.";
                case PilotPerk.TargetMaster: return "Holds an attack order's target 50% longer through a short loss (a target saturated with missiles in flight).";
                case PilotPerk.WingmanInstinct: return "30% faster rejoin acceleration and 25% tighter station-keeping in escort formations.";
                case PilotPerk.CombatSpread: return "Not active in 1.0 yet: the formation's spacing is set for the whole wing.";
                case PilotPerk.TerrainHugger: return "25 m less terrain clearance while flying with the wing.";
                case PilotPerk.BreakTurn: return "Reacts at once to a missile inside 2,500 m while flying with the wing.";
                case PilotPerk.EarlyWarning: return "Reacts 1.5 seconds sooner to an incoming missile while flying with the wing.";
                case PilotPerk.Burnthrough: return "Not active in 1.0 yet: radar locks are the game's own.";
                default: return "Unknown perk.";
            }
        }

        public static string IconKey(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Toughness: return "cover";
                case PilotPerk.Commando: return "land";
                case PilotPerk.NotchExpert: return "formation";
                case PilotPerk.FastLearner: return "tasking";
                case PilotPerk.QuickDraw: return "attack";
                case PilotPerk.Standoff: return "orbit";
                case PilotPerk.Countermeasures: return "jam";
                case PilotPerk.Ghost: return "fallback";
                case PilotPerk.Talented: return "selection";
                case PilotPerk.Marksman: return "engage";
                case PilotPerk.Snapshot: return "attack";
                case PilotPerk.LeadPursuit: return "maneuver";
                case PilotPerk.HeadOnJoust: return "engage";
                case PilotPerk.EnergyFighter: return "posture";
                case PilotPerk.ApexHunter: return "orbit";
                case PilotPerk.Bombardier: return "attack";
                case PilotPerk.SalvoSpecialist: return "attack";
                case PilotPerk.WildWeasel: return "engage";
                case PilotPerk.TargetMaster: return "selection";
                case PilotPerk.WingmanInstinct: return "rejoin";
                case PilotPerk.CombatSpread: return "shape_CombatSpread";
                case PilotPerk.TerrainHugger: return "land";
                case PilotPerk.BreakTurn: return "maneuver";
                case PilotPerk.EarlyWarning: return "disc";
                case PilotPerk.Burnthrough: return "jam";
                default: return "root";
            }
        }

        // Pure mathematical and deterministic multipliers.

        public static float PilotDamage(float amount, bool enabled) => amount > 0f && enabled ? amount * 0.5f : amount;

        public static float EscapeChance(bool commando) => commando ? CommandoChance : 0f;

        public static float MissileErrorChance(bool ghost, bool notching) =>
            (float)Math.Min(0.85d, 1d - (ghost ? 0.65d : 1d) * (notching ? 0.55d : 1d));

        public static int Experience(int amount, bool fastLearner) => amount <= 0 ? 0 :
            (int)Math.Min(int.MaxValue, fastLearner ? ((long)amount * 3 + 1) / 2 : amount);

        public static float GunRangeMultiplier(bool marksman) => marksman ? 1.30f : 1.0f;
        public static float OffBoresightMultiplier(bool snapshot) => snapshot ? 1.40f : 1.0f;
        public static float DogfightEffort(bool leadPursuit) => leadPursuit ? 2.5f : 2.0f;
        public static bool IsHeadOn(float closingSpeed, float dotVelocity) => closingSpeed > 350f && dotVelocity < -0.7f;
        public static float HeadOnRangeMultiplier(bool joust) => joust ? 1.25f : 1.0f;
        public static float CornerSpeedFloor(float cornerSpeed, bool energyFighter) => energyFighter ? cornerSpeed * 1.15f : cornerSpeed;
        public static float HighAltitudeRangeMultiplier(float altitude, bool apexHunter) => (apexHunter && altitude >= 4000f) ? 1.40f : 1.0f;
        public static float BombFloorScale(bool bombardier) => bombardier ? 0.70f : 1.0f;
        public static float BombEnvelopeScale(bool bombardier) => bombardier ? 1.35f : 1.0f;
        public static float SalvoDelayScale(bool salvoSpecialist) => salvoSpecialist ? 0.60f : 1.0f;
        public static float SeadPriorityMultiplier(bool isRadar, bool wildWeasel) => (isRadar && wildWeasel) ? 1.50f : 1.0f;
        public static float ClaimDurationMultiplier(bool targetMaster) => targetMaster ? 1.50f : 1.0f;
        public static float FormationRejoinScale(bool wingmanInstinct) => wingmanInstinct ? 1.30f : 1.0f;
        public static float CombatSpreadScale(bool underThreat, bool combatSpread) => (underThreat && combatSpread) ? 1.40f : 1.0f;
        public static float TerrainFloorOffset(bool terrainHugger) => terrainHugger ? -25f : 0f;
        public static float DefensiveRollAuthority(bool breakTurn) => breakTurn ? 1.50f : 1.0f;
        public static float EarlyWarningReactionLead(bool earlyWarning) => earlyWarning ? 1.5f : 0f;
        public static float BurnthroughJammingScale(bool burnthrough) => burnthrough ? 0.60f : 1.0f;
    }
}
