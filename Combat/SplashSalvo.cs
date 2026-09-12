using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>A finite saturation salvo. Only stores in range when committed join the salvo;
    /// cooldowns and defensive interruptions never replace that selection with closer-range stores.</summary>
    internal sealed class SplashSalvo
    {
        private readonly List<WeaponStation> stations = new List<WeaponStation>();
        private readonly List<Unit> targets = new List<Unit>();
        private int targetIndex;

        public int StationCount => stations.Count;

        public void Begin(Aircraft aircraft, IReadOnlyList<Unit> designated, int firstTarget)
        {
            stations.Clear();
            targets.Clear();
            targetIndex = firstTarget;
            foreach (Unit target in designated)
                if (target != null && !target.disabled && !targets.Contains(target)) targets.Add(target);
            if (aircraft.weaponStations == null) return;
            foreach (WeaponStation station in aircraft.weaponStations)
                foreach (Unit target in targets)
                    if (WingWeapons.CanReach(aircraft, station, target))
                    {
                        stations.Add(station);
                        break;
                    }
        }

        /// <summary>Visit every station on every tick. Return remaining useful stores, including
        /// those waiting for reload, safety, seeker alignment, or native turret lock.</summary>
        public bool Tick(Aircraft aircraft, Pilot pilot, out Unit aimTarget, out WeaponStation aimStation)
        {
            aimTarget = null;
            aimStation = null;
            foreach (WeaponStation station in stations)
            {
                Unit retained = station.HasTurret() ? station.GetStationTarget() : null;
                int start = retained != null && targets.Contains(retained) && !retained.disabled
                    ? targets.IndexOf(retained) : targetIndex;
                for (int attempt = 0; attempt < targets.Count; attempt++)
                {
                    int index = (start + attempt) % targets.Count;
                    Unit target = targets[index];
                    if (!WingWeapons.CanDamage(station, target)) continue;
                    if (aimTarget == null)
                    {
                        aimTarget = target;
                        aimStation = station;
                    }
                    if (!WingWeapons.FireSaturation(aircraft, pilot, station, target)) continue;
                    targetIndex = (index + 1) % targets.Count;
                    break;
                }
            }
            return aimTarget != null;
        }
    }
}
