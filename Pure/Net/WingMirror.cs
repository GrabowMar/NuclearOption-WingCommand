using System;

namespace WingCommand
{
    /// <summary>What a member is doing, as a snapshot carries it (spec M6 §8).</summary>
    internal enum MemberDuty : byte { Formation, Engaged, Recovering, Defending, Grounded, Settled }

    [Flags]
    internal enum SnapshotFlags : byte { None = 0, FallingBehind = 1, Bingo = 2, Joker = 4, Winchester = 8 }

    /// <summary>Spec M6 §8: a member as its snapshot entry.</summary>
    internal static class SnapshotBuilder
    {
        /// <summary>A 0–1 fraction as 0–255 (NaN and anything below 0 → 0, above 1 → 255).</summary>
        public static byte Fraction(float f) => f >= 1f ? (byte)255 : f > 0f ? (byte)(f * 255f + 0.5f) : (byte)0;

        public static SnapshotMember Member(uint id, int slot, byte behaviour, MemberDuty duty, float fuel, float ammo,
            bool fallingBehind, bool bingo, bool joker, bool winchester)
        {
            SnapshotFlags flags = (fallingBehind ? SnapshotFlags.FallingBehind : 0) | (bingo ? SnapshotFlags.Bingo : 0)
                | (joker ? SnapshotFlags.Joker : 0) | (winchester ? SnapshotFlags.Winchester : 0);
            return new SnapshotMember
            {
                Id = id, Slot = (byte)Math.Max(0, Math.Min(255, slot)), Behaviour = behaviour, Duty = (byte)duty,
                Fuel = Fraction(fuel), Ammo = Fraction(ammo), Flags = (byte)flags,
            };
        }
    }

    /// <summary>Spec M6 §8: a client's copy of its wing — the newest snapshot of its owner (an older or repeated tick is
    /// ignored: the unreliable channel reorders), stale after <see cref="StaleSeconds"/> without one.</summary>
    internal sealed class WingMirror
    {
        public static float StaleSeconds = 3f;

        public readonly uint Owner;
        private readonly SnapshotMember[] members = new SnapshotMember[WcSnapshot.MaxMembers];
        private uint tick;
        private bool any;
        private float at;

        public WingMirror(uint owner) => Owner = owner;

        public int Count { get; private set; }
        public SnapshotMember this[int i] => members[i];

        public bool Apply(in WcSnapshot s, float now)
        {
            if (s.Owner != Owner || s.Members == null || (any && s.Tick <= tick)) return false;
            tick = s.Tick;
            any = true;
            at = now;
            Count = Math.Min(s.Members.Length, members.Length);
            for (int i = 0; i < Count; i++) members[i] = s.Members[i];
            return true;
        }

        public bool Stale(float now) => !any || now - at > StaleSeconds;
    }
}
