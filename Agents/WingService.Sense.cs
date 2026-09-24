namespace WingCommand
{
    /// <summary>The members' own state the fight and the orders do not report (spec WMC rebuild R3): the hull from the parts'
    /// hit points (DAMAGED) and running out of weapons in formation (Winchester), sampled once a second.</summary>
    internal sealed partial class WingService
    {
        /// <summary>Seconds between samples.</summary>
        public static float SampleSeconds = 1f;

        private float sampleClock;

        private void SampleMembers(float dt)
        {
            if ((sampleClock += dt) < SampleSeconds) return;
            sampleClock = 0f;
            foreach (WingMember m in Members)
            {
                if (m.Released || !m.Alive) continue;
                Aircraft a = m.Aircraft;
                float sum = 0f;
                int n = 0;
                bool off = false;
                var parts = a.partLookup;
                if (parts != null)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        UnitPart p = parts[i];
                        if (p == null) continue;
                        n++;
                        // ponytail: hit points are 100 a part (UnitPart.Awake/Repair); a game change to that scale misreads the hull.
                        if (p.IsDetached()) off = true;
                        else sum += UnityEngine.Mathf.Clamp01(p.hitPoints / 100f);
                    }
                if (m.Damage.Update(sum, n, off))
                    Events.Push(new WingEvent { Time = missionTime, Member = m.Seat, Kind = WingEventKind.Damaged, Element = (byte)ElementOf(m) });
                bool inFormation = !m.Engaged && m.Recovery == null && !m.OnGround && m.Settle == null;
                if (m.Winchester.Update(AmmoFraction(a) <= 0f, inFormation))
                    Events.Push(new WingEvent { Time = missionTime, Member = m.Seat, Kind = WingEventKind.Winchester, Element = (byte)ElementOf(m) });
            }
        }
    }
}
