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
            internal object Owner;
            internal Transform Spawn;
            internal Aircraft Aircraft;
            internal bool AircraftTracked;
            internal float StartedAt;
            internal bool DelayReported;
        }

        private static readonly List<Departure> active = new List<Departure>();

        internal static bool IsFree(Airbase airbase)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i].Airbase == airbase) return false;
            return true;
        }

        internal static bool Reserve(Airbase airbase, Hangar hangar, object owner)
        {
            if (airbase == null || owner == null || !IsFree(airbase)) return false;
            Transform spawn = hangar?.GetSpawnTransform();
            active.Add(new Departure
            {
                Airbase = airbase,
                Owner = owner,
                Spawn = spawn != null ? spawn : airbase.transform,
                StartedAt = Time.unscaledTime,
            });
            return true;
        }

        internal static void Track(object owner, Aircraft aircraft)
        {
            for (int i = 0; i < active.Count; i++)
                if (ReferenceEquals(active[i].Owner, owner))
                {
                    active[i].Aircraft = aircraft;
                    active[i].AircraftTracked = true;
                    active[i].StartedAt = Time.unscaledTime;
                    active[i].DelayReported = false;
                    return;
                }
        }

        internal static void Release(object owner)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (ReferenceEquals(active[i].Owner, owner)) active.RemoveAt(i);
        }

        internal static void Tick()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Departure departure = active[i];
                Aircraft aircraft = departure.Aircraft;
                // The owning order holds this lane while native doors are still opening.
                // It releases a failed request; an accepted request may still spawn late.
                if (!departure.AircraftTracked && departure.Airbase != null) continue;
                if (aircraft == null || aircraft.disabled || departure.Airbase == null || departure.Spawn == null)
                {
                    active.RemoveAt(i);
                    continue;
                }
                float clearance = Mathf.Max(120f, aircraft.maxRadius * 6f);
                // Follow the live pad: floating-origin shifts and moving carriers move
                // both transforms, and must not look like a stationary plane cleared it.
                if ((aircraft.transform.position - departure.Spawn.position).sqrMagnitude >= clearance * clearance)
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (departure.DelayReported ||
                    Time.unscaledTime - departure.StartedAt < WingTuning.HangarDeliveryTimeout) continue;
                departure.DelayReported = true;
                Plugin.Logger.LogWarning("[Shop] departure lane blocked at " + departure.Airbase.name +
                    "; retaining it until the aircraft clears the hangar");
            }
        }

        internal static void Reset() => active.Clear();
    }
}
