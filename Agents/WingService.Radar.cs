using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>The WEAPONS and RADAR doctrine axes where the AI fires and searches (spec WMC rebuild R3; design
    /// wmc-rebuild/weapons-radar.md C4-C5): stations and targets a member's doctrine allows, and each member's radar kept as its
    /// doctrine wants it on the host.</summary>
    internal sealed partial class WingService
    {
        private static readonly Dictionary<WeaponInfo, bool> sarh = new Dictionary<WeaponInfo, bool>();

        /// <summary>Every tick after the fight's supervision: each member's radar on or off as its doctrine wants (SILENT: on
        /// while engaged). The actual state is compared every tick, so a rearm or a pod that comes up on is switched off.</summary>
        private void DisciplineRadars(float dt)
        {
            foreach (WingMember m in Members)
            {
                if (m.Released || !m.Alive) continue;
                Aircraft a = m.Aircraft;
                if (!a.IsServer || !(a.radar is Radar r) || r == null) continue;
                bool want = RadarDiscipline.Wanted(DoctrineFor(m).Radar, m.Engaged);
                if (!RadarDiscipline.Step(ref m.RadarGate, true, r.activated, want, dt)) continue;
                // The host's own call (CmdToggleRadar needs authority) broadcasts the new state; a dedicated server does not
                // run the RPC locally, so the field is written too (0.9 did both).
                a.UserCode_CmdToggleRadar_1821461427();
                r.activated = want;
            }
        }

        /// <summary>A member leaving the wing alive gets its radar back: the game's AI never switches one on.</summary>
        private static void RestoreRadar(WingMember m)
        {
            Aircraft a = m.Aircraft;
            if (a == null || a.disabled || a.HasEjected() || !a.IsServer || !(a.radar is Radar r) || r == null || r.activated) return;
            a.UserCode_CmdToggleRadar_1821461427();
            r.activated = true;
        }

        /// <summary>Radars switched on across the wing (automation).</summary>
        public int RadarsOn
        {
            get
            {
                int n = 0;
                foreach (WingMember m in Members)
                    if (!m.Released && m.Alive && m.Aircraft.radar is Radar r && r != null && r.activated) n++;
                return n;
            }
        }

        /// <summary>A station member <paramref name="m"/> may use under <paramref name="d"/> (loaded, and allowed).</summary>
        internal static bool UsableBy(WingMember m, in WingDoctrine d, WeaponStation w) => Usable(m.Aircraft, w) && Allowed(d, m.Engaged, w);

        private static bool Allowed(in WingDoctrine d, bool engaged, WeaponStation w)
        {
            if (w == null || w.WeaponInfo == null) return false;
            WeaponInfo i = w.WeaponInfo;
            StoreClass c = StoreClasses.Of(i.gun, i.jammer, i.missile, i.bomb || i.glideBomb, i.effectiveness.antiAir, i.effectiveness.antiSurface);
            return WeaponsFilter.AllowsStation(d.Weapons, c, Sarh(i), RadarDiscipline.Wanted(d.Radar, engaged));
        }

        /// <summary>A radar-guided missile: its prefab carries the SARH seeker (cached per weapon).</summary>
        private static bool Sarh(WeaponInfo i)
        {
            if (sarh.TryGetValue(i, out bool known)) return known;
            bool s = i.weaponPrefab != null && i.weaponPrefab.GetComponent<SARHSeeker>() != null;
            sarh[i] = s;
            return s;
        }

        internal static bool IsAir(Unit u) => u is Missile || (u != null && u.definition != null && u.definition.typeIdentity.air > 0.5f);

        /// <summary>A target member <paramref name="m"/>'s doctrine lets it choose (NO A-G: air only).</summary>
        internal bool AllowsTarget(WingMember m, Unit u) => u == null || WeaponsFilter.AllowsTarget(DoctrineFor(m).Weapons, IsAir(u));

        /// <summary>The game's target choice for a restricted member searches only the stations it may use (the target patch's
        /// prefix): <paramref name="into"/> is filled and true returned; false leaves the game's list alone.</summary>
        internal bool FilterStations(Aircraft a, List<WeaponStation> stations, List<WeaponStation> into)
        {
            WingMember m = MemberOf(a);
            if (m == null || m.Released || stations == null) return false;
            WingDoctrine d = DoctrineFor(m);
            if (!WeaponsFilter.Restricts(d.Weapons, RadarDiscipline.Wanted(d.Radar, m.Engaged))) return false;
            into.Clear();
            // The game's own list is never changed; the stations stay real ones (an empty one keeps its place for the game).
            for (int i = 0; i < stations.Count; i++)
                if (Allowed(d, m.Engaged, stations[i])) into.Add(stations[i]);
            return true;
        }

        /// <summary>A choice the member's doctrine forbids (NO A-G on a ground target) is dropped: the game chooses again.</summary>
        internal void Veto(Aircraft a, ref CombatAI.TargetSearchResults result)
        {
            if (result.target == null) return;
            WingMember m = MemberOf(a);
            if (m != null && !AllowsTarget(m, result.target)) result = new CombatAI.TargetSearchResults(null, null, 0f, result.outOfAmmo);
        }

        /// <summary>After a WEAPONS or RADAR change, engaged members in <paramref name="who"/> drop the game's current target
        /// once, so the next choice uses the new rules instead of finishing a now-forbidden attack; an assigned target the
        /// doctrine forbids is dropped too.</summary>
        public void Rechoose(System.Func<WingMember, bool> who)
        {
            foreach (WingMember m in Members)
            {
                if (!m.Engaged || m.Released || !m.Alive || (who != null && !who(m))) continue;
                PilotBaseState state = m.Pilot.currentState;
                if (state is AIPilotCombatModes plane) PlaneTarget(plane) = null;
                else if (state is AIHeloCombatState helo) HeloTarget(helo) = null;
                m.Aircraft.weaponManager?.ClearTargetList();
                if (m.AssignedTarget != null && !AllowsTarget(m, m.AssignedTarget)) Assign(m, null);
            }
        }
    }
}
