using System;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Where a purchased aircraft appears, and the spawn call itself.
    ///
    /// Both delivery options use the same call and differ only in placement, which is what
    /// makes the price difference meaningful rather than cosmetic: base delivery puts the
    /// aircraft over its airbase and it has to fly to you, fast delivery puts it on your
    /// wing immediately.
    /// </summary>
    internal static class WingShopDelivery
    {
        /// <summary>Height above the airbase that a base delivery joins the circuit at.</summary>
        private const float CircuitAltitude = 1200f;

        /// <summary>Metres of clearance kept above terrain and sea.</summary>
        private const float TerrainClearance = 120f;

        public static Aircraft Spawn(AircraftDefinition definition, Aircraft leader,
                                     FactionHQ hq, WingShop.Delivery mode)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null)
            {
                Plugin.Logger.LogWarning("[Shop] spawner unavailable");
                return null;
            }

            GameObject prefab = definition.unitPrefab;
            if (prefab == null)
            {
                Plugin.Logger.LogWarning("[Shop] no prefab for " + definition.unitName);
                return null;
            }

            Vector3 position;
            Quaternion rotation;
            Vector3 velocity;

            if (mode == WingShop.Delivery.Fast)
                FastPlacement(definition, leader, out position, out rotation, out velocity);
            else if (!BasePlacement(definition, leader, hq, out position, out rotation, out velocity))
                return null;

            // The airframe's own standard loadout and livery, which is what "default
            // equipment" means and what the faction's own AI aircraft launch with.
            AircraftParameters p = definition.aircraftParameters;
            float fuel = p != null ? p.DefaultFuelLevel : 1f;

            LiveryKey livery = leader.NetworkLiveryKey;
            if (p != null && hq != null && hq.faction != null)
                livery = new LiveryKey(p.GetRandomLiveryForFaction(hq.faction));

            try
            {
                return spawner.SpawnAircraft(
                    player: null,
                    prefab: prefab,
                    // Null, never a shared Loadout instance. Aircraft initialisation
                    // substitutes the airframe's own standard loadout when this is null,
                    // whereas handing over an existing object shares one mutable loadout
                    // between aircraft - which once left a whole spawned wing with no
                    // ammunition and sent all of them straight home Winchester.
                    loadout: null,
                    fuelLevel: fuel,
                    livery: livery,
                    globalPosition: position.ToGlobalPosition(),
                    rotation: rotation,
                    startingVel: velocity,
                    spawningHangar: null,
                    HQ: hq,
                    uniqueName: "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    skill: leader.skill,
                    bravery: leader.bravery);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("[Shop] spawn failed: " + e);
                return null;
            }
        }

        /// <summary>
        /// Fast delivery: on the leader's wing, matching its velocity.
        ///
        /// Placed behind rather than beside, so it arrives in clear air whatever the
        /// formation shape is doing, and aligned to the velocity vector rather than to the
        /// flattened heading — arriving level while carrying a climbing or diving velocity
        /// means arriving at a large angle of attack, and the force that generates in one
        /// physics step is enough to kill the pilot outright.
        /// </summary>
        private static void FastPlacement(AircraftDefinition definition, Aircraft leader,
                                          out Vector3 position, out Quaternion rotation,
                                          out Vector3 velocity)
        {
            Vector3 forward = leader.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float back = Plugin.Config2.FastDeliveryDistance.Value;
            position = ClearOfGround(leader.transform.position - forward * back);

            velocity = leader.rb != null ? leader.rb.velocity : forward * 100f;

            float maxSpeed = definition.aircraftParameters != null
                ? definition.aircraftParameters.maxSpeed
                : 0f;
            if (maxSpeed > 1f && velocity.magnitude > maxSpeed)
                velocity = velocity.normalized * maxSpeed;

            Vector3 nose = velocity.sqrMagnitude > 100f ? velocity.normalized : forward;
            rotation = Quaternion.LookRotation(nose, Vector3.up);
        }

        /// <summary>
        /// Base delivery: in the circuit over the nearest friendly airbase, pointed at the
        /// player. It is recruited immediately and flies to its slot under its own power,
        /// which is the whole trade against fast delivery.
        /// </summary>
        private static bool BasePlacement(AircraftDefinition definition, Aircraft leader,
                                          FactionHQ hq, out Vector3 position,
                                          out Quaternion rotation, out Vector3 velocity)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;

            Airbase airbase = hq != null ? hq.GetNearestAirbase(leader.transform.position) : null;
            if (airbase == null)
            {
                Plugin.Logger.LogWarning("[Shop] no friendly airbase for base delivery");
                return false;
            }

            Vector3 field = airbase.transform.position;

            Vector3 toLeader = leader.transform.position - field;
            toLeader.y = 0f;
            if (toLeader.sqrMagnitude < 1f) toLeader = leader.transform.forward;
            toLeader.Normalize();

            position = ClearOfGround(field + Vector3.up * CircuitAltitude);
            rotation = Quaternion.LookRotation(toLeader, Vector3.up);

            AircraftParameters p = definition.aircraftParameters;
            float cruise = p != null ? Mathf.Max(p.landingSpeed * 1.6f, 80f) : 120f;
            velocity = toLeader * cruise;

            return true;
        }

        /// <summary>Keep the spawn point clear of terrain and sea.</summary>
        private static Vector3 ClearOfGround(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 3000f, Vector3.down,
                                out RaycastHit hit, 6000f, PhysicsLayers.StaticsMask))
            {
                position.y = Mathf.Max(position.y, hit.point.y + TerrainClearance);
            }

            position.y = Mathf.Max(position.y, Datum.LocalSeaY + TerrainClearance);
            return position;
        }
    }
}
