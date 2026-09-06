using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Short-lived target reservations shared by every combat path in this plugin.
    ///
    /// Stock AI evaluates targets independently. It accounts for missiles already in the
    /// air, but not for the other aircraft that have just selected the same contact, so a
    /// whole package can make the same locally-correct choice. Reservations turn those
    /// independent decisions into a flight-level allocation without permanently locking a
    /// target to anyone: if an aircraft cannot prosecute, its claim expires in seconds.
    /// </summary>
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

        /// <summary>Active firing reservations; an assignment alone never consumes a shot.</summary>
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

        /// <summary>Selection pressure from shooters, native selections and explicit attack assignments.</summary>
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

            // Assignments discourage opportunists from piling onto an existing attack,
            // but are not the hard firing cap: otherwise two assigned pilots can each
            // wait forever for the other's reservation without either firing a shot.
            WingRegistry wing = WingCommandManager.Instance?.Wing;
            if (wing != null)
            {
                IReadOnlyList<WingMember> members = wing.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    WingMember member = members[i];
                    if (member.AssignedTarget == target &&
                        (member.Order == WingOrder.Attack || member.Order == WingOrder.FireForEffect))
                        AddOwner(member.Aircraft, except);
                }
            }

            return owners.Count;
        }

        /// <summary>
        /// Record a native AI target choice for deconfliction. Selection does not prove
        /// that the aircraft can fire yet, so it must not consume the hard firing cap.
        /// </summary>
        public static void NoteSelection(Unit target, Aircraft owner, float seconds)
        {
            if (target == null || target.disabled || owner == null || owner.disabled) return;

            Prune();
            if (claims.TryGetValue(target, out List<Claim> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Firing || list[i].Owner != owner) continue;
                    list[i].Until = Time.timeSinceLevelLoad + seconds;
                    return;
                }
            }
            AddClaim(target, owner, seconds, firing: false);
        }

        /// <summary>
        /// Reserve a target if its concurrency limit has room. An existing owner may renew
        /// its own claim even while the target is full.
        /// </summary>
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

        public static void Release(Aircraft owner)
        {
            if (owner == null) return;

            foreach (KeyValuePair<Unit, List<Claim>> pair in claims)
            {
                List<Claim> list = pair.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Owner == owner) list.RemoveAt(i);
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
            // Target searches ask once per candidate and weapon. The global sweep only
            // needs to run once per frame; target-local reads still check liveness and
            // expiry so changes later in the same frame are immediately visible.
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
