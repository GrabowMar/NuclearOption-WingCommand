using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Server-only debug spawn of a selected aircraft into verified formation slots, gated
    /// by configuration and safe placement checks.</summary>
    internal static class WingDebugActions
    {
        private const float MinimumLeaderAltitude = 80f;

        /// <summary>Required terrain or sea clearance in metres.</summary>
        private const float TerrainClearance = 60f;

        /// <summary>Spawn position, attitude, and velocity for a wing slot.</summary>
        private struct Placement
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;

            public GlobalPosition Global => Position.ToGlobalPosition();
        }

        // Debug placement.

        /// <summary>Derive a slot from current leader motion, removing leader roll and aligning the new
        /// aircraft with its velocity for safe entry.</summary>
        private static Placement ComputeSlot(Aircraft leader, int slot, float maxSpeed)
        {
            Vector3 forward = leader.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 offset = FormationSolver.SlotOffset(
                forward, slot,
                WingFormation.Shape,
                WingFormation.SlotSpacing,
                WingTuning.SlotStack);

            Vector3 position = ClearOfGround(leader.transform.position + offset);

            Vector3 velocity = leader.rb != null ? leader.rb.velocity : Vector3.zero;
            if (maxSpeed > 1f && velocity.magnitude > maxSpeed)
                velocity = velocity.normalized * maxSpeed;

            // Align the nose with spawn velocity to avoid a large angle-of-attack impulse during the
            // first physics step.
            Vector3 nose = velocity.sqrMagnitude > 100f ? velocity.normalized : forward;

            return new Placement
            {
                Position = position,
                Rotation = Quaternion.LookRotation(nose, Vector3.up),
                Velocity = velocity,
            };
        }

        /// <summary>Raise slot placement above terrain beneath it; leader clearance alone does not protect
        /// lower hillside slots.</summary>
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

        // Debug spawning.

        /// <summary>Draw the shared Debug-category warning once above its controls.</summary>
        public static void DrawWarning(ConfigEntryBase entry)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.2f);
            GUILayout.Label(
                "(PROBABLY BREAKS MOD)  Everything below is a cheat: unbalanced, barely " +
                "tested, and liable to break mission or mod progression.",
                GUILayout.ExpandWidth(true));
            GUI.color = previous;
        }

        /// <summary>Draw the spawn action in ConfigurationManager. SpawnWingLikePlayer validates and
        /// reports failed preconditions so the button always explains refusal.</summary>
        public static void DrawSpawnButton(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Spawn debug wing", GUILayout.ExpandWidth(true)))
                SpawnWingLikePlayer(WingCommandManager.Instance?.Wing);
        }

        public static void DrawAircraftSelector(ConfigEntryBase entry)
        {
            var setting = (ConfigEntry<string>)entry;
            GUILayout.BeginVertical();
            GUILayout.Label(string.IsNullOrEmpty(setting.Value)
                ? "Current aircraft" : setting.Value);
            if (GUILayout.Button("Use my aircraft")) setting.Value = "";
            var aircraft = Encyclopedia.i?.aircraft;
            if (aircraft != null)
            {
                var choices = new List<AircraftDefinition>();
                foreach (AircraftDefinition definition in aircraft)
                    if (definition != null && EconomyFacade.Shop.IsFlyableAircraft(definition) &&
                        EconomyFacade.Shop.MatchesLeader(definition)) choices.Add(definition);
                choices.Sort((a, b) => string.Compare(a.unitName, b.unitName,
                    StringComparison.OrdinalIgnoreCase));
                foreach (AircraftDefinition definition in choices)
                    if (GUILayout.Button(definition.unitName)) setting.Value = definition.unitName;
            }
            GUILayout.EndVertical();
        }

        /// <summary>Fill the wing to MaxWingSize with new copies of the player's airframe in formation
        /// slots.</summary>
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

            AircraftDefinition definition = leader.definition;
            string selected = Plugin.Settings.DebugSpawnAircraft.Value;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                definition = null;
                var catalogue = Encyclopedia.i?.aircraft;
                if (catalogue != null)
                    foreach (AircraftDefinition candidate in catalogue)
                        if (candidate != null && string.Equals(candidate.unitName, selected,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            definition = candidate;
                            break;
                        }
                if (definition == null || !EconomyFacade.Shop.IsFlyableAircraft(definition) ||
                    !EconomyFacade.Shop.MatchesLeader(definition))
                {
                    Toast("Selected debug aircraft is unavailable or incompatible with your aircraft");
                    return;
                }
            }

            GameObject prefab = definition != null ? definition.unitPrefab : null;
            if (prefab == null)
            {
                Toast("Could not resolve the aircraft prefab");
                return;
            }

            int want = WingFormation.MaxWingSize - wing.Count;
            if (want <= 0)
            {
                Toast("Wing is already full");
                return;
            }

            Aircraft template = prefab.GetComponent<Aircraft>();
            if (template == null)
            {
                Toast("Selected prefab has no aircraft component");
                return;
            }
            float maxSpeed = template.GetAircraftParameters().maxSpeed;
            int spawned = 0;

            for (int slot = 1; slot <= WingFormation.MaxWingSize && wing.Count < WingFormation.MaxWingSize; slot++)
            {
                if (wing.SlotTaken(slot)) continue;

                Placement p = ComputeSlot(leader, slot, maxSpeed);

                try
                {
                    Aircraft spawnedAircraft = spawner.SpawnAircraft(
                        player: null,
                        prefab: prefab,
                        // Build a separate mutable Loadout for each aircraft; shared containers can
                        // lose ammunition during initialisation.
                        loadout: EconomyFacade.LoadoutCatalog.Build(
                            definition, WingLoadoutChoice.Standard),
                        fuelLevel: 1f,
                        livery: definition == leader.definition ? leader.NetworkLiveryKey : default,
                        globalPosition: p.Global,
                        rotation: p.Rotation,
                        startingVel: p.Velocity,
                        spawningHangar: null,
                        HQ: leader.NetworkHQ,
                        uniqueName: "WingCommand_Debug_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        skill: leader.skill,
                        bravery: leader.bravery);

                    if (spawnedAircraft == null) break;

                    // Recruit airborne debug spawns directly; the hangar queue would incorrectly impose
                    // pending-delivery hold.
                    WingMember member = wing.Add(spawnedAircraft);
                    if (member != null) spawned++;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("Debug spawn failed: " + e);
                    break;
                }
            }

            Toast(spawned > 0
                ? "Spawned " + spawned + " " + definition.unitName + " in formation"
                : "Spawn failed - see the BepInEx log");
        }

        // Debug guards.

        private static bool Guard(WingRegistry wing, out string why)
        {
            why = null;

            if (!Plugin.Settings.EnableDebugActions.Value)
            {
                why = "Debug actions are disabled in config";
                return false;
            }

            if (wing == null || wing.Leader == null)
            {
                why = "Not flying";
                return false;
            }

            // Require server authority for world-state changes.
            if (!wing.Leader.IsServer)
            {
                why = "Host or single-player only";
                return false;
            }

            // Require leader altitude so relative slots have room for safe entry.
            if (wing.Leader.radarAlt < MinimumLeaderAltitude)
            {
                why = "Climb above " + MinimumLeaderAltitude + " m first";
                return false;
            }

            return true;
        }

        private static void Toast(string message)
        {
            WingCommandManager.Instance?.DebugToast(message);
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.LogVerbose("[Debug] " + message);
        }
    }
}
