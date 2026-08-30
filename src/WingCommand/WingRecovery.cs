using System;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Finishes a Return To Base order: when the wingman is home and shut down, its airframe
    /// enters the three-slot wing reserve and the aircraft leaves the world.
    ///
    /// Without this an RTB order simply ended. The stock states land the aircraft perfectly
    /// well — <c>AIPilotLandingState</c> hands off to <c>AIPilotTaxiState</c>, which taxis to
    /// a service point, calls <c>StartEjectionSequence</c> and parks; the rotary path in
    /// <c>AIHeloLandingState</c> ends the same way — but nothing credits the airframe back.
    /// <c>FactionHQ.RemoveFactionUnit</c> only drops it from the active-AI list, so a wingman
    /// sent home consumed its airframe exactly as if it had been shot down.
    ///
    /// It read that way too. The ejection at the end of the taxi clears
    /// <see cref="WingMember.Alive"/>, so <see cref="WingRegistry.Prune"/> reported a
    /// successful recovery as "pilot ejected" — a loss. This runs immediately before Prune
    /// so it claims the aircraft first.
    /// </summary>
    internal static class WingRecovery
    {
        /// <summary>Height, in metres, below which the aircraft counts as being on the ground.</summary>
        private const float GroundHeight = 5f;

        /// <summary>Speed, in m/s, below which a landed aircraft counts as stopped.</summary>
        private const float StoppedSpeed = 3f;

        public static void Tick(WingRegistry wing)
        {
            if (wing == null || !Plugin.Config2.RtbReturnsToReserve.Value) return;

            // Iterate backwards: a recovery removes its member from the roster.
            for (int i = wing.Count - 1; i >= 0; i--)
            {
                WingMember member = wing.Members[i];
                if (member == null || member.Order != WingOrder.ReturnToBase) continue;
                if (!IsHome(member)) continue;

                Recover(wing, member);
            }
        }

        /// <summary>
        /// Whether this wingman has finished its recovery.
        ///
        /// Two ways in, because the stock flow can stop at either. A fixed-wing aircraft
        /// brakes to a halt at a service point and only then ejects its pilot; a helicopter
        /// touches down and ejects immediately. Both are "home", and so is an aircraft that
        /// simply came to rest on the apron because the taxi state gave up. What all of them
        /// share is an intact airframe, stationary, on the ground, inside a friendly airbase.
        /// </summary>
        private static bool IsHome(WingMember member)
        {
            Aircraft aircraft = member.Aircraft;
            if (aircraft == null || aircraft.disabled) return false;

            // Only the host may write supply or destroy a network object.
            if (!aircraft.IsServer || !aircraft.LocalSim) return false;
            if (aircraft.radarAlt > GroundHeight) return false;

            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null || !hq.AnyNearAirbase(aircraft.transform.position, out _)) return false;

            if (aircraft.speed <= StoppedSpeed) return true;

            // Still rolling, but nobody is flying it any more: the pilot has climbed out or
            // the aircraft has been handed to the parked state. Either way it is finished.
            Pilot pilot = member.Pilot;
            return pilot != null &&
                   (pilot.ejected || pilot.dead || pilot.currentState is PilotParkedState);
        }

        /// <summary>
        /// Put the airframe in the wing reserve and take the aircraft out of the world.
        ///
        /// An owned requisition remains owned, while an active mission aircraft becomes a
        /// normal held slot. If all three slots are occupied, the frame is returned to the
        /// faction's ordinary stock instead.
        ///
        /// The aircraft is destroyed rather than disabled, for the reason
        /// <see cref="WingTakeover"/> destroys its source aircraft: <c>DisableUnit</c> would
        /// register a kill, a score event and a supply loss for an aircraft that landed
        /// safely.
        /// </summary>
        private static void Recover(WingRegistry wing, WingMember member)
        {
            Aircraft aircraft = member.Aircraft;
            string name = member.Name;
            bool owned = WingShop.TakePurchased(aircraft);

            // Whatever happens below, the member leaves the roster. A recovery that failed
            // half way and stayed on the books would be retried every frame for the rest of
            // the mission.
            WingComms.Say(member, WingComms.Call.Recovered);
            wing.Recover(member);

            try
            {
                FactionHQ hq = aircraft.NetworkHQ;
                bool stored = WingSupplyReserve.StoreRecovered(aircraft.definition, owned);
                if (!stored && hq != null && aircraft.definition != null)
                    hq.AddSupplyUnit(aircraft.definition, 1);

                NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(
                    aircraft.Identity, !aircraft.Identity.IsSceneObject);

                int stock = hq != null && aircraft.definition != null
                    ? hq.GetUnitSupply(aircraft.definition)
                    : 0;

                WingCommandManager.Instance?.Toast(stored
                    ? name + " recovered to wing reserve (" + WingSupplyReserve.Count +
                      "/" + WingSupplyReserve.Capacity + ")"
                    : name + " recovered to faction stock - wing reserve full");
                Plugin.Logger.LogInfo(
                    "[Recovery] " + name + " recovered at base; " +
                    (aircraft.definition != null ? aircraft.definition.unitName : "airframe") +
                    (stored ? " stored in wing reserve" : " faction stock now " + stock));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Recovery] " + name + " could not be returned to stock: " + e);
                WingCommandManager.Instance?.Toast(name + " landed; airframe could not be returned");
            }
        }
    }
}
