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
        // Reused: the call is synchronous on the main thread and never re-entered (weapons-radar.md A).
        private static readonly List<WeaponStation> filtered = new List<WeaponStation>(16);
        private static bool confirmed;

        /// <summary>Spec WMC rebuild R3: a member whose WEAPONS or RADAR restrict it searches only the stations it may use.
        /// The aircraft's own list is swapped for a copy, never changed.</summary>
        private static void Prefix(Unit searcher, ref List<WeaponStation> stationList)
        {
            if (searcher is Aircraft a && WingService.Instance != null && WingService.Instance.FilterStations(a, stationList, filtered))
                stationList = filtered;
        }

        private static void Postfix(Unit searcher, List<WeaponStation> stationList, ref CombatAI.TargetSearchResults __result)
        {
            if (!(searcher is Aircraft a) || stationList == null || WingService.Instance == null) return;
            WingService wing = WingService.Instance;
            if (!confirmed && ReferenceEquals(stationList, filtered))
            {
                // weapons-radar.md E: that the postfix sees the prefix's list is Harmony's standard behaviour; logged once.
                confirmed = true;
                Plugin.Logger.LogInfo("[Wing] target choice: the filtered station list reaches the postfix");
            }
            Unit target = wing.AssignedTarget(a);
            if (target == null)
            {
                // Fighting on its own choice: spread off the others' and the player's targets (spec M5 §6.4).
                if (__result.target != null) wing.Spread(a, stationList, ref __result);
                wing.Veto(a, ref __result);
                return;
            }
            WingMember m = wing.MemberOf(a);
            if (target.disabled || a.NetworkHQ == null || m == null || !wing.AllowsTarget(m, target))
            {
                wing.Veto(a, ref __result);
                return;
            }
            WingDoctrine doctrine = wing.DoctrineFor(m);
            TrackingInfo tracking = a.NetworkHQ.GetTrackingData(target.persistentID);
            // The game only chooses a target whose position is accurate (review M5a I4): a stale track would be circled.
            if (tracking == null || !a.NetworkHQ.IsTargetPositionAccurate(target, WingService.TargetAccuracyMetres)) return;
            WeaponStation best = null;
            float opportunity = 0f;
            for (int i = 0; i < stationList.Count; i++)
            {
                WeaponStation w = stationList[i];
                if (!WingService.UsableBy(m, doctrine, w)) continue;
                float value = CombatAI.AnalyzeTarget(w, a, tracking, 0f, -1f, 100f).opportunity;
                if (value <= opportunity) continue;
                opportunity = value;
                best = w;
            }
            if (best != null) __result = new CombatAI.TargetSearchResults(target, best, opportunity, __result.outOfAmmo);
            wing.Veto(a, ref __result);
        }
    }
}
