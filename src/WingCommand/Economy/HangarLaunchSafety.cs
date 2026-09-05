using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Keep nearby pads separated, and stop repeating failed apron launches.</summary>
    internal static class HangarLaunchSafety
    {
        private sealed class Departure
        {
            public Hangar Hangar;
            public Aircraft Aircraft;
            public AircraftDefinition Definition;
            public float TrackedAt;
        }

        /// <summary>
        /// Longest a departure is allowed to hold up the airbase it launched from. A stuck
        /// or very slow taxi should eventually give the field back rather than lock every
        /// later requisition out of it forever. Generous, because the condition this backs
        /// up (<see cref="LaunchSafety.ReadyForHandoff"/>) waits for actual liftoff, not
        /// just clearing the pad, and a full taxi-and-takeoff roll can take a while.
        /// </summary>
        private const float MaxTrackSeconds = 45f;

        private static readonly List<Departure> departures = new List<Departure>();
        private static readonly Dictionary<Hangar, HashSet<AircraftDefinition>> failed =
            new Dictionary<Hangar, HashSet<AircraftDefinition>>();

        public static bool IsBlocked(Hangar hangar, AircraftDefinition definition) =>
            hangar != null && failed.TryGetValue(hangar, out var types) && types.Contains(definition);

        internal static float Size(AircraftDefinition definition) => definition == null ? 0f :
            Mathf.Max(definition.length, definition.width, definition.height);

        public static bool IsClear(Hangar hangar, AircraftDefinition definition)
        {
            if (hangar == null || IsBlocked(hangar, definition)) return false;
            Transform spawn = hangar.GetSpawnTransform();
            if (spawn == null) return false;
            // Include other factions, player aircraft and wrecks. Native Available
            // tracks this hangar's door sequence, not occupancy of adjacent pads.
            foreach (Aircraft aircraft in UnitRegistry.allAircraft)
            {
                if (aircraft == null) continue;
                float radius = LaunchSafety.Clearance(Size(definition), Size(aircraft.definition));
                if ((aircraft.transform.position - spawn.position).sqrMagnitude < radius * radius)
                    return false;
            }
            return true;
        }

        public static void Track(Hangar hangar, Aircraft aircraft)
        {
            departures.Add(new Departure
            {
                Hangar = hangar,
                Aircraft = aircraft,
                Definition = aircraft.definition,
                TrackedAt = Time.unscaledTime,
            });
        }

        /// <summary>
        /// Whether every hangar at this airbase has finished its last tracked departure -
        /// the aircraft either lifted off, was lost trying, or hit the tracking ceiling.
        ///
        /// A clear pad (<see cref="IsClear"/>) is not a clear taxiway. Clearing the pad's
        /// own small radius was tried first and was not enough: two Ifrits departing
        /// different pads at the same field still crashed into each other converging on
        /// the same shared taxiway well outside either pad's radius. This waits instead on
        /// <see cref="LaunchSafety.ReadyForHandoff"/> - the same altitude/speed/sink gate
        /// that decides when a delivered aircraft is actually flying - so a new departure
        /// only starts once the last one is genuinely off the ground, not just off its pad.
        /// </summary>
        public static bool IsAirbaseClear(Airbase airbase)
        {
            if (airbase == null) return true;
            for (int i = 0; i < departures.Count; i++)
            {
                Hangar hangar = departures[i].Hangar;
                if (hangar != null && hangar.parentAirbase == airbase) return false;
            }
            return true;
        }

        public static void Tick()
        {
            for (int i = departures.Count - 1; i >= 0; i--)
            {
                Departure departure = departures[i];
                Aircraft aircraft = departure.Aircraft;
                Hangar hangar = departure.Hangar;
                if (hangar == null) { departures.RemoveAt(i); continue; }
                Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
                if (aircraft == null || aircraft.disabled || pilot != null && (pilot.dead || pilot.ejected))
                {
                    if (!failed.TryGetValue(hangar, out var types))
                        failed[hangar] = types = new HashSet<AircraftDefinition>();
                    types.Add(departure.Definition);
                    Plugin.Logger.LogWarning("[Shop] blocked " + hangar.name + " at " +
                        hangar.parentAirbase?.name + " for " + departure.Definition.unitName +
                        ": aircraft lost before clearing the spawn area; using another compatible pad");
                    departures.RemoveAt(i);
                    continue;
                }
                if (IsReadyForHandoff(aircraft, pilot))
                {
                    departures.RemoveAt(i);
                    continue;
                }

                // A stuck or crawling taxi should not lock its airbase out of future
                // departures forever - give the field back after a generous ceiling.
                if (Time.unscaledTime - departure.TrackedAt >= MaxTrackSeconds)
                    departures.RemoveAt(i);
            }
        }

        /// <summary>
        /// Mirrors <see cref="WingMember.IsAirborne"/> for an aircraft that has no
        /// <see cref="WingMember"/> yet - a delivery is only wrapped in one once it is
        /// claimed, but a departure is tracked from the moment it is claimed, which is
        /// before that gate would otherwise be reachable.
        /// </summary>
        private static bool IsReadyForHandoff(Aircraft aircraft, Pilot pilot)
        {
            if (aircraft.rb == null) return false;
            AircraftParameters p = aircraft.GetAircraftParameters();
            return LaunchSafety.ReadyForHandoff(aircraft.radarAlt, aircraft.speed,
                p != null ? p.takeoffSpeed : 70f, aircraft.rb.velocity.y,
                WingRegistry.IsRotary(aircraft), StillLaunching(pilot));
        }

        private static bool StillLaunching(Pilot pilot)
        {
            if (pilot == null) return false;
            PilotBaseState state = pilot.currentState;
            if (state == null) return false;
            return state == pilot.AITaxiState || state == pilot.AITakeoffState ||
                   state == pilot.AIHeloTakeoffState || state is PilotParkedState;
        }

        public static void Reset() { departures.Clear(); failed.Clear(); }
    }
}
