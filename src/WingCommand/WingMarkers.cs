using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// What the wing looks like on the player's displays: who is a wingman, what the wing
    /// is shooting at, and the colour each of those gets.
    ///
    /// Both the tactical map (<see cref="WingMapTint"/>) and the in-cockpit HUD
    /// (<see cref="WingHudTint"/>) draw from this, so a unit cannot be a wingman on one
    /// display and anonymous on the other. Before this existed the map had its own
    /// membership test and the HUD had nothing at all, which left the game's own
    /// nearest-ally icon as the only aircraft on the HUD with distinct symbology — one
    /// aircraft, chosen by proximity, that reads exactly like a wing designation and
    /// never is one.
    /// </summary>
    internal static class WingMarkers
    {
        internal enum Role
        {
            /// <summary>Not connected to the wing; the game's own colour stands.</summary>
            None,

            /// <summary>A wingman under the player's command.</summary>
            Member,

            /// <summary>A unit the wing is currently engaging.</summary>
            Target,
        }

        // Engaged targets change as the fight develops but not every frame, and resolving
        // them walks each member's weapon manager. Rebuilt on a timer instead.
        private const float TargetPollInterval = 0.25f;

        private static readonly List<Unit> engaged = new List<Unit>();
        private static readonly List<Unit> scratch = new List<Unit>();
        private static readonly List<Unit> repaint = new List<Unit>();
        private static float nextPoll;

        /// <summary>Units the wing is engaging, as of the last poll.</summary>
        public static IReadOnlyList<Unit> EngagedTargets => engaged;

        public static void Reset()
        {
            engaged.Clear();
            scratch.Clear();
            nextPoll = 0f;
        }

        /// <summary>
        /// Refresh the engaged-target set and repaint anything whose role changed.
        /// Called every frame; does real work four times a second.
        /// </summary>
        public static void Tick(WingRegistry wing)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + TargetPollInterval;

            CollectTargets(wing);

            if (!SameAsEngaged())
            {
                // Repaint the union of both sets, and only after the new set is in place:
                // a unit's role is resolved by looking it up in this very list, so
                // repainting a departing target while it is still listed would simply
                // paint it as a target again.
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

            // Members are repainted when membership changes, but the HUD marker for a
            // wingman is recoloured by the game for a second after it is created and
            // whenever its track goes stale, so it is reasserted on the same timer.
            WingHudTint.Reassert(wing);
        }

        private static void CollectTargets(WingRegistry wing)
        {
            scratch.Clear();
            if (wing == null || !Plugin.Config2.HighlightWingTargets.Value) return;

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

        /// <summary>
        /// What a member is shooting at. An explicitly assigned target always counts; an
        /// autonomous one only counts while the member is actually off fighting, because
        /// a weapon manager holds the last target it was given long after the engagement
        /// is over and marking that would leave stale symbols on the display.
        /// </summary>
        private static Unit TargetOf(WingMember member)
        {
            Unit assigned = member.AssignedTarget;
            if (assigned != null && !assigned.disabled) return assigned;

            if (member.Order != WingOrder.Engage && !member.OnLeash) return null;

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

        /// <summary>The role a unit plays for the player's wing, for symbology purposes.</summary>
        public static Role RoleOf(Unit unit)
        {
            if (unit == null) return Role.None;

            WingCommandManager mgr = WingCommandManager.Instance;
            if (mgr == null) return Role.None;

            // Membership wins: a wingman that is also somebody's target is still a wingman.
            if (unit is Aircraft aircraft && mgr.Wing.Contains(aircraft))
                return Plugin.Config2.HighlightWingOnMap.Value ||
                       Plugin.Config2.HighlightWingOnHud.Value
                    ? Role.Member
                    : Role.None;

            for (int i = 0; i < engaged.Count; i++)
            {
                if (engaged[i] == unit) return Role.Target;
            }

            return Role.None;
        }

        /// <summary>Repaint one unit on every display that carries wing symbology.</summary>
        public static void Repaint(Unit unit)
        {
            WingMapTint.Refresh(unit);
            WingHudTint.Refresh(unit);
        }


        // ------------------------------------------------------------------- colours

        private static Color memberColor = new Color(0.20f, 0.90f, 1f);
        private static string memberFrom;

        private static Color targetColor = new Color(1f, 0.69f, 0.13f);
        private static string targetFrom;

        /// <summary>Configured wing colour, parsed once per distinct config value.</summary>
        public static Color MemberColor
        {
            get
            {
                Parse(Plugin.Config2.WingIconColor.Value, ref memberFrom, ref memberColor,
                      new Color(0.20f, 0.90f, 1f), "WingIconColor");
                return memberColor;
            }
        }

        /// <summary>Configured colour for units the wing is engaging.</summary>
        public static Color TargetColor
        {
            get
            {
                Parse(Plugin.Config2.WingTargetColor.Value, ref targetFrom, ref targetColor,
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

        /// <summary>Selected symbology stays brighter, as it does in the stock theme.</summary>
        public static Color ColorFor(Role role, bool selected)
        {
            Color c = ColorFor(role);
            return selected ? Brighten(c, 0.35f) : c;
        }
    }
}
