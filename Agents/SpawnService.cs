using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Air-starts wingmen (spec §8), host only.
    /// <list type="bullet">
    /// <item>Placement: 2 km behind and 150 m below the leader, spread toward each slot's side, at least
    /// 300 m above the terrain, flying at the leader's velocity.</item>
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
                    continue;
                }
                Pilot p = a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
                if (p == null || p.currentState == null) continue;   // the game has not initialised it yet
                if (settling.Add(a)) continue;                       // wait one more physics tick
                settling.Remove(a);
                pending.RemoveAt(i);
                if (WingService.Instance == null || !WingService.Instance.Adopt(a))
                    Plugin.Logger.LogWarning($"[Spawn] {a.definition.unitName} could not join the wing and keeps the game's AI");
            }
        }

        /// <summary>Air-start up to <paramref name="n"/> wingmen of <paramref name="definition"/> (null: the
        /// leader's type). Returns how many were spawned.</summary>
        public int Call(int n, AircraftDefinition definition)
        {
            WingService wing = WingService.Instance;
            Aircraft leader = wing?.Leader;
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
            if (leader.radarAlt < MinLeaderAgl)
            {
                WingToast.Show($"Climb above {MinLeaderAgl:0} m first");
                return 0;
            }
            definition = definition ?? leader.definition;
            GameObject prefab = definition != null ? definition.unitPrefab : null;
            if (prefab == null || definition.aircraftParameters == null || definition.aircraftParameters.takeoffDistance <= 0f)
            {
                WingToast.Show("That airframe cannot fly formation in this build");
                return 0;
            }
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
            Quaternion rotation = Quaternion.LookRotation(AirStart.Heading(lv).ToUnity());
            LiveryKey livery = definition == leader.definition ? leader.NetworkLiveryKey : default;
            int spawned = 0;
            for (int k = 0; k < n; k++)
            {
                SlotDef slot = SlotSolver.SlotFor(wing.Selection.Current, wing.Members.Count + pending.Count);
                float ground = TerrainProbe.GroundY(AirStart.Position(lp, lv, slot.Right, k, 0f));
                Vec3 pos = AirStart.Position(lp, lv, slot.Right, k, ground);
                try
                {
                    Aircraft a = spawner.SpawnAircraft(null, prefab, null, 1f, livery, pos.ToGlobal(), rotation, lv.ToUnity(),
                        null, leader.NetworkHQ, "WingCommand_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        leader.skill, leader.bravery);
                    if (a == null) break;
                    pending.Add(a);
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

        private void Clear()
        {
            pending.Clear();
            settling.Clear();
        }
    }
}
