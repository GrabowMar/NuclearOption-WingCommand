namespace WingCommand
{
    /// <summary>Engine-free immutable reflex snapshot sampled once per decision, with no aircraft or
    /// order-mutation access. Optional constructor inputs let callers specify only relevant
    /// telemetry.</summary>
    public readonly struct WingSituation
    {
        /// <summary>Player's retained standing order.</summary>
        public readonly WingOrder Order;

        /// <summary>Standing wing ROE.</summary>
        public readonly WingRoe Roe;

        /// <summary>Native delivery taxi/launch still owns this aircraft.</summary>
        public readonly bool DeliveryPending;

        /// <summary>An airborne missile targets this member.</summary>
        public readonly bool MissileWarned;

        /// <summary>Seconds since a live warning, zero while active; bridges brief tracking gaps during
        /// defence.</summary>
        public readonly float SecondsSinceMissileWarning;

        /// <summary>Leader is grounded or landing rather than merely flying low.</summary>
        public readonly bool LeaderOnDeck;

        /// <summary>Whether a usable leader exists.</summary>
        public readonly bool LeaderPresent;

        /// <summary>Whether the directive's designated unit is alive.</summary>
        public readonly bool TargetAlive;

        /// <summary>Leader distance in metres; negative when unavailable.</summary>
        public readonly float LeaderDistance;

        /// <summary>Pursuit leash radius in metres.</summary>
        public readonly float LeashRadius;

        /// <summary>Radar altitude in metres AGL.</summary>
        public readonly float RadarAlt;

        /// <summary>Member lacks an autopilot and requires surface control; exposes that capability
        /// without live engine references.</summary>
        public readonly bool MemberIsSurface;

        /// <summary>Whether the member uses rotary flight control.</summary>
        public readonly bool MemberIsRotary;

        /// <summary>Forward airspeed in m/s.</summary>
        public readonly float Airspeed;

        /// <summary>Rotation/takeoff speed in m/s.</summary>
        public readonly float TakeoffSpeed;

        /// <summary>Remaining fuel fraction from 0 to 1.</summary>
        public readonly float Fuel;

        /// <summary>Total non-cargo ammunition.</summary>
        public readonly int Ammo;

        /// <summary>Native part-based integrity fraction from 0 to 1.</summary>
        public readonly float Integrity;

        /// <summary>Seconds the winning reflex has held control, used for lifecycle and minimum-hold
        /// decisions.</summary>
        public readonly float SecondsInBehaviour;
        public readonly float SecondsWithoutEngagement;

        /// <summary>Current native terrain warning and motion used for recovery; zero defaults keep
        /// older providers' snapshots compatible.</summary>
        public readonly float TerrainUrgency, VerticalSpeed, BankAngle, MinimumAirspeed;
        public readonly bool RecoveringFromDefence;

        private WingSituation(in WingSituation basis, float terrainUrgency, float verticalSpeed,
            float bankAngle, float minimumAirspeed, bool recoveringFromDefence)
        {
            this = basis;
            TerrainUrgency = terrainUrgency;
            VerticalSpeed = verticalSpeed;
            BankAngle = bankAngle;
            MinimumAirspeed = minimumAirspeed;
            RecoveringFromDefence = recoveringFromDefence;
        }

        public WingSituation WithFlightSafety(float terrainUrgency, float verticalSpeed,
            float bankAngle, float minimumAirspeed, bool recoveringFromDefence = false) =>
            new WingSituation(in this, terrainUrgency, verticalSpeed, bankAngle,
                minimumAirspeed, recoveringFromDefence);

        // Preserve the existing public constructor signature for plugin compatibility.
        private WingSituation(in WingSituation basis, float secondsWithoutEngagement)
        {
            this = basis;
            SecondsWithoutEngagement = System.Math.Max(0f, secondsWithoutEngagement);
        }

        public WingSituation WithEngagementIdle(float seconds) => new WingSituation(in this, seconds);

        /// <summary>Explicit benign default: airborne, leader present, no missile warning. Optional
        /// arguments alone do not supply a struct's parameterless construction defaults.</summary>
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
            bool memberIsRotary = false,
            float airspeed = 150f,
            float takeoffSpeed = 70f,
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
            MemberIsRotary = memberIsRotary;
            Airspeed = airspeed;
            TakeoffSpeed = takeoffSpeed;
            Fuel = fuel;
            Ammo = ammo;
            Integrity = integrity;
            SecondsInBehaviour = secondsInBehaviour;
            SecondsWithoutEngagement = 0f;
            TerrainUrgency = VerticalSpeed = BankAngle = MinimumAirspeed = 0f;
            RecoveringFromDefence = false;
        }

    }
}
