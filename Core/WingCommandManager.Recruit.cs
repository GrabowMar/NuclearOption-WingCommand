using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        /// <summary>Pending wing delivery and its reporting deadline.</summary>
        private struct PendingRecruit
        {
            public Aircraft Aircraft;
            public WingMember Member;
            public WingPilot PreferredPilot;
            public float ReadyAt;
            public float Deadline;
            public bool DelayReported;
        }

        /// <summary>Seconds before reporting a delayed departure; delivery remains pending.</summary>
        private const float RecruitTimeout = 420f;

        private readonly List<PendingRecruit> recruitQueue = new List<PendingRecruit>();

        /// <summary>Whether an aircraft is currently awaiting takeoff or airborne activation in the recruit
        /// queue.</summary>
        internal bool IsRecruitPending(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            for (int i = 0; i < recruitQueue.Count; i++)
            {
                if (recruitQueue[i].Aircraft == aircraft) return true;
            }
            return false;
        }

        /// <summary>Add deliveries to the roster immediately, but let native taxi and launch AI retain
        /// controls until airborne.</summary>
        internal void QueueRecruit(Aircraft aircraft, WingPilot preferredPilot = null)
        {
            if (aircraft == null) return;

            if (Wing.Find(aircraft) != null) return;
            for (int i = 0; i < recruitQueue.Count; i++)
                if (recruitQueue[i].Aircraft == aircraft) return;

            WingMember member = WingRegistry.HasRoom(Wing.Count)
                ? Wing.Add(aircraft, deferCommand: true, preferredPilot: preferredPilot)
                : null;

            if (member != null)
            {
                Plugin.LogVerbose("[Wing] " + aircraft.unitName +
                                      " rostered slot " + member.Slot +
                                      ", awaiting airborne activation");
            }
            else
            {
                Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
                Plugin.LogVerbose(
                    "[Wing] " + aircraft.unitName + " bought but not yet rostered" +
                    " (LocalSim=" + aircraft.LocalSim +
                    ", room=" + WingRegistry.HasRoom(Wing.Count) +
                    ", pilot=" + (pilot != null) + ")");
            }

            recruitQueue.Add(new PendingRecruit
            {
                Aircraft = aircraft,
                Member = member,
                PreferredPilot = preferredPilot,
                ReadyAt = Time.timeSinceLevelLoad + 0.25f,
                Deadline = Time.timeSinceLevelLoad + RecruitTimeout,
            });
        }

        /// <summary>Wait for pilot initialisation and native takeoff before activating formation control;
        /// switching a parked aircraft would strand it.</summary>
        private void FlushRecruitQueue()
        {
            for (int i = recruitQueue.Count - 1; i >= 0; i--)
            {
                PendingRecruit p = recruitQueue[i];
                Aircraft a = p.Aircraft;

                if (a == null || a.disabled)
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                if (p.Member != null && !p.Member.DeliveryPending)
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                // Report long waits without dropping a purchased aircraft or losing its eventual
                // takeoff handoff.
                if (!p.DelayReported && Time.timeSinceLevelLoad > p.Deadline)
                {
                    p.DelayReported = true;
                    recruitQueue[i] = p;
                    Plugin.Logger.LogWarning("[Wing] " + a.unitName +
                        " departure delayed; retaining its wing assignment until launch");
                }

                // Claim a roster slot when one opens, then retain it through taxi and launch.
                if (p.Member == null)
                {
                    p.Member = Wing.Find(a);
                    if (p.Member == null && WingRegistry.HasRoom(Wing.Count))
                    {
                        p.Member = Wing.Add(a, deferCommand: true, preferredPilot: p.PreferredPilot);
                        if (p.Member != null)
                            Plugin.LogVerbose("[Wing] " + a.unitName +
                                                  " rostered slot " + p.Member.Slot +
                                                  " after wait, awaiting airborne activation");
                    }
                    recruitQueue[i] = p;
                    if (p.Member == null) continue;
                }

                // Do not re-add a delivery the player explicitly released while parked.
                if (!Wing.Contains(p.Member))
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                // Keep the roster entry while departure is pending.
                if (Time.timeSinceLevelLoad < p.ReadyAt) continue;
                if (!p.Member.ActivateWhenAirborne()) continue;

                recruitQueue.RemoveAt(i);
            }
        }
    }
}
