using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Tracks active ordnance released by wingmen to display time-of-flight countdowns
    /// and splash confirmations in the cockpit HUD.
    /// Non-allocating and capped to prevent runaway lists.
    /// </summary>
    internal static class WingDeliveryTracker
    {
        private sealed class Delivery
        {
            public Aircraft Shooter;
            public Unit Target;
            public string WeaponName;
            public float ImpactTime;
            public bool Splashed;
            public float SplashUntil;
        }

        private const int MaxDeliveries = 16;
        private static readonly List<Delivery> active = new List<Delivery>(MaxDeliveries);

        public static void Reset()
        {
            active.Clear();
        }

        public static void TrackShot(Aircraft shooter, Unit target, string weaponName, float estimatedTof)
        {
            if (shooter == null) return;

            float now = Time.timeSinceLevelLoad;
            float impact = now + Mathf.Clamp(estimatedTof, 1f, 90f);

            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Shooter == shooter && active[i].Target == target)
                {
                    active[i].WeaponName = string.IsNullOrEmpty(weaponName) ? "ORD" : weaponName.ToUpperInvariant();
                    active[i].ImpactTime = impact;
                    active[i].Splashed = false;
                    return;
                }
            }

            if (active.Count >= MaxDeliveries)
            {
                active.RemoveAt(0);
            }

            active.Add(new Delivery
            {
                Shooter = shooter,
                Target = target,
                WeaponName = string.IsNullOrEmpty(weaponName) ? "ORD" : weaponName.ToUpperInvariant(),
                ImpactTime = impact,
                Splashed = false,
                SplashUntil = 0f,
            });
        }

        public static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Delivery d = active[i];
                if (d.Shooter == null || !d.Shooter.gameObject.activeInHierarchy)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (!d.Splashed && d.Target != null && d.Target.disabled)
                {
                    d.Splashed = true;
                    d.SplashUntil = now + 3f;
                }

                if (d.Splashed)
                {
                    if (now >= d.SplashUntil)
                    {
                        active.RemoveAt(i);
                    }
                }
                else if (now > d.ImpactTime + 3f)
                {
                    active.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Compact HUD delivery tag for this aircraft, e.g. "AGM-98 12s" or "SPLASH".
        /// </summary>
        public static string GetDeliveryTag(Aircraft aircraft)
        {
            if (aircraft == null) return null;
            float now = Time.timeSinceLevelLoad;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                Delivery d = active[i];
                if (d.Shooter != aircraft) continue;

                if (d.Splashed)
                {
                    return "SPLASH";
                }

                int remaining = Mathf.Max(0, Mathf.CeilToInt(d.ImpactTime - now));
                return d.WeaponName + " " + remaining + "s";
            }

            return null;
        }
    }
}
