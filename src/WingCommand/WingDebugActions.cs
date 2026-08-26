using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Testing aids for formation work. Both are cheats — they move or create aircraft
    /// outright — so they are server-side only and off unless explicitly enabled.
    ///
    /// Both go through the same placement solver: take the leader's position, heading and
    /// velocity, derive each slot from it, then check the result is actually a safe piece
    /// of sky before putting an aircraft there. Spawning and teleporting differ only in
    /// whether the aircraft already exists.
    /// </summary>
    internal static class WingDebugActions
    {
        private const float MinimumLeaderAltitude = 80f;

        /// <summary>Metres of clearance a slot must have above terrain or sea.</summary>
        private const float TerrainClearance = 60f;

        /// <summary>Where a wingman should be, and how it should be moving when it gets there.</summary>
        private struct Placement
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;

            public GlobalPosition Global => Position.ToGlobalPosition();
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// Derive a slot from the leader's current state.
        ///
        /// Attitude is levelled to the leader's heading rather than copied outright: a
        /// wingman appearing mid-barrel-roll has no way to recover, and a level start is
        /// what the formation controller expects to take over from.
        /// </summary>
        private static Placement ComputeSlot(Aircraft leader, int slot, float maxSpeed)
        {
            Vector3 forward = leader.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 offset = FormationSolver.SlotOffset(
                forward, slot,
                Plugin.Config2.Shape.Value,
                Plugin.Config2.SlotSpacing.Value,
                Plugin.Config2.SlotStack.Value);

            Vector3 position = ClearOfGround(leader.transform.position + offset);

            Vector3 velocity = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            if (maxSpeed > 1f && velocity.magnitude > maxSpeed)
                velocity = velocity.normalized * maxSpeed;

            // Point the airframe along its velocity vector, not along the flattened
            // heading. Arriving level while carrying a climbing or diving velocity means
            // arriving at a large angle of attack, and the aerodynamic force that
            // generates in a single physics step is enough to kill the pilot outright.
            // Aligning to velocity inserts the aircraft at zero AoA, which is the only
            // attitude that produces no transient at all.
            Vector3 nose = velocity.sqrMagnitude > 100f ? velocity.normalized : forward;

            return new Placement
            {
                Position = position,
                Rotation = Quaternion.LookRotation(nose, Vector3.up),
                Velocity = velocity,
            };
        }

        /// <summary>
        /// Push a slot up until it has real clearance. Formation offsets are relative to
        /// the leader, so over rising ground a slot can land inside a hillside even though
        /// the leader is comfortably clear of it.
        /// </summary>
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

        // ----------------------------------------------------------------- teleport

        /// <summary>
        /// Snap every current wingman into its slot, matching the leader's heading and
        /// velocity. Turns "does it hold station" into a question answerable in seconds
        /// rather than after a long join-up.
        /// </summary>
        public static void TeleportWingToFormation(WingRegistry wing)
        {
            if (!Guard(wing, out string why))
            {
                Toast(why);
                return;
            }

            Aircraft leader = wing.Leader;
            int moved = 0;

            foreach (WingMember m in wing.Members)
            {
                if (!m.Alive) continue;
                if (Place(m.Aircraft, leader, m.Slot)) moved++;
            }

            Toast(moved > 0
                ? "Teleported " + moved + " into formation"
                : "No wingmen to teleport");
        }

        private static bool Place(Aircraft aircraft, Aircraft leader, int slot)
        {
            if (aircraft == null || aircraft.disabled) return false;

            Rigidbody rb = aircraft.rb;
            if (rb == null) return false;

            Placement p = ComputeSlot(leader, slot, aircraft.GetAircraftParameters().maxSpeed);

            rb.position = p.Position;
            rb.rotation = p.Rotation;
            rb.velocity = p.Velocity;
            rb.angularVelocity = Vector3.zero;

            aircraft.transform.SetPositionAndRotation(p.Position, p.Rotation);

            // Colliders otherwise keep their old pose until the next physics step, which
            // makes the arrival look like an intersection to anything querying them.
            Physics.SyncTransforms();

            SuppressGForceSpike(aircraft, p.Velocity);
            return true;
        }

        // -------------------------------------------------------------------- spawn

        /// <summary>
        /// Spawn a fresh wing of the player's own aircraft type, already in their slots,
        /// and assign them. Fills the wing up to MaxWingSize.
        /// </summary>
        public static void SpawnWingLikePlayer(WingRegistry wing)
        {
            if (!Guard(wing, out string why))
            {
                Toast(why);
                return;
            }

            Aircraft leader = wing.Leader;

            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null)
            {
                Toast("Spawner unavailable");
                return;
            }

            GameObject prefab = leader.definition != null ? leader.definition.unitPrefab : null;
            if (prefab == null)
            {
                Toast("Could not resolve the aircraft prefab");
                return;
            }

            int want = Plugin.Config2.MaxWingSize.Value - wing.Count;
            if (want <= 0)
            {
                Toast("Wing is already full");
                return;
            }

            float maxSpeed = leader.GetAircraftParameters().maxSpeed;
            int spawned = 0;

            // Slots must be numbered as we go. Assignment is deferred by a frame, so
            // wing.Count does not move during this loop — reading it each time put every
            // aircraft in slot 1, stacked on top of each other.
            int nextSlot = wing.Count + 1;

            for (int i = 0; i < want; i++, nextSlot++)
            {
                Placement p = ComputeSlot(leader, nextSlot, maxSpeed);

                try
                {
                    Aircraft spawnedAircraft = spawner.SpawnAircraft(
                        player: null,
                        prefab: prefab,
                        loadout: leader.loadout,
                        fuelLevel: 1f,
                        livery: leader.NetworkLiveryKey,
                        globalPosition: p.Global,
                        rotation: p.Rotation,
                        startingVel: p.Velocity,
                        spawningHangar: null,
                        HQ: leader.NetworkHQ,
                        uniqueName: "WingCommand_Debug_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        skill: leader.skill,
                        bravery: leader.bravery);

                    if (spawnedAircraft == null) break;

                    // The pilot state machine is built during the aircraft's own
                    // initialisation, so assignment waits until the next frame.
                    WingCommandManager.Instance?.QueueRecruit(spawnedAircraft);
                    spawned++;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("Debug spawn failed: " + e);
                    break;
                }
            }

            Toast(spawned > 0
                ? "Spawned " + spawned + " " + leader.definition.unitName + " in formation"
                : "Spawn failed - see the BepInEx log");
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// Stop a placement from registering as a lethal G load.
        ///
        /// <c>Pilot_OnAeroInputsApplied</c> derives G from the frame-to-frame velocity
        /// delta and squares anything over 20 g into <c>TakeGForceDamage</c>. Handing a
        /// wingman a new velocity is a step change, so without this a large speed
        /// difference reads as hundreds of g. Freshly spawned aircraft are unaffected
        /// because their <c>velocityPrev</c> starts at zero, which the stock code treats as
        /// "no reading yet"; this puts a teleported aircraft in the same position.
        /// </summary>
        private static void SuppressGForceSpike(Aircraft aircraft, Vector3 velocity)
        {
            aircraft.velocityPrev = velocity;

            if (aircraft.pilots == null) return;
            foreach (Pilot pilot in aircraft.pilots)
            {
                if (pilot == null) continue;
                pilot.velocityPrev = velocity;
                pilot.accel = Vector3.zero;
                pilot.gForce = 0f;
            }
        }

        private static bool Guard(WingRegistry wing, out string why)
        {
            why = null;

            if (!Plugin.Config2.EnableDebugActions.Value)
            {
                why = "Debug actions are disabled in config";
                return false;
            }

            if (wing == null || wing.Leader == null)
            {
                why = "Not flying";
                return false;
            }

            // Both actions write world state, which only the server may do.
            if (!wing.Leader.IsServer)
            {
                why = "Host or single-player only";
                return false;
            }

            // Slots are relative to the leader, so a leader on the ground puts wingmen at
            // ground level with no room to recover.
            if (wing.Leader.radarAlt < MinimumLeaderAltitude)
            {
                why = "Climb above " + MinimumLeaderAltitude + " m first";
                return false;
            }

            return true;
        }

        private static void Toast(string message)
        {
            WingCommandManager.Instance?.Toast(message);
            if (Plugin.Config2.VerboseLogging.Value)
                Plugin.Logger.LogInfo("[Debug] " + message);
        }
    }
}
