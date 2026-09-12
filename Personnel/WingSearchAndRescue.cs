using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Bridge native dismounted-pilot outcomes to squadron records. Never revive a native casualty.</summary>
    internal static class WingSearchAndRescue
    {
        public const float LocalRecoveryCost = 10_000_000f;
        public const float LocalRecoveryDuration = 300f;

        private sealed class Survivor
        {
            public WingPilot Pilot;
            public PilotDismounted Native;
            public Aircraft PendingAircraft;
            public float PendingUntil;
            public bool EscapeRolled;
            public bool EscapeCompleting;
            public float EscapeAt = float.PositiveInfinity;
            public bool LocalRecoveryCompleting;
            public float LocalRecoveryAt = float.PositiveInfinity;
        }

        private static readonly Dictionary<PersistentID, Survivor> survivors = new Dictionary<PersistentID, Survivor>();
        private static float nextTick;

        public static void Reset() { survivors.Clear(); nextTick = 0f; }
        public static void Forget(PersistentID id) => survivors.Remove(id);

        internal static WingPilot PilotOf(PilotDismounted native) =>
            native != null && native.pilotNumber == 0 &&
            survivors.TryGetValue(native.parentUnit, out Survivor survivor) && survivor.Native == native
                ? survivor.Pilot : null;

        internal static void CollectDowned(List<Unit> into)
        {
            foreach (Survivor survivor in survivors.Values)
                if (survivor.Native != null && !survivor.Native.disabled &&
                    survivor.Pilot.RecoveryStatus == PilotRecoveryStatus.Downed)
                    into.Add(survivor.Native);
        }

        internal static void Track(PilotDismounted native)
        {
            if (native == null || !native.IsServer || native.pilotNumber != 0) return;
            if (survivors.TryGetValue(native.parentUnit, out Survivor pending))
            {
                if (pending.Native != null || pending.Pilot.Lost) return;
                pending.Native = native;
                pending.PendingAircraft = null;
                pending.Pilot.RecoveryStatus = PilotRecoveryStatus.Downed;
                WingCommandManager.Instance?.Toast(pending.Pilot.Callsign + " ejected alive — SAR needed");
                return;
            }
            WingPilot pilot = WingPilotRoster.Of(native.parentUnit);
            if (pilot == null || pilot.Lost || survivors.ContainsKey(native.parentUnit)) return;
            survivors.Add(native.parentUnit, new Survivor { Pilot = pilot, Native = native });
        }

        internal static bool MarkDowned(PersistentID id, WingPilot pilot)
        {
            if (!survivors.TryGetValue(id, out Survivor survivor))
            {
                // A disabled aircraft can spawn its living crew after the native ejection delay.
                // Hold an unresolved record briefly instead of declaring that crew KIA immediately.
                if (!UnitRegistry.TryGetUnit(id, out Unit unit) || !(unit is Aircraft aircraft) ||
                    !aircraft.IsServer) return false;
                Pilot seated = WingRegistry.PrimaryPilot(aircraft);
                if (seated == null || seated.dead) return false;
                survivors.Add(id, new Survivor {
                    Pilot = pilot, PendingAircraft = aircraft,
                    PendingUntil = Time.timeSinceLevelLoad + 30f
                });
                pilot.RecoveryStatus = PilotRecoveryStatus.Missing;
                WingCommandManager.Instance?.Toast(pilot.Callsign + " — checking ejection, pilot unavailable");
                return true;
            }
            if (survivor.Pilot != pilot ||
                survivor.Native == null || survivor.Native.animationState == PilotDismounted.PilotState.dead) return false;
            pilot.RecoveryStatus = PilotRecoveryStatus.Downed;
            WingCommandManager.Instance?.Toast(pilot.Callsign + " ejected alive — SAR needed");
            return true;
        }

        private static void Settle(PersistentID id, PilotRecoveryStatus status, bool killed, string message)
        {
            if (!survivors.TryGetValue(id, out Survivor survivor)) return;
            survivors.Remove(id);
            if (killed) survivor.Pilot.LossCause = "Killed after ejection";
            WingPilotRoster.SettleRescue(id, survivor.Pilot, status, killed, message);
        }

        private static void AwardRescueBounty(PersistentID id, WingPilot pilot)
        {
            float value = EconomyFacade.Shop.PaidFor(id);
            if (value <= 0f && UnitRegistry.TryGetUnit(id, out Unit u) && u is Aircraft a && a.definition != null)
                value = EconomyFacade.Shop.CurrentPriceOf(a.definition);
            if (value <= 0f) value = 500f;

            float bounty = Mathf.Round(value * 0.5f);
            if (bounty > 0f && GameManager.GetLocalPlayer(out Player player) && player != null)
            {
                player.AddAllocation(bounty);
                string call = pilot != null && !string.IsNullOrWhiteSpace(pilot.Callsign) ? pilot.Callsign : "Pilot";
                WingCommandManager.Instance?.Toast($"CSAR: {call} rescued! +${bounty:F0} bounty awarded");
            }
        }

        internal static void Observe(PilotDismounted native)
        {
            if (native == null || !native.IsServer || native.pilotNumber != 0) return;
            if (native.animationState == PilotDismounted.PilotState.dead)
                Settle(native.parentUnit, PilotRecoveryStatus.None, true, "KIA after ejection");
            else if (native.unitState == Unit.UnitState.Returned)
            {
                bool escaped = survivors.TryGetValue(native.parentUnit, out Survivor survivor) && survivor.EscapeCompleting;
                bool local = survivors.TryGetValue(native.parentUnit, out survivor) && survivor.LocalRecoveryCompleting;
                if (!escaped && !local && survivors.TryGetValue(native.parentUnit, out Survivor s))
                    AwardRescueBounty(native.parentUnit, s.Pilot);
                Settle(native.parentUnit, PilotRecoveryStatus.None, false,
                    escaped ? "independent escape — returned to pilot pool" :
                    local ? "local recovery complete — returned to pilot pool" :
                    "rescued — returned to pilot pool");
            }
        }

        internal static void Captured(PilotDismounted native, Unit captor)
        {
            if (ReferenceEquals(native, null) || !native.IsServer || captor == null || native.pilotNumber != 0) return;
            if (native.animationState == PilotDismounted.PilotState.dead) return;
            bool friendly = native.NetworkHQ != null && captor.NetworkHQ == native.NetworkHQ;
            if (friendly && survivors.TryGetValue(native.parentUnit, out Survivor survivor))
            {
                AwardRescueBounty(native.parentUnit, survivor.Pilot);
            }
            Settle(native.parentUnit, friendly ? PilotRecoveryStatus.None : PilotRecoveryStatus.Captured,
                false, friendly ? "rescued — returned to pilot pool" : "captured — unavailable");
        }

        public static void Tick()
        {
            if (survivors.Count == 0 || Time.timeSinceLevelLoad < nextTick) return;
            nextTick = Time.timeSinceLevelLoad + 0.5f;
            // Native callbacks can settle records while this pass is running.
            foreach (PersistentID id in new List<PersistentID>(survivors.Keys))
            {
                if (!survivors.TryGetValue(id, out Survivor survivor)) continue;
                PilotDismounted native = survivor.Native;
                if (native == null)
                {
                    if (survivor.PendingAircraft != null)
                    {
                        Pilot seated = WingRegistry.PrimaryPilot(survivor.PendingAircraft);
                        if (seated != null && seated.dead)
                        {
                            Settle(id, PilotRecoveryStatus.None, true, "KIA before ejection");
                            continue;
                        }
                        if (Time.timeSinceLevelLoad < survivor.PendingUntil) continue;
                    }
                    if (Time.timeSinceLevelLoad >= survivor.LocalRecoveryAt)
                    {
                        Settle(id, PilotRecoveryStatus.None, false,
                            "local search complete — returned to pilot pool");
                        continue;
                    }
                    // Keep the record for local searches and delayed native ejection callbacks.
                    if (!float.IsPositiveInfinity(survivor.PendingUntil))
                    {
                        survivor.PendingAircraft = null;
                        survivor.PendingUntil = float.PositiveInfinity;
                        WingPilotRoster.SettleRescue(id, survivor.Pilot, PilotRecoveryStatus.Missing,
                            false, "MIA — survivor signal lost; local SAR available");
                    }
                    continue;
                }
                if (!native.IsServer) continue;
                Observe(native);
                if (!survivors.ContainsKey(id) || native.disabled) continue;
                if (Time.timeSinceLevelLoad >= survivor.LocalRecoveryAt)
                {
                    survivor.LocalRecoveryCompleting = true;
                    native.NetworkunitState = Unit.UnitState.Returned;
                    native.Networkdisabled = true;
                    Object.Destroy(native.gameObject);
                    Settle(id, PilotRecoveryStatus.None, false,
                        "local recovery complete — returned to pilot pool");
                    continue;
                }
                bool landed = native.animationState == PilotDismounted.PilotState.landing &&
                              native.radarAlt <= 3f && native.speed < 3f && !native.IsSlung();
                if (!landed) continue;
                if (!survivor.EscapeRolled)
                {
                    // Roll once, including failure; gaining/toggling a perk cannot reroll this ejection.
                    survivor.EscapeRolled = true;
                    float chance = PilotPerks.EscapeChance(
                        WingSurvivalPerks.Has(survivor.Pilot, PilotPerk.Commando));
                    if (chance > 0f && Random.value < chance)
                        survivor.EscapeAt = Time.timeSinceLevelLoad + PilotPerks.EscapeDelay;
                }
                if (Time.timeSinceLevelLoad < survivor.EscapeAt ||
                    !WingSurvivalPerks.Has(survivor.Pilot, PilotPerk.Commando)) continue;
                // Use the native returned state, which also synchronizes the survivor's disappearance.
                survivor.EscapeCompleting = true;
                native.NetworkunitState = Unit.UnitState.Returned;
                native.Networkdisabled = true;
                Object.Destroy(native.gameObject);
                Settle(id, PilotRecoveryStatus.None, false, "independent escape — returned to pilot pool");
            }
        }

        public static string Status(WingPilot pilot)
        {
            if (pilot == null) return "";
            if (pilot.Lost) return "KIA";
            float localRemaining = LocalRecoveryRemaining(pilot);
            if (localRemaining >= 0f)
                return "LOCAL SAR — " + FormatTime(localRemaining) + " REMAINING";
            switch (pilot.RecoveryStatus)
            {
                case PilotRecoveryStatus.Downed:
                    return "DOWNED — AWAITING SAR";
                case PilotRecoveryStatus.Captured: return "CAPTURED";
                case PilotRecoveryStatus.Missing: return "MIA";
                default: return "AVAILABLE";
            }
        }

        public static float LocalRecoveryRemaining(WingPilot pilot)
        {
            foreach (Survivor survivor in survivors.Values)
                if (survivor.Pilot == pilot && !float.IsPositiveInfinity(survivor.LocalRecoveryAt))
                    return Mathf.Max(0f, survivor.LocalRecoveryAt - Time.timeSinceLevelLoad);
            return -1f;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        public static bool OrganizeLocalRecovery(WingPilot pilot)
        {
            Survivor survivor = null;
            foreach (Survivor candidate in survivors.Values)
                if (candidate.Pilot == pilot) { survivor = candidate; break; }

            PilotDismounted native = survivor?.Native;
            if (pilot == null || pilot.Lost || survivor == null ||
                (pilot.RecoveryStatus != PilotRecoveryStatus.Downed &&
                 pilot.RecoveryStatus != PilotRecoveryStatus.Missing) ||
                (native != null && (native.disabled || !native.IsServer ||
                    native.animationState == PilotDismounted.PilotState.dead)) ||
                (native == null && pilot.RecoveryStatus != PilotRecoveryStatus.Missing) ||
                !float.IsPositiveInfinity(survivor.LocalRecoveryAt)) return false;

            if (!GameManager.GetLocalPlayer(out Player player) || player == null)
            {
                WingCommandManager.Instance?.Toast("LOCAL SAR: no player funds available");
                return false;
            }
            if (player.Allocation < LocalRecoveryCost)
            {
                WingCommandManager.Instance?.Toast("LOCAL SAR requires 10,000,000 funds");
                return false;
            }

            player.AddAllocation(-LocalRecoveryCost);
            survivor.LocalRecoveryAt = Time.timeSinceLevelLoad + LocalRecoveryDuration;
            WingCommandManager.Instance?.Toast("LOCAL SAR organized for " + pilot.Callsign + " — ETA 05:00");
            return true;
        }

        /// <summary>Explicit player dispatch only. Use native capture after a safe land-in-place approach.</summary>
        public static void Dispatch(WingPilot pilot, WingRegistry wing)
        {
            Survivor survivor = null;
            foreach (Survivor candidate in survivors.Values)
                if (candidate.Pilot == pilot) { survivor = candidate; break; }
            PilotDismounted native = survivor?.Native;
            if (native == null || native.disabled || !native.IsServer || wing == null ||
                pilot.RecoveryStatus != PilotRecoveryStatus.Downed) return;
            if (native.animationState != PilotDismounted.PilotState.landing || native.IsSlung() ||
                native.transform.position.y <= Datum.LocalSeaY + 2f)
            {
                WingCommandManager.Instance?.Toast("SAR: wait for a land touchdown; use the native helicopter hoist for water rescue");
                return;
            }
            WingMember best = null;
            float distance = float.PositiveInfinity;
            foreach (WingMember member in wing.Members)
            {
                Aircraft aircraft = member.Aircraft;
                if (!member.IsCommandable || member.IsPanicking || aircraft == null ||
                    !aircraft.IsServer || !aircraft.LocalSim || aircraft.NetworkHQ != native.NetworkHQ ||
                    !WingRegistry.IsRotary(aircraft) || aircraft.definition == null ||
                    aircraft.definition.captureCapacity <= 0 || member.Fuel <= 0.25f ||
                    (member.Order != WingOrder.Formation && member.Order != WingOrder.OrbitHere)) continue;
                float candidateDistance = (aircraft.GlobalPosition() - native.GlobalPosition()).sqrMagnitude;
                if (candidateDistance >= distance) continue;
                distance = candidateDistance;
                best = member;
            }
            if (best == null)
            {
                WingCommandManager.Instance?.Toast("SAR needs a friendly idle helicopter with capture capacity and over 25% fuel");
                return;
            }
            best.Apply(WingDirective.AtPoint(WingOrder.LandHere, native.GlobalPosition()));
            WingCommandManager.Instance?.Toast(best.Name + " dispatched to " + pilot.Callsign + " — native rescue after landing");
        }
    }

#pragma warning disable IDE0051
    [HarmonyPatch(typeof(PilotDismounted), "OnStartServer")]
    internal static class WingSurvivorSpawnPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PilotDismounted __instance) => WingSearchAndRescue.Track(__instance);
    }

    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.UnitDisabled))]
    internal static class WingSurvivorReturnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PilotDismounted __instance) => WingSearchAndRescue.Observe(__instance);
    }

    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.SetPilotState))]
    internal static class WingSurvivorDeathPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PilotDismounted __instance) => WingSearchAndRescue.Observe(__instance);
    }

    [HarmonyPatch(typeof(PilotDismounted), nameof(PilotDismounted.Capture))]
    internal static class WingSurvivorCapturePatch
    {
        [HarmonyPostfix]
        private static void Postfix(PilotDismounted __instance, Unit capturingUnit) =>
            WingSearchAndRescue.Captured(__instance, capturingUnit);
    }
}
