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

        /// <summary>Launches not yet in the wing: air starts and field launches.</summary>
        public int PendingTotal => pending.Count + groundPending.Count;

        private readonly List<Aircraft> pending = new List<Aircraft>();
        private readonly HashSet<Aircraft> settling = new HashSet<Aircraft>();
        private readonly Dictionary<Aircraft, float> pendingSince = new Dictionary<Aircraft, float>();
        public static float AdoptTimeoutSeconds = 10f, FallbackRotaryCruise = 60f;
        /// <summary>A carrier hangar spawns after its door sequence; give up waiting after this.</summary>
        public static float HangarSpawnTimeoutSeconds = 30f;

        /// <summary>A field launch in flight: the hangar it was asked of (null for a service-point spawn), the aircraft
        /// once it exists, and where its ground pilot starts.</summary>
        private sealed class GroundLaunch
        {
            public Hangar Hangar;
            public GameObject Before;
            public Aircraft Aircraft;
            public FieldTraffic Traffic;
            public Pose Spawn;
            public int HangarIndex = -1, StartNode = -1;
            public WingLedger.Charge Charge;
            public float Since;
            public bool Settling;
        }

        private readonly List<GroundLaunch> groundPending = new List<GroundLaunch>();

        public SpawnService() => Instance = this;

        public void Activate() => Clear();

        public void Deactivate() => Clear();

        public void Tick(float dt)
        {
        }

        public void FixedTick(float dt)
        {
            StepGroundLaunches();
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

        /// <summary>Launch up to <paramref name="n"/> wingmen of <paramref name="definition"/> from <paramref name="airbase"/>
        /// (spec M3 §3): each through a free hangar that can spawn the type, as the game does (doors, supply), or, on a
        /// field without one, at its service points. They start on the ground and taxi out. Each is paid for first
        /// (<see cref="WingLedger"/>; <paramref name="sandbox"/> or Squadron/SandboxFreeCalls: free). Returns how many.</summary>
        public int LaunchFromField(Airbase airbase, AircraftDefinition definition, int n, bool sandbox = false)
        {
            sandbox |= Plugin.Settings.SandboxFreeCalls.Value;
            WingService wing = WingService.Instance;
            // The player calls; without a player aircraft (automation) the anchor stands in.
            Aircraft player = wing?.Player != null ? wing.Player : wing?.Leader;
            if (wing?.Selection == null || airbase == null || definition == null || player == null)
            {
                WingToast.Show("No field to launch from");
                return 0;
            }
            if (!player.IsServer)
            {
                WingToast.Show("Only the host can call wingmen");
                return 0;
            }
            FactionHQ hq = airbase.CurrentHQ != null ? airbase.CurrentHQ : player.NetworkHQ;
            if (airbase.CurrentHQ != null && airbase.CurrentHQ != player.NetworkHQ)
            {
                WingToast.Show(airbase.name + " is not a friendly field");
                return 0;
            }
            GameObject prefab = definition.unitPrefab;
            Aircraft template = prefab != null ? prefab.GetComponent<Aircraft>() : null;
            if (template == null || template.pilots == null || template.pilots.Length == 0 || template.pilots[0] == null ||
                template.pilots[0].pilotType == Pilot.PilotType.VTOL)
            {
                WingToast.Show("VTOL aircraft cannot fly formation");
                return 0;
            }
            FieldTraffic traffic = FieldRegistry.For(airbase);
            if (traffic == null)
            {
                WingToast.Show(airbase.name + " has no runway to take off from");
                return 0;
            }
            n = Math.Min(n, WingService.MaxMembers - wing.Members.Count - pending.Count - groundPending.Count);
            if (n <= 0)
            {
                WingToast.Show("Wing is full");
                return 0;
            }
            var used = new HashSet<Hangar>();
            int launched = 0;
            string refused = null;
            for (int k = 0; k < n; k++)
            {
                try
                {
                    CallQuote quote = WingLedger.Quote(definition, hq, true, sandbox);
                    if (!quote.Allowed)
                    {
                        refused = quote.Reason;
                        break;
                    }
                    GroundLaunch launch = FromHangar(airbase, definition, traffic, used);
                    if (launch == null)
                    {
                        quote = WingLedger.Quote(definition, hq, false, sandbox);
                        launch = FromServicePoint(definition, traffic, hq, player);
                    }
                    if (launch == null) break;
                    launch.Charge = WingLedger.Take(quote, hq, definition, launch.Hangar != null);
                    launch.Since = Time.time;
                    groundPending.Add(launch);
                    launched++;
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("[Spawn] field launch failed: " + e);
                    break;
                }
            }
            WingToast.Show(launched > 0
                ? launched + " × " + definition.unitName + " launching from " + airbase.name + (refused != null ? " (then: " + refused + ")" : "")
                : refused != null ? "Cannot call " + definition.unitName + ": " + refused
                : airbase.name + " could not launch " + definition.unitName);
            return launched;
        }

        private static GroundLaunch FromHangar(Airbase airbase, AircraftDefinition definition, FieldTraffic traffic, HashSet<Hangar> used)
        {
            if (airbase.hangars == null || !GameAccess.HangarSpawnAvailable) return null;
            for (int i = 0; i < airbase.hangars.Count; i++)
            {
                Hangar h = airbase.hangars[i];
                if (h == null || used.Contains(h) || h.Disabled || !h.Available || !h.CanSpawnAircraft(definition)) continue;
                GameObject before = GameAccess.GetHangarSpawnedObject(h);
                if (!h.TrySpawnAircraft(null, definition, default, null, 1f).Allowed) continue;
                used.Add(h);
                Transform t = h.GetSpawnTransform();
                return new GroundLaunch
                {
                    Hangar = h, Before = before, Traffic = traffic, HangarIndex = i,
                    Spawn = new Pose(t.GlobalPosition().ToVec3(), t.forward.ToVec3()),
                };
            }
            return null;
        }

        /// <summary>A field without a usable hangar: the aircraft appears at rest on the first free service-point spot
        /// (clear of members, pending launches and other aircraft on the field).</summary>
        private GroundLaunch FromServicePoint(AircraftDefinition definition, FieldTraffic traffic, FactionHQ hq, Aircraft player)
        {
            if (!ServiceSpots.Pick(traffic, SpotTaken(traffic), out ServiceSpot spot)) return null;
            Vec3 pos = spot.Pose.Pos + Vec3.Up * definition.spawnOffset.y;
            Quaternion rotation = Quaternion.LookRotation(spot.Pose.Fwd.Horizontal.Normalized.ToUnity()) * Quaternion.Euler(definition.restRotation);
            Aircraft a = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, definition.unitPrefab, null, 1f, default, pos.ToGlobal(),
                rotation, Vector3.zero, null, hq, "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                player.skill, player.bravery);
            return a == null ? null : new GroundLaunch { Aircraft = a, Traffic = traffic, Spawn = spot.Pose, StartNode = spot.StartNode };
        }

        private Func<Vec3, bool> SpotTaken(FieldTraffic traffic) => at =>
        {
            if (traffic.Occupied(at, ServiceSpots.ClearRadius, -1)) return true;
            foreach (GroundLaunch g in groundPending)
                if (g.Traffic == traffic && (g.Spawn.Pos - at).Horizontal.Length < ServiceSpots.ClearRadius) return true;
            return false;
        };

        /// <summary>Hangar spawns appear in the hangar's private spawnedObject (at once, or after a carrier's doors); every
        /// launch is adopted one physics tick after the game initialised its AI, before native taxi can act.</summary>
        private void StepGroundLaunches()
        {
            for (int i = groundPending.Count - 1; i >= 0; i--)
            {
                GroundLaunch g = groundPending[i];
                if (g.Aircraft == null)
                {
                    GameObject spawned = g.Hangar != null ? GameAccess.GetHangarSpawnedObject(g.Hangar) : null;
                    if (spawned != null && !ReferenceEquals(spawned, g.Before)) g.Aircraft = spawned.GetComponent<Aircraft>();
                    if (g.Aircraft == null)
                    {
                        if (Time.time - g.Since > HangarSpawnTimeoutSeconds)
                        {
                            Plugin.Logger.LogWarning("[Spawn] a hangar never produced its aircraft; launch dropped");
                            WingLedger.Refund(g.Charge, null);
                            groundPending.RemoveAt(i);
                        }
                        continue;
                    }
                    g.Since = Time.time;
                }
                if (g.Aircraft == null || g.Aircraft.disabled)
                {
                    WingLedger.Refund(g.Charge, g.Aircraft);
                    groundPending.RemoveAt(i);
                    continue;
                }
                Pilot p = g.Aircraft.pilots != null && g.Aircraft.pilots.Length > 0 ? g.Aircraft.pilots[0] : null;
                if (p == null || p.currentState == null)
                {
                    if (Time.time - g.Since > AdoptTimeoutSeconds)
                    {
                        Plugin.Logger.LogWarning("[Spawn] " + g.Aircraft.definition.unitName + " never initialised its AI; back to the reserve");
                        WingLedger.Refund(g.Charge, g.Aircraft);
                        groundPending.RemoveAt(i);
                    }
                    continue;
                }
                if (!g.Settling)
                {
                    g.Settling = true;
                    continue;
                }
                groundPending.RemoveAt(i);
                if (WingService.Instance != null && WingService.Instance.AdoptGround(g.Aircraft, g.Traffic, g.Spawn, g.HangarIndex, g.StartNode))
                    WingLedger.Joined(g.Aircraft, g.Charge);
                else
                {
                    Plugin.Logger.LogWarning("[Spawn] " + g.Aircraft.definition.unitName + " could not join the wing; back to the reserve");
                    WingLedger.Refund(g.Charge, g.Aircraft);
                }
            }
        }

        private void Clear()
        {
            pending.Clear();
            groundPending.Clear();
            settling.Clear();
            pendingSince.Clear();
        }
    }
}
