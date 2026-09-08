using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Adds reservation pressure to native target scores so locally simulated AI spread across
    /// comparable targets. Stock opportunity and threat scores still govern selection, including player
    /// targets.</summary>
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class AiTargetDeconflictionPatch
    {
        private const float SelectionSeconds = 7f;

        [HarmonyPostfix]
        private static void Postfix(Unit searcher, float bravery, List<WeaponStation> stationList,
                                    ref CombatAI.TargetSearchResults __result)
        {
            if (!WingFidelity.Deconfliction || !Plugin.Settings.AiTargetSpreading.Value) return;
            if (!(searcher is Aircraft aircraft) || aircraft.Player != null || !aircraft.LocalSim) return;
            if (aircraft.NetworkHQ == null || stationList == null || stationList.Count == 0)
            {
                TacticalCoordinator.NoteSelection(__result.target, aircraft, SelectionSeconds);
                return;
            }

            Unit bestTarget = null;
            WeaponStation bestStation = null;
            float bestScore = 0f;
            float bestOpportunity = 0f;

            foreach (WeaponStation station in stationList)
            {
                if (station == null || station.Cargo || station.Ammo <= 0 || station.WeaponInfo == null)
                    continue;
                if (station.WeaponInfo.energy && (aircraft.GetPowerSupply()?.GetCharge() ?? 0f) < 0.6f)
                    continue;

                foreach (KeyValuePair<PersistentID, TrackingInfo> pair in aircraft.NetworkHQ.trackingDatabase)
                {
                    TrackingInfo tracking = pair.Value;
                    if (tracking == null || !tracking.TryGetUnit(out Unit candidate)) continue;
                    if (candidate == null || candidate.disabled || candidate.NetworkHQ == null ||
                        candidate.NetworkHQ == aircraft.NetworkHQ)
                        continue;
                    if (!aircraft.NetworkHQ.IsTargetPositionAccurate(candidate, 1000f)) continue;

                    float range = FastMath.Distance(tracking.GetPosition(), aircraft.GlobalPosition());
                    OpportunityThreat analysis = CombatAI.AnalyzeTarget(
                        station, aircraft, tracking, 0f, range, 100f);
                    if (analysis.opportunity <= 0f) continue;

                    float score = analysis.opportunity * (1f + analysis.threat)
                                / Mathf.Max(range, 500f);

                    TargetRequirements requirements = station.WeaponInfo.targetRequirements;
                    if (range > requirements.maxRange * 1.2f) score *= 0.5f;

                    // Reservation pressure only lowers scores. Skip capacity and roster scans when
                    // even this unpenalised candidate cannot beat the current choice.
                    if (score <= bestScore) continue;

                    int capacity = Mathf.Clamp(
                        Mathf.CeilToInt(station.WeaponInfo.CalcAttacksNeeded(candidate)), 1, 4);
                    if (candidate is Missile) capacity = 1;

                    int committed = TacticalCoordinator.CountCommitments(candidate, aircraft)
                                  + Mathf.Max(tracking.attackers, 0);
                    int excess = Mathf.Max(committed - capacity + 1, 0);

                    float pressure = 1f + excess * WingTuning.TargetSaturationPenalty;
                    score /= pressure;
                    if (score <= bestScore) continue;

                    bestScore = score;
                    bestTarget = candidate;
                    bestStation = station;
                    bestOpportunity = analysis.opportunity;
                }
            }

            if (bestTarget == null)
            {
                TacticalCoordinator.NoteSelection(__result.target, aircraft, SelectionSeconds);
                return;
            }

            // Keep the stock bravery gate so deconfliction cannot admit a rejected threat.
            if (bestOpportunity * bravery * 2f < 0.35f &&
                aircraft.NetworkHQ.GetAircraftThreat(bestTarget.persistentID) >
                    bestOpportunity * bravery * 2f &&
                FastMath.Distance(bestTarget.GlobalPosition(), aircraft.GlobalPosition()) >
                    bestStation.WeaponInfo.targetRequirements.maxRange * 2f)
            {
                TacticalCoordinator.NoteSelection(__result.target, aircraft, SelectionSeconds);
                return;
            }

            __result = new CombatAI.TargetSearchResults(
                bestTarget, bestStation, bestOpportunity, __result.outOfAmmo);
            TacticalCoordinator.NoteSelection(bestTarget, aircraft, SelectionSeconds);
        }
    }
}
