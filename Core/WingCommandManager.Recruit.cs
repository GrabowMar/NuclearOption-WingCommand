using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    internal partial class WingCommandManager
    {
        /// <summary>An aircraft on its way into the wing, and how long it has to get there.</summary>
        private struct PendingRecruit
        {
            public Aircraft Aircraft;
            public WingMember Member;
            public WingPilot PreferredPilot;
            public float ReadyAt;
            public float Deadline;
        }

        /// <summary>How long a delivery has to taxi out and get off the ground.</summary>
        private const float RecruitTimeout = 420f;

        private readonly List<PendingRecruit> recruitQueue = new List<PendingRecruit>();

        /// <summary>
        /// Put a delivery on the wing roster immediately, then wait to take command until the
        /// airbase has launched it. The roster and HUD can therefore show the aircraft while
        /// the stock taxi/door sequence still owns its controls.
        /// </summary>
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
                Plugin.Logger.LogInfo("[Wing] " + aircraft.unitName +
                                      " rostered slot " + member.Slot +
                                      ", awaiting airborne activation");
            }
            else
            {
                Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
                Plugin.Logger.LogInfo(
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

        /// <summary>
        /// Activate deliveries once they can actually hold station.
        ///
        /// Two waits, for two different reasons. An aircraft spawned this frame has not
        /// finished initialising its pilot state machine, so nothing may touch it yet. And an
        /// aircraft delivered into a hangar is parked: it has to taxi out and take off under
        /// the stock AI first, and switching it to formation flight on the apron would strand
        /// it there with its gear up.
        /// </summary>
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

                if (Time.timeSinceLevelLoad > p.Deadline)
                {
                    if (p.Member != null && Wing.Contains(p.Member))
                        Wing.Remove(p.Member, "delivery never got airborne");
                    recruitQueue.RemoveAt(i);
                    Toast(p.Member == null
                        ? a.unitName + " never joined the wing - assign it from the map when airborne"
                        : a.unitName + " never got airborne - assign it from the map when it does");
                    continue;
                }

                // If the immediate add had no slot, claim one as soon as another member
                // leaves. Once claimed, it stays on the roster through taxi and launch.
                if (p.Member == null)
                {
                    p.Member = Wing.Find(a);
                    if (p.Member == null && WingRegistry.HasRoom(Wing.Count))
                    {
                        p.Member = Wing.Add(a, deferCommand: true, preferredPilot: p.PreferredPilot);
                        if (p.Member != null)
                            Plugin.Logger.LogInfo("[Wing] " + a.unitName +
                                                  " rostered slot " + p.Member.Slot +
                                                  " after wait, awaiting airborne activation");
                    }
                    recruitQueue[i] = p;
                    if (p.Member == null) continue;
                }

                // A player may release a still-parked delivery. Do not add it back from the
                // queue after that explicit removal.
                if (!Wing.Contains(p.Member))
                {
                    recruitQueue.RemoveAt(i);
                    continue;
                }

                // Not yet flying: keep waiting while the roster already shows the member.
                if (Time.timeSinceLevelLoad < p.ReadyAt) continue;
                if (!p.Member.ActivateWhenAirborne()) continue;

                recruitQueue.RemoveAt(i);
            }
        }
    }
}
