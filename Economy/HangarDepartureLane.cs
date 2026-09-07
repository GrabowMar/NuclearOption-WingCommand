using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// One departure at a time per field, so two requisitions never occupy the same
    /// threshold or the same pad.
    ///
    /// The unit of exclusion is the airbase rather than the hangar, which is what it used to
    /// be. A fixed-wing requisition is put on the takeoff runway, and every aircraft at a
    /// field shares that runway however many hangars the field has — reserving a pad said
    /// nothing about whether the strip was clear, and two jets ordered together arrived on
    /// top of each other. Rotary deliveries still come out of a hangar, and holding the same
    /// slot for them costs nothing: a helipad departure is over in seconds.
    ///
    /// This only waits. Every movement decision belongs to the stock AI, and the anchor is
    /// re-read from its live transform each pass so that a floating-origin shift or a moving
    /// carrier does not read as a stationary aircraft that has cleared the spot.
    /// </summary>
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

        /// <summary>
        /// True while a departure has been holding this field's slot for longer than a
        /// delivery should take. Drawn as JAMMED on the Supply field list, which is the
        /// only honest answer when the pad or the strip is occupied by something the mod
        /// does not control.
        /// </summary>
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

        /// <summary>
        /// Hand the slot from the order that reserved it to the member now flying it, so a
        /// delivery keeps its field held across the point where the shop stops owning the
        /// aircraft and the wing starts.
        /// </summary>
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

                // The owning order holds this slot while native doors are still opening or
                // a spawn is still being scheduled. It releases a failed request itself; an
                // accepted request may still emit its aircraft several seconds later.
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
