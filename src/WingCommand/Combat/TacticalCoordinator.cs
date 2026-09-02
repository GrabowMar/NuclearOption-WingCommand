using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Short-lived target reservations shared by every combat path in this plugin.
    ///
    /// Stock AI evaluates targets independently. It accounts for missiles already in the
    /// air, but not for the other aircraft that have just selected the same contact, so a
    /// whole package can make the same locally-correct choice. Reservations turn those
    /// independent decisions into a flight-level allocation without permanently locking a
    /// target to anyone: if an aircraft cannot prosecute, its claim expires in seconds.
    /// </summary>
    internal static class TacticalCoordinator
    {
        private sealed class Claim
        {
            public Aircraft Owner;
            public float Until;
        }

        private static readonly Dictionary<Unit, List<Claim>> claims =
            new Dictionary<Unit, List<Claim>>();
        private static readonly List<Unit> staleTargets = new List<Unit>();
        private static readonly List<Aircraft> owners = new List<Aircraft>();

        public static void Reset()
        {
            claims.Clear();
            staleTargets.Clear();
            owners.Clear();
        }

        /// <summary>Number of distinct other aircraft currently committed to a target.</summary>
        public static int CountClaims(Unit target, Aircraft except = null)
        {
            if (target == null || target.disabled) return 0;

            Prune();
            owners.Clear();

            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                    AddOwner(list[i].Owner, except);
            }

            // Explicit attack orders are persistent commitments, not short weapon pulses.
            // Read them from the roster so they never expire halfway through an attack run.
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing != null)
            {
                IReadOnlyList<WingMember> members = wing.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    WingMember member = members[i];
                    if (member.AssignedTarget == target)
                        AddOwner(member.Aircraft, except);
                }
            }

            return owners.Count;
        }

        /// <summary>
        /// Reserve a target if its concurrency limit has room. An existing owner may renew
        /// its own claim even while the target is full.
        /// </summary>
        public static bool TryClaim(Unit target, Aircraft owner, int maximum, float seconds)
        {
            if (target == null || target.disabled || owner == null || maximum <= 0) return false;

            Prune();
            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Owner != owner) continue;
                    list[i].Until = Time.timeSinceLevelLoad + seconds;
                    return true;
                }
            }

            if (CountClaims(target, owner) >= maximum) return false;

            // CountClaims prunes empty buckets, so create the bucket only after the capacity
            // check. Creating it first let the prune remove it and the new claim was then
            // added to a detached list that the dictionary no longer owned.
            if (!claims.TryGetValue(target, out list))
            {
                list = new List<Claim>();
                claims.Add(target, list);
            }

            list.Add(new Claim
            {
                Owner = owner,
                Until = Time.timeSinceLevelLoad + seconds,
            });
            return true;
        }

        public static void Release(Aircraft owner)
        {
            if (owner == null) return;

            foreach (KeyValuePair<Unit, List<Claim>> pair in claims)
            {
                List<Claim> list = pair.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Owner == owner) list.RemoveAt(i);
                }
            }
            Prune();
        }

        private static void AddOwner(Aircraft owner, Aircraft except)
        {
            if (owner == null || owner.disabled || owner == except || owners.Contains(owner)) return;
            owners.Add(owner);
        }

        private static void Prune()
        {
            float now = Time.timeSinceLevelLoad;
            staleTargets.Clear();

            foreach (KeyValuePair<Unit, List<Claim>> pair in claims)
            {
                Unit target = pair.Key;
                List<Claim> list = pair.Value;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Claim claim = list[i];
                    if (claim.Owner == null || claim.Owner.disabled || claim.Until <= now)
                        list.RemoveAt(i);
                }

                if (target == null || target.disabled || list.Count == 0)
                    staleTargets.Add(target);
            }

            for (int i = 0; i < staleTargets.Count; i++) claims.Remove(staleTargets[i]);
        }
    }

    /// <summary>
    /// Adds reservation pressure to the stock target search for all locally simulated AI.
    /// The original opportunity/threat calculation remains authoritative; this only breaks
    /// the pathological tie where several pilots independently select the same best target.
    /// A player remains a valid target, but each existing commitment makes another AI choose
    /// a similarly useful unclaimed contact instead of dog-piling the human.
    /// </summary>
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class AiTargetDeconflictionPatch
    {
        private const float ClaimSeconds = 7f;

        [HarmonyPostfix]
        private static void Postfix(Unit searcher, float bravery, List<WeaponStation> stationList,
                                    ref CombatAI.TargetSearchResults __result)
        {
            if (!WingBrain.Deconfliction) return;
            if (!(searcher is Aircraft aircraft) || aircraft.Player != null || !aircraft.LocalSim) return;
            if (aircraft.NetworkHQ == null || stationList == null || stationList.Count == 0) return;

            Unit bestTarget = null;
            WeaponStation bestStation = null;
            float bestScore = 0f;
            float bestOpportunity = 0f;
            int bestCapacity = 1;

            foreach (WeaponStation station in stationList)
            {
                if (station == null || station.Cargo || station.Ammo <= 0 || station.WeaponInfo == null)
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

                    int capacity = Mathf.Clamp(
                        Mathf.CeilToInt(station.WeaponInfo.CalcAttacksNeeded(candidate)), 1, 4);
                    if (candidate is Missile) capacity = 1;

                    int committed = TacticalCoordinator.CountClaims(candidate, aircraft)
                                  + Mathf.Max(tracking.attackers, 0);
                    int excess = Mathf.Max(committed - capacity + 1, 0);

                    float pressure = 1f + excess * WingTuning.TargetSaturationPenalty;
                    score /= pressure;
                    if (score <= bestScore) continue;

                    bestScore = score;
                    bestTarget = candidate;
                    bestStation = station;
                    bestOpportunity = analysis.opportunity;
                    bestCapacity = capacity;
                }
            }

            if (bestTarget == null)
            {
                if (__result.target != null)
                    TacticalCoordinator.TryClaim(__result.target, aircraft, 1, ClaimSeconds);
                return;
            }

            // Preserve the stock bravery escape gate. Deconfliction should change who an AI
            // fights, not make a timid aircraft accept a threat the base game rejected.
            if (bestOpportunity * bravery * 2f < 0.35f &&
                aircraft.NetworkHQ.GetAircraftThreat(bestTarget.persistentID) >
                    bestOpportunity * bravery * 2f &&
                FastMath.Distance(bestTarget.GlobalPosition(), aircraft.GlobalPosition()) >
                    bestStation.WeaponInfo.targetRequirements.maxRange * 2f)
                return;

            __result = new CombatAI.TargetSearchResults(
                bestTarget, bestStation, bestOpportunity, __result.outOfAmmo);
            TacticalCoordinator.TryClaim(bestTarget, aircraft, bestCapacity, ClaimSeconds);
        }
    }
}
