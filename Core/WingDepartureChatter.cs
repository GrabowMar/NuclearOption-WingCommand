using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Observe native departure phases without changing flight ownership.</summary>
    internal static class WingDepartureChatter
    {
        private static readonly DepartureChatter reports = new DepartureChatter();
        private static readonly Dictionary<int, WingMember> tracked = new Dictionary<int, WingMember>();
        private static readonly List<int> removed = new List<int>();

        internal static void Activated(WingMember member) => Observe(member, DeparturePhase.Airborne);

        internal static bool ReportingLiftoff(WingMember member) => member?.Aircraft != null &&
            reports.HasPending(member.Aircraft.GetInstanceID(), DeparturePhase.Airborne);

        private static void Observe(WingMember member, DeparturePhase phase)
        {
            if (member == null || !member.Alive || member.IsSurface) return;
            int id = member.Aircraft.GetInstanceID();
            tracked[id] = member;
            reports.Observe(id, phase, Time.unscaledTime);
        }

        internal static void Tick(WingRegistry wing, bool speechAllowed)
        {
            if (wing == null) return;
            IReadOnlyList<WingMember> members = wing.Members;
            for (int i = 0; i < members.Count; i++)
            {
                WingMember member = members[i];
                if (member == null || !member.Alive || !member.DeliveryPending || member.IsSurface) continue;
                PilotBaseState state = member.Pilot.currentState;
                if (state is AIPilotTaxiState && member.Aircraft.speed > 0.5f)
                    Observe(member, DeparturePhase.Taxiing);
                else if ((state is AIPilotTakeoffState && member.Aircraft.speed > 2f) ||
                         (state is AIHeloTakeoffState && member.Aircraft.radarAlt > 1f))
                    Observe(member, DeparturePhase.Departing);
            }

            removed.Clear();
            foreach (var pair in tracked)
            {
                WingMember member = pair.Value;
                if (!member.Alive || !wing.Contains(member)) removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
            {
                tracked.Remove(removed[i]);
                reports.Forget(removed[i]);
            }

            if (!speechAllowed)
            {
                reports.Silence();
                return;
            }

            if (!reports.TryDequeue(Time.unscaledTime, WingChatterHud.IsIdle,
                out int id, out DeparturePhase phase) || !tracked.TryGetValue(id, out WingMember speaker)) return;
            WingComms.Call call = phase == DeparturePhase.Taxiing ? WingComms.Call.Taxiing :
                phase == DeparturePhase.Departing ? WingComms.Call.Departing :
                speaker.Order == WingOrder.Formation && speaker.Leader != null
                    ? WingComms.Call.AirborneRejoining : WingComms.Call.Airborne;
            WingComms.Say(speaker, call);
        }

        internal static void Reset()
        {
            reports.Reset();
            tracked.Clear();
            removed.Clear();
        }
    }
}
