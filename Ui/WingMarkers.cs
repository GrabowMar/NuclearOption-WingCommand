using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Shared wing membership and engaged-target roles for map outlines and HUD
    /// colours.</summary>
    internal static class WingMarkers
    {
        internal enum Role
        {
            /// <summary>No wing role; retain native symbology.</summary>
            None,

            /// <summary>Aircraft under wing command.</summary>
            Member,

            /// <summary>Current wing engagement target.</summary>
            Target,
        }

        // Poll weapon managers for engaged targets periodically rather than each frame.
        private const float TargetPollInterval = 0.25f;

        private static readonly List<Unit> engaged = new List<Unit>();
        private static readonly List<Unit> scratch = new List<Unit>();
        private static readonly List<Unit> repaint = new List<Unit>();
        private static float nextPoll;

        /// <summary>Engaged units from the latest poll.</summary>
        public static IReadOnlyList<Unit> EngagedTargets => engaged;

        public static void Reset()
        {
            engaged.Clear();
            scratch.Clear();
            nextPoll = 0f;
        }

        /// <summary>Poll role changes and repaint affected units four times per second.</summary>
        public static void Tick(WingRegistry wing)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + WingFidelity.Interval(TargetPollInterval);

            CollectTargets(wing);

            if (!SameAsEngaged())
            {
                // Install the new target set before repainting the union of old and new units so
                // departed targets lose their role.
                repaint.Clear();
                repaint.AddRange(engaged);
                foreach (Unit u in scratch)
                {
                    if (!repaint.Contains(u)) repaint.Add(u);
                }

                engaged.Clear();
                engaged.AddRange(scratch);

                foreach (Unit u in repaint) Repaint(u);
                repaint.Clear();
            }

            // Restore map markings after icon recreation or external UI changes.
            WingMapTint.Reassert(wing);

            // Reassert HUD tint after native creation fades and stale-track recolouring.
            WingHudTint.Reassert(wing);
        }

        private static void CollectTargets(WingRegistry wing)
        {
            scratch.Clear();
            if (wing == null || Plugin.Settings.Highlight.Value != HighlightMode.WingAndTargets) return;

            IReadOnlyList<WingMember> members = wing.Members;
            for (int i = 0; i < members.Count; i++)
            {
                WingMember m = members[i];
                if (!m.Alive) continue;

                Unit target = TargetOf(m);
                if (target == null || target.disabled) continue;
                if (!scratch.Contains(target)) scratch.Add(target);
            }
        }

        /// <summary>Prefer explicit assignments. Count autonomous weapon targets only during active combat
        /// because native managers retain old targets after engagement.</summary>
        private static Unit TargetOf(WingMember member)
        {
            if (member.IsPanicking && member.Aircraft != null)
            {
                MissileWarning warning = member.Aircraft.GetMissileWarningSystem();
                if (warning != null && warning.TryGetNearestIncoming(out Missile missile))
                    return missile;
            }

            Unit assigned = member.AssignedTarget;
            if (assigned != null && !assigned.disabled) return assigned;

            if (member.Order != WingOrder.Engage) return null;

            Aircraft aircraft = member.Aircraft;
            if (aircraft == null || aircraft.weaponManager == null) return null;

            List<Unit> list = aircraft.weaponManager.GetTargetList();
            return (list != null && list.Count > 0) ? list[0] : null;
        }

        private static bool SameAsEngaged()
        {
            if (scratch.Count != engaged.Count) return false;
            for (int i = 0; i < scratch.Count; i++)
            {
                if (!engaged.Contains(scratch[i])) return false;
            }
            return true;
        }

        /// <summary>Resolve a unit's wing symbology role.</summary>
        public static Role RoleOf(Unit unit)
        {
            if (unit == null) return Role.None;

            WingCommandManager mgr = WingCommandManager.Instance;
            if (mgr == null) return Role.None;

            // Membership takes precedence if a wingman is also targeted.
            if (unit is Aircraft aircraft && mgr.Wing.Contains(aircraft))
                return Plugin.Settings.Highlight.Value != HighlightMode.Off
                    ? Role.Member
                    : Role.None;

            for (int i = 0; i < engaged.Count; i++)
            {
                if (engaged[i] == unit) return Role.Target;
            }

            return Role.None;
        }

        /// <summary>Refresh wing symbology on every supported display for this unit.</summary>
        public static void Repaint(Unit unit)
        {
            WingMapTint.Refresh(unit);
            WingHudTint.Refresh(unit);
        }


        // Marker colours.

        private static Color memberColor = new Color(0.22f, 1f, 0.40f);
        private static string memberFrom;

        private static Color targetColor = new Color(1f, 0.69f, 0.13f);
        private static string targetFrom;

        /// <summary>Wing-member colour cached per distinct configuration value.</summary>
        public static Color MemberColor
        {
            get
            {
                Parse(Plugin.Settings.WingIconColor.Value, ref memberFrom, ref memberColor,
                      new Color(0.22f, 1f, 0.40f), "WingMemberColor");
                return memberColor;
            }
        }

        /// <summary>Configured wing-target colour.</summary>
        public static Color TargetColor
        {
            get
            {
                Parse(Plugin.Settings.WingTargetColor.Value, ref targetFrom, ref targetColor,
                      new Color(1f, 0.69f, 0.13f), "WingTargetColor");
                return targetColor;
            }
        }

        public static Color ColorFor(Role role)
        {
            return role == Role.Member ? MemberColor : TargetColor;
        }

        private static void Parse(string raw, ref string cachedFrom, ref Color cached,
                                  Color fallback, string setting)
        {
            if (raw == cachedFrom) return;

            cachedFrom = raw;
            if (!ColorUtility.TryParseHtmlString(raw, out cached))
            {
                cached = fallback;
                Plugin.Logger.LogWarning(
                    "Could not parse " + setting + " '" + raw + "'; using the default.");
            }
        }

        private static Color Brighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        /// <summary>Brighten selected markings consistently with the native theme.</summary>
        public static Color ColorFor(Role role, bool selected)
        {
            Color c = ColorFor(role);
            return selected ? Brighten(c, 0.35f) : c;
        }
    }
}
