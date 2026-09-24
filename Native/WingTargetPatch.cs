using System.Collections.Generic;
using HarmonyLib;

namespace WingCommand
{
    /// <summary>An engaged member with an assigned target (Attack My Target, spec M5 §2.1) chooses it when one of its
    /// weapon stations can attack it, by the game's own analysis; else the game's choice stands. Runs after the aces'
    /// hunt (<see cref="Interop.WingSquad"/>), which never targets wing members' choices.</summary>
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class WingTargetPatch
    {
        private static void Postfix(Unit searcher, List<WeaponStation> stationList, ref CombatAI.TargetSearchResults __result)
        {
            if (!(searcher is Aircraft a) || stationList == null || WingService.Instance == null) return;
            Unit target = WingService.Instance.AssignedTarget(a);
            if (target == null || target.disabled || a.NetworkHQ == null) return;
            TrackingInfo tracking = a.NetworkHQ.GetTrackingData(target.persistentID);
            if (tracking == null) return;
            WeaponStation best = null;
            float opportunity = 0f;
            for (int i = 0; i < stationList.Count; i++)
            {
                WeaponStation w = stationList[i];
                if (w == null || w.Cargo || w.Ammo <= 0 || w.WeaponInfo == null) continue;
                float value = CombatAI.AnalyzeTarget(w, a, tracking, 0f, -1f, 100f).opportunity;
                if (value <= opportunity) continue;
                opportunity = value;
                best = w;
            }
            if (best != null) __result = new CombatAI.TargetSearchResults(target, best, opportunity, __result.outOfAmmo);
        }
    }
}
