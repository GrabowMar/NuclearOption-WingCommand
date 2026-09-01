using System.Collections.Generic;
using NuclearOption.Networking;

namespace WingCommand
{
    /// <summary>
    /// Aircraft the player has released, on their way home.
    ///
    /// Releasing a wingman used to hand it straight to the stock combat AI, which left it
    /// hunting over the battlefield for the rest of the mission. That is a poor reading of
    /// the gesture: releasing an aircraft is dismissing it, not tasking it, and the only
    /// thing the player reliably wanted was for it to stop being theirs. Worse, the airframe
    /// went on occupying a slot in the faction's AI aircraft allowance forever, so a player
    /// who released a wingman to buy a better one found they still could not afford the
    /// capacity — the release had cost them the aircraft and freed nothing.
    ///
    /// A released aircraft now flies the stock landing state home. This tracks it between
    /// the release and the runway so that two things can be true of it in the meantime: it
    /// is no longer counted against the squadron limit the shop enforces, and it is settled
    /// and despawned on arrival by <see cref="WingRecovery"/> exactly as an RTB is. Its
    /// airframe therefore comes back into stock rather than being written off, which is the
    /// same bargain the player already gets for ordering a wingman home.
    /// </summary>
    internal static class WingDeparture
    {
        /// <summary>The facts a settlement needs, captured while the member still exists.</summary>
        internal sealed class Departing
        {
            public Aircraft Aircraft;
            public PersistentID AircraftId;
            public string Name;
            public bool Owned;
            public bool LoadoutKnown;
            public WingLoadoutChoice Loadout;
        }

        private static readonly List<Departing> outbound = new List<Departing>();

        public static IReadOnlyList<Departing> Outbound => outbound;

        /// <summary>Begin tracking an aircraft that has been released and told to go home.</summary>
        public static void Begin(WingMember member)
        {
            if (member == null || member.Aircraft == null) return;
            if (Contains(member.Aircraft)) return;

            outbound.Add(new Departing
            {
                Aircraft = member.Aircraft,
                AircraftId = member.Aircraft.persistentID,
                Name = member.Name,
                Owned = WingShop.IsPurchased(member.Aircraft),
                LoadoutKnown = member.LoadoutKnown,
                Loadout = member.Loadout,
            });
        }

        /// <summary>Stop tracking one aircraft, once it has been settled or lost.</summary>
        public static void Forget(Departing departing)
        {
            if (departing != null) outbound.Remove(departing);
        }

        /// <summary>
        /// Drop anything that has stopped existing.
        ///
        /// A released aircraft can be shot down on the way home, and one that dies between
        /// passes must not go on excusing a squadron slot that is already free.
        /// </summary>
        public static void Prune()
        {
            for (int i = outbound.Count - 1; i >= 0; i--)
            {
                Aircraft aircraft = outbound[i].Aircraft;
                if (aircraft == null || aircraft.disabled) outbound.RemoveAt(i);
            }
        }

        /// <summary>
        /// True while this aircraft is a released one flying home.
        ///
        /// Read by the shop's squadron count, which walks the world's live aircraft and
        /// cannot otherwise tell a departing airframe from one still on task.
        /// </summary>
        public static bool Contains(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            for (int i = 0; i < outbound.Count; i++)
                if (outbound[i].Aircraft == aircraft) return true;
            return false;
        }

        /// <summary>How many released aircraft are still on their way home.</summary>
        public static int Count => outbound.Count;

        public static void Reset() => outbound.Clear();
    }
}
