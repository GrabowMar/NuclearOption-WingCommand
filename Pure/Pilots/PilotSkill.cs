using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>How a squadron pilot flies (parent plan M3: the per-pilot profile from rank and perks): rank raises the
    /// flight pipeline's precision (tighter guidance) and aggression (a wider bank ceiling), scaled by the rank-effect
    /// setting (0 turns progression off); Wingman Instinct adds precision, Energy Fighter and Apex Hunter add
    /// aggression; both are capped.</summary>
    internal static class PilotSkill
    {
        public static float BasePrecision = 0.9f, PrecisionPerRank = 0.06f, MaxPrecision = 1.2f, InstinctPrecision = 0.05f;
        public static float BaseAggression = 0.4f, AggressionPerRank = 0.05f, MaxAggression = 0.8f;
        public static float EnergyFighterAggression = 0.1f, ApexHunterAggression = 0.05f;

        public static void For(WingRank rank, IReadOnlyList<PilotPerk> perks, float rankEffect, out float precision, out float aggression)
        {
            float steps = (int)rank * Math.Max(0f, Math.Min(2f, rankEffect));
            bool on = rankEffect > 0f;
            precision = BasePrecision + PrecisionPerRank * steps + (on && Has(perks, PilotPerk.WingmanInstinct) ? InstinctPrecision : 0f);
            aggression = BaseAggression + AggressionPerRank * steps +
                         (on && Has(perks, PilotPerk.EnergyFighter) ? EnergyFighterAggression : 0f) +
                         (on && Has(perks, PilotPerk.ApexHunter) ? ApexHunterAggression : 0f);
            precision = Math.Min(MaxPrecision, precision);
            aggression = Math.Min(MaxAggression, aggression);
        }

        private static bool Has(IReadOnlyList<PilotPerk> perks, PilotPerk perk)
        {
            if (perks == null) return false;
            for (int i = 0; i < perks.Count; i++)
                if (perks[i] == perk) return true;
            return false;
        }
    }
}
