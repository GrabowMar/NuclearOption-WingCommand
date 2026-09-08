using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Serialises departures per airbase, covering shared runways and pads across hangars. Native
    /// AI owns movement. Read the anchor's live transform so origin shifts and carrier motion do not
    /// falsely signal clearance.</summary>
    internal static class HangarDepartureLane
    {
        private sealed class Departure
        {
            internal Airbase Airbase;
            internal object Owner;
            internal Transform Anchor;
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

        /// <summary>Whether field departure ownership has exceeded its expected duration; shown as JAMMED
        /// in Supply.</summary>
        internal static bool IsJammed(Airbase airbase)
        {
            if (airbase == null) return false;
            for (int i = 0; i < active.Count; i++)
            {
                Departure departure = active[i];
                if (departure.Airbase != airbase) continue;
                if (Time.unscaledTime - departure.StartedAt >= WingTuning.HangarDeliveryTimeout)
                    return true;
            }
            return false;
        }

        internal static bool Reserve(Airbase airbase, Hangar hangar, object owner)
        {
            Transform spawn = hangar != null ? hangar.GetSpawnTransform() : null;
            return Reserve(airbase, spawn, owner);
        }

        internal static bool Reserve(Airbase airbase, Transform anchor, object owner)
        {
            if (airbase == null || owner == null || !IsFree(airbase)) return false;
            active.Add(new Departure
            {
                Airbase = airbase,
                Owner = owner,
                Anchor = anchor != null ? anchor : airbase.transform,
                StartedAt = Time.unscaledTime,
            });
            return true;
        }

        /// <summary>Transfer lane ownership from purchase order to wing member without releasing the field
        /// during handoff.</summary>
        internal static bool Transfer(object from, object to)
        {
            if (from == null || to == null) return false;
            for (int i = 0; i < active.Count; i++)
            {
                if (!ReferenceEquals(active[i].Owner, from)) continue;
                active[i].Owner = to;
                return true;
            }
            return false;
        }

        internal static void Track(object owner, Aircraft aircraft)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (!ReferenceEquals(active[i].Owner, owner)) continue;
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

                // Keep order ownership while native doors or delayed spawn are pending. The order
                // releases failed requests itself.
                if (!departure.AircraftTracked && departure.Airbase != null) continue;

                if (aircraft == null || aircraft.disabled || departure.Airbase == null ||
                    departure.Anchor == null)
                {
                    active.RemoveAt(i);
                    continue;
                }

                float clearance = Mathf.Max(120f, aircraft.maxRadius * 6f);
                if ((aircraft.transform.position - departure.Anchor.position).sqrMagnitude >=
                    clearance * clearance)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (departure.DelayReported ||
                    Time.unscaledTime - departure.StartedAt < WingTuning.HangarDeliveryTimeout)
                    continue;

                departure.DelayReported = true;
                Plugin.Logger.LogWarning(
                    "[Shop] departure lane blocked at " + WingLaunchFields.DisplayName(departure.Airbase) +
                    "; retaining it until the aircraft clears the field");
            }
        }

        internal static void Reset() => active.Clear();
    }
}
