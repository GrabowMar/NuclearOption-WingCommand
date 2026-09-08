using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Shares expiring target reservations across combat paths. Native AI accounts for missiles
    /// in flight but not simultaneous target selections; short claims spread fire without permanently
    /// locking targets.</summary>
    internal static class TacticalCoordinator
    {
        private sealed class Claim
        {
            public Aircraft Owner;
            public float Until;
            public bool Firing;
        }

        private static readonly Dictionary<Unit, List<Claim>> claims =
            new Dictionary<Unit, List<Claim>>();
        private static readonly List<Unit> staleTargets = new List<Unit>();
        private static readonly List<Aircraft> owners = new List<Aircraft>();
        private static int lastPrunedFrame = -1;

        public static void Reset()
        {
            claims.Clear();
            staleTargets.Clear();
            owners.Clear();
            lastPrunedFrame = -1;
        }

        /// <summary>Count live firing claims; target assignments do not consume shots.</summary>
        public static int CountClaims(Unit target, Aircraft except = null)
        {
            if (target == null || target.disabled) return 0;

            Prune();
            int count = 0;
            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Firing && Active(list[i]) && list[i].Owner != except) count++;
            }
            return count;
        }

        /// <summary>Count firing claims, native selections, and active explicit attack
        /// assignments.</summary>
        public static int CountCommitments(Unit target, Aircraft except = null)
        {
            if (target == null || target.disabled) return 0;

            Prune();
            owners.Clear();

            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                    if (Active(list[i])) AddOwner(list[i].Owner, except);
            }

            // Only active attack assignments add selection pressure. Retained targets during recall,
            // defence, or taxi do not. Assignments never consume the hard firing cap; actual shots
            // retain their own claims.
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing != null)
            {
                IReadOnlyList<WingMember> members = wing.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    WingMember member = members[i];
                    if (member != null && member.Alive && !member.DeliveryPending &&
                        member.AssignedTarget == target &&
                        member.EngagementAuthority == OrderEngagementAuthority.ExplicitTarget)
                        AddOwner(member.Aircraft, except);
                }
            }

            return owners.Count;
        }

        /// <summary>Record native selection pressure without consuming a firing slot; selection alone does
        /// not prove the aircraft can shoot.</summary>
        public static void NoteSelection(Unit target, Aircraft owner, float seconds)
        {
            if (owner == null) return;

            Prune();
            bool valid = target != null && !target.disabled && !owner.disabled;
            if (valid && claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Firing || list[i].Owner != owner) continue;
                    list[i].Until = Time.timeSinceLevelLoad + seconds;
                    return;
                }
            }
            // Native callers replace one current target. A switch or empty search must not keep
            // discouraging teammates from targets this pilot abandoned; existing shots still count.
            ReleaseSelection(owner);
            if (valid) AddClaim(target, owner, seconds, firing: false);
        }

        /// <summary>Claim a firing slot below the target limit, or renew this owner's existing claim even
        /// at capacity.</summary>
        public static bool TryClaim(Unit target, Aircraft owner, int maximum, float seconds)
        {
            if (target == null || target.disabled || owner == null || owner.disabled || maximum <= 0)
                return false;

            Prune();
            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].Firing || list[i].Owner != owner || !Active(list[i])) continue;
                    list[i].Until = Time.timeSinceLevelLoad + seconds;
                    return true;
                }
            }

            if (CountClaims(target, owner) >= maximum) return false;

            AddClaim(target, owner, seconds, firing: true);
            return true;
        }

        private static void AddClaim(Unit target, Aircraft owner, float seconds, bool firing)
        {
            if (!claims.TryGetValue(target, out List<Claim> list))
            {
                list = new List<Claim>();
                claims.Add(target, list);
            }

            list.Add(new Claim
            {
                Owner = owner,
                Until = Time.timeSinceLevelLoad + seconds,
                Firing = firing,
            });
        }

        /// <summary>Abandon target selection while retaining reservations for shots already fired.</summary>
        public static void ReleaseSelection(Aircraft owner) => Release(owner, selectionOnly: true);

        public static void Release(Aircraft owner) => Release(owner, selectionOnly: false);

        private static void Release(Aircraft owner, bool selectionOnly)
        {
            if (owner == null) return;

            foreach (KeyValuePair<Unit, List<Claim>> pair in claims)
            {
                List<Claim> list = pair.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Owner == owner && (!selectionOnly || !list[i].Firing))
                        list.RemoveAt(i);
                }
            }
            Prune();
        }

        private static void AddOwner(Aircraft owner, Aircraft except)
        {
            if (owner == null || owner.disabled || owner == except || owners.Contains(owner)) return;
            owners.Add(owner);
        }

        private static bool Active(Claim claim) =>
            claim.Owner != null && !claim.Owner.disabled && claim.Until > Time.timeSinceLevelLoad;

        private static void Prune()
        {
            // Sweep globally once per frame. Target-local reads still check expiry and liveness for
            // changes within the frame.
            if (lastPrunedFrame == Time.frameCount) return;
            lastPrunedFrame = Time.frameCount;
            float now = Time.timeSinceLevelLoad;
            staleTargets.Clear();

            foreach (KeyValuePair<Unit, List<Claim>> pair in claims)
            {
                Unit target = pair.Key;
                List<Claim> list = pair.Value;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Claim claim = list[i];
                    if (claim.Owner == null || claim.Owner.disabled || claim.Until <= now)
                        list.RemoveAt(i);
                }

                if (target == null || target.disabled || list.Count == 0)
                    staleTargets.Add(target);
            }

            for (int i = 0; i < staleTargets.Count; i++) claims.Remove(staleTargets[i]);
        }
    }
}
