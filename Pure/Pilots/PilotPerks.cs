using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum WingRank { Rookie, Wingman, Veteran, Ace, Legend }
    internal enum PilotRecoveryStatus { None, Downed, Missing, Captured }
    internal enum PilotPerk
    {
        Luck, FuelDiscipline, Toughness, GTolerance, Commando,
        Fireproof, BlastSurvivor, BallisticVest, CrashTraining, SecondChance,
        CoolHead, HighAltitudeCruise, LowLevelEvasion, RadarGhost, HeatGhost,
        NotchExpert, ChaffReflex, FlareReflex, EcmSpecialist, FastLearner,
        QuickDraw, Standoff, Survivalist, Pathfinder
    }

    /// <summary>Engine-free progression and survival balance. Each promotion grants one unique perk.</summary>
    internal static class PilotPerks
    {
        public static readonly int Count = Enum.GetValues(typeof(PilotPerk)).Length;
        public const float LuckChance = 0.30f;
        public const float GuidanceOffset = 350f;
        public const float CommandoChance = 0.5f;
        public const float EscapeDelay = 120f;

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

        public static void GrantThroughRank(List<PilotPerk> owned, WingRank rank, Func<int, int> random)
        {
            int desired = Math.Min((int)rank, Count);
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
                case PilotPerk.Luck: return "LUCK";
                case PilotPerk.FuelDiscipline: return "FUEL DISCIPLINE";
                case PilotPerk.Toughness: return "TOUGHNESS";
                case PilotPerk.GTolerance: return "G TOLERANCE";
                case PilotPerk.Commando: return "COMMANDO";
                case PilotPerk.Fireproof: return "FIREPROOF";
                case PilotPerk.BlastSurvivor: return "BLAST SURVIVOR";
                case PilotPerk.BallisticVest: return "BALLISTIC VEST";
                case PilotPerk.CrashTraining: return "CRASH TRAINING";
                case PilotPerk.SecondChance: return "SECOND CHANCE";
                case PilotPerk.CoolHead: return "COOL HEAD";
                case PilotPerk.HighAltitudeCruise: return "HIGH ALTITUDE CRUISE";
                case PilotPerk.LowLevelEvasion: return "LOW LEVEL EVASION";
                case PilotPerk.RadarGhost: return "RADAR GHOST";
                case PilotPerk.HeatGhost: return "HEAT GHOST";
                case PilotPerk.NotchExpert: return "NOTCH EXPERT";
                case PilotPerk.ChaffReflex: return "CHAFF REFLEX";
                case PilotPerk.FlareReflex: return "FLARE REFLEX";
                case PilotPerk.EcmSpecialist: return "ECM SPECIALIST";
                case PilotPerk.FastLearner: return "FAST LEARNER";
                case PilotPerk.QuickDraw: return "QUICK DRAW";
                case PilotPerk.Standoff: return "STANDOFF";
                case PilotPerk.Survivalist: return "SURVIVALIST";
                case PilotPerk.Pathfinder: return "PATHFINDER";
                default: return "UNKNOWN";
            }
        }

        public static string Description(PilotPerk perk)
        {
            switch (perk)
            {
                case PilotPerk.Luck: return "30% chance per incoming missile to bias guidance 350m sideways; proximity blasts still hurt.";
                case PilotPerk.FuelDiscipline: return "30% less engine fuel consumption. Leaks and fires remain dangerous.";
                case PilotPerk.Toughness: return "35% less seated-pilot damage; does not strengthen the airframe.";
                case PilotPerk.GTolerance: return "75% less pilot damage from extreme G loads.";
                case PilotPerk.Commando: return "One 50% escape chance after landing alive; return after 120 seconds if still free and alive.";
                case PilotPerk.Fireproof: return "75% less fire damage to the seated pilot. Does not extinguish the aircraft.";
                case PilotPerk.BlastSurvivor: return "60% less explosion damage to the seated pilot.";
                case PilotPerk.BallisticVest: return "60% less projectile damage to the seated pilot.";
                case PilotPerk.CrashTraining: return "60% less impact damage to the seated pilot; aircraft can still be destroyed.";
                case PilotPerk.SecondChance: return "Survive one otherwise fatal seated-pilot hit at 1 HP per sortie. Rearmed by assignment or successful base sortie; subsequent damage can kill.";
                case PilotPerk.CoolHead: return "40% less engine fuel use while a native incoming-missile warning is active.";
                case PilotPerk.HighAltitudeCruise: return "40% less engine fuel use above 3000m AGL.";
                case PilotPerk.LowLevelEvasion: return "25% guidance-error chance per incoming missile, rolled when first encountered below 300m AGL.";
                case PilotPerk.RadarGhost: return "30% guidance-error chance per incoming ARH or SARH missile.";
                case PilotPerk.HeatGhost: return "30% guidance-error chance per incoming IR missile.";
                case PilotPerk.NotchExpert: return "40% guidance-error chance against radar missiles first encountered within 15 degrees of a beam approach, while moving over 30m/s.";
                case PilotPerk.ChaffReflex: return "Fitted chaff dispensers have 50% shorter burst intervals; uses ammunition faster.";
                case PilotPerk.FlareReflex: return "Fitted flare dispensers have 50% shorter burst intervals; uses ammunition faster.";
                case PilotPerk.EcmSpecialist: return "Fitted self-protection radar jammer produces 75% stronger ECM at normal power cost.";
                case PilotPerk.FastLearner: return "50% more XP from every award, reaching ranks and survival perks sooner.";
                case PilotPerk.QuickDraw: return "35% shorter wing weapon-command intervals; native reload and lock requirements still apply.";
                case PilotPerk.Standoff: return "25% larger wing weapon-employment envelope; native weapon readiness still applies.";
                case PilotPerk.Survivalist: return "60% less incoming projectile, blast and fire damage to the dismounted pilot before native armour calculations.";
                case PilotPerk.Pathfinder: return "35% independent escape chance with a 60-second return delay; with Commando, 67.5% chance and 60 seconds. One roll per ejection.";
                default: return "Unknown perk.";
            }
        }

        public static float FuelUse(float amount, bool enabled) => amount > 0f && enabled ? amount * 0.7f : amount;
        public static float PilotDamage(float amount, bool enabled) => amount > 0f && enabled ? amount * 0.65f : amount;
        public static float GLoad(float squaredG, bool enabled) =>
            enabled && squaredG > 400f ? 400f + (squaredG - 400f) * 0.25f : squaredG;

        public static float EscapeChance(bool commando, bool pathfinder) =>
            (float)(1d - (commando ? 0.5d : 1d) * (pathfinder ? 0.65d : 1d));

        public static float MissileErrorChance(bool lucky, bool lowLevel, bool radarGhost,
                                               bool heatGhost, bool notching) =>
            (float)Math.Min(0.8d, 1d - (lucky ? 0.7d : 1d) * (lowLevel ? 0.75d : 1d) *
                (radarGhost ? 0.7d : 1d) * (heatGhost ? 0.7d : 1d) * (notching ? 0.6d : 1d));

        public static float FuelMultiplier(bool disciplined, bool warned, bool highCruise) =>
            Math.Max(0.25f, (disciplined ? 0.7f : 1f) * (warned ? 0.6f : 1f) * (highCruise ? 0.6f : 1f));

        public static int Experience(int amount, bool fastLearner) => amount <= 0 ? 0 :
            (int)Math.Min(int.MaxValue, fastLearner ? ((long)amount * 3 + 1) / 2 : amount);
    }
}
