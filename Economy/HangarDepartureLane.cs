using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// One stock departure is started at an airbase at a time. The game owns every movement
    /// decision; this only waits until the previous airframe has cleared its hangar area.
    /// </summary>
    internal static class HangarDepartureLane
    {
        private sealed class Departure
        {
            internal Airbase Airbase;
            internal Vector3 SpawnPosition;
            internal Aircraft Aircraft;
            internal float StartedAt;
        }

        private static readonly List<Departure> active = new List<Departure>();

        internal static bool IsFree(Airbase airbase)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i].Airbase == airbase) return false;
            return true;
        }

        internal static bool Reserve(Airbase airbase, Hangar hangar)
        {
            if (airbase == null || !IsFree(airbase)) return false;
            Transform spawn = hangar?.GetSpawnTransform();
            active.Add(new Departure
            {
                Airbase = airbase,
                SpawnPosition = spawn != null ? spawn.position : airbase.transform.position,
                StartedAt = Time.unscaledTime,
            });
            return true;
        }

        internal static void Track(Airbase airbase, Aircraft aircraft)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i].Airbase == airbase)
                {
                    active[i].Aircraft = aircraft;
                    return;
                }
        }

        internal static void Release(Airbase airbase)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Airbase == airbase) active.RemoveAt(i);
        }

        internal static void Tick()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Departure departure = active[i];
                Aircraft aircraft = departure.Aircraft;
                if (aircraft == null || aircraft.disabled)
                {
                    active.RemoveAt(i);
                    continue;
                }
                float clearance = Mathf.Max(120f, aircraft.maxRadius * 6f);
                if ((aircraft.transform.position - departure.SpawnPosition).sqrMagnitude >= clearance * clearance)
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (Time.unscaledTime - departure.StartedAt < WingTuning.HangarDeliveryTimeout) continue;
                Plugin.Logger.LogWarning("[Shop] departure lane timed out at " + departure.Airbase.name + "; releasing it");
                active.RemoveAt(i);
            }
        }

        internal static void Reset() => active.Clear();
    }
}