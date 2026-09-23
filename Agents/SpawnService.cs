using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Air-starts wingmen (spec §8, M2 §7), host only. Any class but VTOL (the game gives a VTOL no AI state).
    /// <list type="bullet">
    /// <item>Jets and tiltwings: 2 km behind and 150 m below the leader, spread toward each slot's side, at least
    /// 300 m above the terrain, flying the leader's velocity but never slower than 1.5x their own loaded minimum.</item>
    /// <item>Helicopters: 1 km behind and 50 m above the leader (at least 150 m over the terrain), level, at the
    /// leader's speed up to their cruise.</item>
    /// <item>The game initialises each spawn with its own AI. One physics tick after that AI state is entered,
    /// the aircraft is adopted by the wing.</item>
    /// </list></summary>
    internal sealed class SpawnService : IWingService
    {
        public static float MinLeaderAgl = 300f;

        public static SpawnService Instance { get; private set; }

        public string Name => "Spawn";
        public int Pending => pending.Count;

        private readonly List<Aircraft> pending = new List<Aircraft>();
        private readonly HashSet<Aircraft> settling = new HashSet<Aircraft>();
        private readonly Dictionary<Aircraft, float> pendingSince = new Dictionary<Aircraft, float>();
        public static float AdoptTimeoutSeconds = 10f, FallbackRotaryCruise = 60f;

        public SpawnService() => Instance = this;

        public void Activate() => Clear();

        public void Deactivate() => Clear();

        public void Tick(float dt)
        {
        }

        public void FixedTick(float dt)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Aircraft a = pending[i];
                if (a == null || a.disabled)
                {
                    pending.RemoveAt(i);
                    pendingSince.Remove(a);
                    continue;
                }
                Pilot p = a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
                if (p == null || p.currentState == null)
                {
                    // The game has not initialised it yet; give up (it keeps whatever the game does) after a timeout.
                    if (pendingSince.TryGetValue(a, out float since) && Time.time - since > AdoptTimeoutSeconds)
                    {
                        Plugin.Logger.LogWarning($"[Spawn] {a.definition.unitName} never initialised its AI; not adopted");
                        pending.RemoveAt(i);
                        pendingSince.Remove(a);
                    }
                    continue;
                }
                if (settling.Add(a)) continue;                       // wait one more physics tick
                settling.Remove(a);
                pending.RemoveAt(i);
                pendingSince.Remove(a);
                if (WingService.Instance == null || !WingService.Instance.Adopt(a))
                    Plugin.Logger.LogWarning($"[Spawn] {a.definition.unitName} could not join the wing and keeps the game's AI");
            }
        }

        /// <summary>Air-start up to <paramref name="n"/> wingmen of <paramref name="definition"/> (null: the player's
        /// type, or the leader's when there is no player aircraft). Returns how many were spawned.</summary>
        public int Call(int n, AircraftDefinition definition)
        {
            WingService wing = WingService.Instance;
            // Around the aircraft the wing forms on, or the player while it escorts a vehicle or a ship. Unity-null
            // aware: a destroyed leader falls back to the player.
            Aircraft player = wing?.Player;
            Aircraft leader = wing?.Leader;
            if (leader == null) leader = player;
            // The player calls: their altitude gates the call and their type is the default.
            Aircraft caller = player != null ? player : leader;
            if (wing?.Selection == null || leader == null)
            {
                WingToast.Show("Not flying");
                return 0;
            }
            if (!leader.IsServer)
            {
                WingToast.Show("Only the host can call wingmen");
                return 0;
            }
            if (caller.radarAlt < MinLeaderAgl)
            {
                WingToast.Show($"Climb above {MinLeaderAgl:0} m first");
                return 0;
            }
            definition = definition ?? caller.definition;
            GameObject prefab = definition != null ? definition.unitPrefab : null;
            Aircraft template = prefab != null ? prefab.GetComponent<Aircraft>() : null;
            if (template == null || template.pilots == null || template.pilots.Length == 0 || template.pilots[0] == null ||
                template.pilots[0].pilotType == Pilot.PilotType.VTOL)
            {
                // A VTOL would never even get an AI state to adopt.
                WingToast.Show("VTOL aircraft cannot fly formation");
                return 0;
            }
            bool rotary = template.pilots[0].pilotType == Pilot.PilotType.Helo;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null)
            {
                WingToast.Show("Spawner unavailable");
                return 0;
            }
            int room = WingService.MaxMembers - wing.Members.Count - pending.Count;
            n = Math.Min(n, room);
            if (n <= 0)
            {
                WingToast.Show("Wing is full");
                return 0;
            }

            Vec3 lp = leader.GlobalPosition().ToVec3();
            Vec3 lv = leader.CockpitRB().velocity.ToVec3();
            Vec3 v0 = rotary ? AirStart.RotaryVelocity(lv, RotaryCruise(template)) : AirStart.Velocity(lv, LoadedMinimum(definition, template));
            Quaternion rotation = Quaternion.LookRotation((rotary ? AirStart.Heading(lv) : AirStart.Direction(v0)).ToUnity());
            LiveryKey livery = definition == leader.definition ? leader.NetworkLiveryKey : default;
            int spawned = 0;
            for (int k = 0; k < n; k++)
            {
                int slot = wing.Members.Count + pending.Count;
                float right = SlotSolver.SlotFor(wing.Selection.Current, slot).Right;
                Vec3 probe = rotary ? AirStart.RotaryPosition(lp, lv, right, slot, 0f) : AirStart.ForSlot(wing.Selection.Current, slot, lp, lv, 0f);
                float ground = TerrainProbe.GroundY(probe);
                Vec3 pos = rotary ? AirStart.RotaryPosition(lp, lv, right, slot, ground) : AirStart.ForSlot(wing.Selection.Current, slot, lp, lv, ground);
                try
                {
                    Aircraft a = spawner.SpawnAircraft(null, prefab, null, 1f, livery, pos.ToGlobal(), rotation, v0.ToUnity(),
                        null, leader.NetworkHQ, "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        leader.skill, leader.bravery);
                    if (a == null) break;
                    pending.Add(a);
                    pendingSince[a] = Time.time;
                    spawned++;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("[Spawn] air-start failed: " + e);
                    break;
                }
            }
            WingToast.Show(spawned > 0 ? $"{spawned} × {definition.unitName} inbound" : "Air-start failed; see the log");
            return spawned;
        }

        /// <summary>A helicopter's cruise speed from the prefab's parameters, through the same derivation as its profile.</summary>
        private static float RotaryCruise(Aircraft template)
        {
            float maxSpeed = 0f;
            try
            {
                AircraftParameters p = template.GetAircraftParameters();
                if (p != null) maxSpeed = p.maxSpeed;
            }
            catch (Exception e)
            {
                Plugin.LogVerbose("[Spawn] helicopter parameters unreadable on the prefab: " + e.Message);
            }
            // Unknown: a utility helicopter's cruise, not the jet-shaped default a profile derives without a max speed.
            return maxSpeed > 0f
                ? AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = maxSpeed }).CruiseSpeed
                : FallbackRotaryCruise;
        }

        /// <summary>Loaded minimum speed of the called type, from its published stall speed (or its landing and
        /// takeoff speeds) through the same derivation as the profile. Reads the prefab only, so the profile cache
        /// is never filled from a prefab.</summary>
        private static float LoadedMinimum(AircraftDefinition definition, Aircraft template)
        {
            AircraftParameters ap = template.GetAircraftParameters();
            return AirframeProfile.Derive(new ProfileInputs
            {
                PublishedStallKmh = definition.aircraftInfo != null ? definition.aircraftInfo.stallSpeed : 0f,
                LandingSpeed = ap != null ? ap.landingSpeed : 0f,
                TakeoffSpeed = ap != null ? ap.takeoffSpeed : 0f,
            }).MinimumSpeed(1f);
        }

        private void Clear()
        {
            pending.Clear();
            settling.Clear();
            pendingSince.Clear();
        }
    }
}
