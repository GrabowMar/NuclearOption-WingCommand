using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Recovery and refit (spec M3 §4).
    /// <list type="bullet">
    /// <item>RTB, Refit and bingo fuel send a member home: to the picked launch field while it is friendly, else the
    /// nearest friendly field. A member taxiing out turns round; one lining up or rolling goes once airborne.</item>
    /// <item>Approach: its own pipeline flies the <see cref="RecoveryPilot"/>'s approach; there the game's landing state
    /// takes over (<see cref="NativeLandingBridge"/>), watched by <see cref="SwitchStateGuard"/> (every way out becomes
    /// ours) and <see cref="EjectGuard"/>; an overdue landing is taken back.</item>
    /// <item>Down: the ground pilot taxis it to a stand (the field it landed at); an RTB member goes back to the reserve
    /// there, a refit member is refuelled and rearmed after its refit time and departs again, rejoining once
    /// airborne.</item>
    /// <item>Three failed landings, or a landing where no field can take it, return it to the reserve.</item>
    /// </list></summary>
    internal sealed partial class WingService
    {
        public static float BingoCheckSeconds = 1f, BingoFieldSeconds = 10f, TouchdownFieldRadius = 5000f;

        /// <summary>Sends a member home. False when it cannot go (no friendly field, already going, released).</summary>
        public bool Recover(WingMember m, RecoveryIntent intent)
        {
            if (m.Released || m.Recovery != null) return false;
            if (m.OnGround)
            {
                if (m.Ground.TaxiIn(m.Last, missionTime, Events, m.Brain.Slot))
                {
                    m.Recovery = RecoveryPilot.FromGround(m.Id, m.Ground, intent, m.Profile.Class);
                    return true;
                }
                m.PendingRecovery = intent;
                m.HasPendingRecovery = true;
                return true;
            }
            Airbase airbase = RecoveryField(m);
            FieldTraffic field = airbase != null ? FieldRegistry.For(airbase) : null;
            if (field == null) return false;
            m.Recovery = new RecoveryPilot(m.Id, field, m.Profile.Class, intent);
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} {(intent == RecoveryIntent.Rtb ? "returning to base" : "going to refit")} at {airbase.name}");
            return true;
        }

        public int RecoverAll(RecoveryIntent intent)
        {
            int n = 0;
            foreach (WingMember m in Members)
                if (Recover(m, intent)) n++;
            return n;
        }

        /// <summary>The picked launch field while it is friendly, else the friendly field nearest the member.</summary>
        private Airbase RecoveryField(WingMember m)
        {
            List<Airbase> fields = FriendlyFields(m.Aircraft);
            if (LaunchField != null && fields.Contains(LaunchField)) return LaunchField;
            return fields.Count > 0 ? fields[0] : null;
        }

        /// <summary>A recovering member's airborne steps (approach, a release after failed landings); false when its
        /// ground pilot flies it.</summary>
        private bool StepRecovery(WingMember m, WingFrame frame, float dt)
        {
            if (m.ReserveNow)
            {
                ReturnToReserve(m, "no field to take it after landing");
                return true;
            }
            RecoveryPilot r = m.Recovery;
            switch (r.Phase)
            {
                case RecoveryPhase.Approach:
                    FlightIntent intent = r.ApproachIntent(m.Last, m.Profile, out bool handOver);
                    ControlWriter.Fly(m.Aircraft, m.Brain.FlyIntent(intent, frame, m.Last, m.Profile, dt), m.Profile.Class);
                    if (!handOver) return true;
                    // Landing first: the guards protect it from the moment the game's state enters (its search may fail
                    // at once).
                    r.LandingBegun(missionTime);
                    Plugin.Logger.LogInfo($"[Wing] #{m.Number} handed to the game's landing");
                    if (!NativeLandingBridge.Begin(m) && r.Phase == RecoveryPhase.Landing) r.LandingFailed(missionTime, Events, m.Brain.Slot);
                    return true;
                case RecoveryPhase.Released:
                    WingToast.Show($"#{m.Number} could not land; back to the reserve");
                    ReturnToReserve(m, $"{r.FailedLandings} failed landings");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>After a recovering member's ground step: back to the reserve on its stand (RTB), or serviced and out
        /// again (Refit).</summary>
        private void StepRecoveryGround(WingMember m)
        {
            Aircraft a = m.Aircraft;
            RecoveryAction action = m.Recovery.Update(missionTime, a.GetFuelLevel(), AmmoFraction(a), Events, m.Brain.Slot);
            if (action == RecoveryAction.Reserve)
            {
                Release(m, "back in the reserve");
                return;
            }
            if (action != RecoveryAction.Service) return;
            Service(a);
            FieldTraffic field = m.Recovery.Ground.Field;
            m.Recovery.Serviced(missionTime, LineupPlanner.Abreast(field.Runway.Width, m.Profile.SpanM), Events, m.Brain.Slot);
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} refuelled and rearmed; departing again");
        }

        /// <summary>Airborne again from the field: a refit is over, and a recovery asked for on the runway starts now.</summary>
        private void AirborneAfterGround(WingMember m)
        {
            m.Recovery = null;
            if (!m.HasPendingRecovery) return;
            m.HasPendingRecovery = false;
            Recover(m, m.PendingRecovery);
        }

        /// <summary>The game's landing state switching a recovering member to <paramref name="next"/>: our state instead
        /// (null: not ours to redirect).</summary>
        public PilotBaseState LeaveNativeLanding(Pilot pilot, PilotBaseState next)
        {
            WingMember m = null;
            foreach (WingMember x in Members)
                if (ReferenceEquals(x.Pilot, pilot)) m = x;
            if (m == null || m.Released || m.Recovery == null || m.Recovery.Phase != RecoveryPhase.Landing) return null;
            if (ReferenceEquals(next, m.State) || !NativeLandingBridge.Landing(pilot)) return null;
            try
            {
                bool down = m.Aircraft.radarAlt < 2f;
                bool touchdown = (next != null && ReferenceEquals(next, pilot.AITaxiState)) || (down && ReferenceEquals(next, pilot.parkedState));
                if (touchdown) Touchdown(m);
                else
                {
                    m.Recovery.LandingFailed(missionTime, Events, m.Brain.Slot);
                    Plugin.Logger.LogInfo($"[Wing] #{m.Number} landing failed ({(next == null ? "no runway" : next.GetType().Name)}); approaching again");
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[Wing] #{m.Number} leaving the game's landing failed: {e}");
                m.ReserveNow = true;
            }
            return m.State;
        }

        /// <summary>Down: a ground pilot on the field it landed at takes it to a stand (set before our state is entered,
        /// so the gear stays down); a site with no field for ground operations returns it to the reserve.</summary>
        private void Touchdown(WingMember m)
        {
            Aircraft a = m.Aircraft;
            Airbase airbase = NearestAirbase(a.transform.position);
            NativeLandingBridge.Deregister(airbase, a);
            FieldTraffic field = airbase != null ? FieldRegistry.For(airbase) : null;
            if (field == null)
            {
                m.ReserveNow = true;
                return;
            }
            Transform t = a.transform;
            Rigidbody rb = a.rb;
            var s = new AircraftState
            {
                Pos = a.GlobalPosition().ToVec3(), Fwd = t.forward.ToVec3(), Up = t.up.ToVec3(), Right = t.right.ToVec3(),
                Vel = rb != null ? rb.velocity.ToVec3() : Vec3.Zero, RadarAlt = a.radarAlt,
            };
            m.Recovery.Landed(field, s, missionTime, Events, m.Brain.Slot);
            m.Ground = m.Recovery.Ground;
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} down at {airbase.name}; taxiing in");
        }

        /// <summary>Overdue landings are taken back (the game's state may hover or circle for ever).</summary>
        private void SuperviseLandings()
        {
            foreach (WingMember m in Members)
            {
                RecoveryPilot r = m.Recovery;
                if (m.Released || r == null || r.Phase != RecoveryPhase.Landing || !NativeLandingBridge.Landing(m.Pilot)) continue;
                if (!r.LandingOverdue(missionTime)) continue;
                r.LandingFailed(missionTime, Events, m.Brain.Slot);
                NativeLandingBridge.TakeBack(m);
                Plugin.Logger.LogInfo($"[Wing] #{m.Number} landing overdue; approaching again");
            }
        }

        private void CheckBingo(WingMember m, float dt)
        {
            m.BingoClock += dt;
            if (m.BingoClock < BingoCheckSeconds) return;
            float step = m.BingoClock;
            m.BingoClock = 0f;
            if (m.BingoField == null || missionTime - m.BingoFieldAt > BingoFieldSeconds)
            {
                m.BingoField = RecoveryField(m);
                m.BingoFieldAt = missionTime;
            }
            if (m.BingoField == null) return;
            float distance = (m.BingoField.transform.position - m.Aircraft.transform.position).magnitude;
            if (!m.Bingo.Update(m.Aircraft.GetFuelLevel(), distance, m.Last.Tas, step)) return;
            Events.Push(new WingEvent { Time = missionTime, Member = m.Brain.Slot, Kind = WingEventKind.Bingo });
            WingToast.Show($"#{m.Number} bingo fuel; returning to base");
            Recover(m, RecoveryIntent.Rtb);
        }

        private void ReturnToReserve(WingMember m, string why)
        {
            if (m.Released) return;
            m.Released = true;
            m.Recovery = null;
            m.Ground?.Leave();
            if (m.Aircraft != null && !m.Aircraft.disabled) m.Aircraft.ReturnToInventory();
            Plugin.Logger.LogInfo($"[Wing] #{m.Number} returned to the reserve: {why}");
        }

        private static Airbase NearestAirbase(Vector3 at)
        {
            Airbase best = null;
            float bestD = TouchdownFieldRadius;
            foreach (Airbase b in UnityEngine.Object.FindObjectsOfType<Airbase>())
            {
                if (b == null || b.disabled) continue;
                float d = (b.transform.position - at).magnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = b;
            }
            return best;
        }

        /// <summary>Munitions aboard as a fraction of a full load (cargo aside); 1 with no weapon stations.</summary>
        private static float AmmoFraction(Aircraft a)
        {
            int total = 0, full = 0;
            if (a.weaponStations != null)
                foreach (WeaponStation w in a.weaponStations)
                    if (w != null && !w.Cargo && w.FullAmmo > 0)
                    {
                        total += w.GetAmmoTotal();
                        full += w.FullAmmo;
                    }
            return full > 0 ? (float)total / full : 1f;
        }

        /// <summary>Tanks full again (the aircraft's loadout fuel) and every station rearmed through the game's own
        /// networked calls (native §B9: the rearmer must not be null; the aircraft serves as its own).</summary>
        private static void Service(Aircraft a)
        {
            a.Refuel(a);
            if (a.weaponStations == null || a.weaponStations.Count == 0) return;
            var stations = new int[a.weaponStations.Count];
            bool any = false;
            for (int i = 0; i < stations.Length; i++)
            {
                WeaponStation w = a.weaponStations[i];
                stations[i] = w == null || w.Cargo ? 0 : Math.Max(0, w.FullAmmo - w.GetAmmoTotal());
                any |= stations[i] > 0;
            }
            if (any) a.RpcRearm(new RearmEventArgs { Rearmer = a, Stations = stations });
        }
    }
}
