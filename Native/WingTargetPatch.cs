using System.Collections.Generic;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>An engaged member with an assigned target (Attack My Target, spec M5 §2.1) chooses it when one of its
    /// weapon stations can attack it and its position is accurate, by the game's own analysis; else the game's choice
    /// stands. The aces' hunt patches the same method at equal priority; the two act on different aircraft (enemy aces,
    /// wing members).</summary>
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class WingTargetPatch
    {
        private static void Postfix(Unit searcher, List<WeaponStation> stationList, ref CombatAI.TargetSearchResults __result)
        {
            if (!(searcher is Aircraft a) || stationList == null || WingService.Instance == null) return;
            Unit target = WingService.Instance.AssignedTarget(a);
            if (target == null)
            {
                // Fighting on its own choice: spread off the others' and the player's targets (spec M5 §6.4).
                if (__result.target != null) WingService.Instance.Spread(a, stationList, ref __result);
                return;
            }
            if (target.disabled || a.NetworkHQ == null) return;
            TrackingInfo tracking = a.NetworkHQ.GetTrackingData(target.persistentID);
            // The game only chooses a target whose position is accurate (review M5a I4): a stale track would be circled.
            if (tracking == null || !a.NetworkHQ.IsTargetPositionAccurate(target, WingService.TargetAccuracyMetres)) return;
            WeaponStation best = null;
            float opportunity = 0f;
            for (int i = 0; i < stationList.Count; i++)
            {
                WeaponStation w = stationList[i];
                if (!WingService.Usable(a, w)) continue;
                float value = CombatAI.AnalyzeTarget(w, a, tracking, 0f, -1f, 100f).opportunity;
                if (value <= opportunity) continue;
                opportunity = value;
                best = w;
            }
            if (best != null) __result = new CombatAI.TargetSearchResults(target, best, opportunity, __result.outOfAmmo);
        }
    }
}
