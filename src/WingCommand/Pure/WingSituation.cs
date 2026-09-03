namespace WingCommand
{
    /// <summary>
    /// Everything a reflex is allowed to know about one wingman, sampled once per
    /// arbitration pass.
    ///
    /// A snapshot rather than a live reference, and engine-free by construction: a reflex
    /// cannot reach through this to the aircraft, cannot switch a pilot state, and cannot
    /// write anything. That is the whole safety story for third-party reflexes — the worst
    /// a badly written one can do is return a wrong number.
    ///
    /// Every field is optional at the call site. Tests name only what they are exercising,
    /// which keeps a scoring test to one readable line instead of fourteen positional
    /// arguments of noise.
    /// </summary>
    public readonly struct WingSituation
    {
        /// <summary>The standing order — what the player actually asked for.</summary>
        public readonly WingOrder Order;

        /// <summary>The wing's standing weapons policy.</summary>
        public readonly WingRoe Roe;

        /// <summary>Still under the airbase's taxi/launch AI after a hangar delivery.</summary>
        public readonly bool DeliveryPending;

        /// <summary>A missile is in the air and this aircraft is its target.</summary>
        public readonly bool MissileWarned;

        /// <summary>
        /// Seconds since the warning last read true, and 0 while it still does. The missile
        /// break scores on this rather than on the bare flag: a warning that drops for a
        /// tick as the missile is re-acquired must not end the break.
        /// </summary>
        public readonly float SecondsSinceMissileWarning;

        /// <summary>The leader is on the runway rather than merely low.</summary>
        public readonly bool LeaderOnDeck;

        /// <summary>False when the leader is gone; several reflexes have nothing to say then.</summary>
        public readonly bool LeaderPresent;

        /// <summary>The directive carries a live unit to prosecute.</summary>
        public readonly bool TargetAlive;

        /// <summary>Metres to the leader. Negative when there is no leader to measure against.</summary>
        public readonly float LeaderDistance;

        /// <summary>The leash this wingman is being held to, in metres.</summary>
        public readonly float LeashRadius;

        /// <summary>Height above ground, metres.</summary>
        public readonly float RadarAlt;

        /// <summary>
        /// This wingman has no autopilot - it is a ship or a ground vehicle.
        ///
        /// Here because a reflex has no other way to find out: the snapshot is engine-free
        /// by design and carries no aircraft to interrogate. A reflex that resolves a
        /// surface member to a flying behaviour would resolve it to nothing at all.
        /// </summary>
        public readonly bool MemberIsSurface;

        /// <summary>Fuel remaining, 0-1.</summary>
        public readonly float Fuel;

        /// <summary>Rounds and missiles remaining across every non-cargo station.</summary>
        public readonly int Ammo;

        /// <summary>Airframe condition from the game's own part hit points, 0-1.</summary>
        public readonly float Integrity;

        /// <summary>
        /// How long the currently winning reflex has been in control. Drives the minimum-hold
        /// rule; a reflex reads it to know whether it is being entered or sustained.
        /// </summary>
        public readonly float SecondsInBehaviour;

        /// <summary>
        /// A benign situation: airborne, leader present, nothing shooting at us.
        ///
        /// Explicit, and not merely a consequence of the optional parameters below. A
        /// constructor whose arguments are all optional is <b>not</b> a parameterless one:
        /// <c>new WingSituation()</c> zero-initialises the struct and skips every default,
        /// which quietly produced a wingman with no leader, no fuel and no altitude. That is
        /// the opposite of benign, and it is exactly the shape a test writes when it means
        /// "nothing interesting is happening".
        /// </summary>
        public WingSituation() : this(order: WingOrder.Formation) { }

        public WingSituation(
            WingOrder order = WingOrder.Formation,
            WingRoe roe = WingRoe.Hold,
            bool deliveryPending = false,
            bool missileWarned = false,
            float secondsSinceMissileWarning = 999f,
            bool leaderOnDeck = false,
            bool leaderPresent = true,
            bool targetAlive = false,
            float leaderDistance = 0f,
            float leashRadius = 0f,
            float radarAlt = 1000f,
            bool memberIsSurface = false,
            float fuel = 1f,
            int ammo = 1,
            float integrity = 1f,
            float secondsInBehaviour = 0f)
        {
            Order = order;
            Roe = roe;
            DeliveryPending = deliveryPending;
            MissileWarned = missileWarned;
            SecondsSinceMissileWarning = missileWarned ? 0f : secondsSinceMissileWarning;
            LeaderOnDeck = leaderOnDeck;
            LeaderPresent = leaderPresent;
            TargetAlive = targetAlive;
            LeaderDistance = leaderDistance;
            LeashRadius = leashRadius;
            RadarAlt = radarAlt;
            MemberIsSurface = memberIsSurface;
            Fuel = fuel;
            Ammo = ammo;
            Integrity = integrity;
            SecondsInBehaviour = secondsInBehaviour;
        }

    }
}
