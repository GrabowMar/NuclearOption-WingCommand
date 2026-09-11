using System;

namespace WingCommand
{
    /// <summary>Engine-free runway selection and spawn-offset rules. WingAirfield handles transforms. Keep
    /// offset within native taxi handoff range while allowing tail clearance; placement too far forward
    /// makes taxi steer back toward the threshold.</summary>
    internal static class LaunchGeometry
    {
        /// <summary>Requested tail clearance from the pavement end.</summary>
        public const float ThresholdMargin = 8f;

        /// <summary>Keep both aircraft footprints clear, including a larger existing aircraft or wreck.</summary>
        public static float SpawnClearance(float spawningSize, float existingSize) =>
            Math.Max(14f, (Math.Max(0f, spawningSize) + Math.Max(0f, existingSize)) * 0.5f + ThresholdMargin);

        /// <summary>Only the host clears wrecks or orphaned debris; parts of live units remain intact.</summary>
        public static bool CanClearRunwayDebris(bool isHost, bool hasOwner, bool ownerDisabled,
                                               bool detached) =>
            isHost && (hasOwner ? ownerDisabled : detached);

        /// <summary>Maximum along-strip spawn offset. Native taxi's 12 m handoff distance includes
        /// vertical spawn height, so reserve part of that budget for tall aircraft.</summary>
        public const float MaximumThresholdOffset = 10f;

        /// <summary>Minimum land-runway length before airframe-specific requirements.</summary>
        public const float MinimumRunwayLength = 200f;

        /// <summary>Place the aircraft centre beyond the threshold using footprint and margin, capped for
        /// taxi handoff. Accept large-airframe tail overhang rather than steering backward; native runway
        /// detection uses fuselage position.</summary>
        public static float ThresholdOffset(float length, float width)
        {
            float half = Math.Max(Math.Max(length, width), 0f) * 0.5f;
            return Math.Min(MaximumThresholdOffset, half + ThresholdMargin);
        }

        /// <summary>Validate land-strip takeoff flag, length, and slope. Carrier catapults trust the
        /// native takeoff flag without land length/slope limits; their launch energy and moving decks
        /// differ.</summary>
        public static bool IsUsable(bool takeoff, float runwayLength, float takeoffRun,
                                    bool level, bool catapult = false)
        {
            if (!takeoff) return false;
            if (catapult) return true;
            if (runwayLength < MinimumRunwayLength) return false;
            if (!level) return false;
            return runwayLength >= Math.Max(0f, takeoffRun) + MaximumThresholdOffset;
        }

        /// <summary>Estimate ground roll from nominal v²/(2a) to reject obviously short strips. This is a
        /// coarse compatibility gate, not a predicted native rotation point.</summary>
        public static float TakeoffRun(float takeoffSpeed)
        {
            float v = Math.Max(0f, takeoffSpeed);
            return v * v / (2f * NominalTakeoffAcceleration);
        }

        private const float NominalTakeoffAcceleration = 4f;

        /// <summary>Native 30-second runway-heading retention window; spawn and takeoff must agree during
        /// it.</summary>
        public const float OperatingDirectionHold = 30f;

        /// <summary>Whether recent runway usage still locks its direction.</summary>
        public static bool OperatingDirectionLocked(float secondsSinceLastUsed) =>
            secondsSinceLastUsed < OperatingDirectionHold;

        /// <summary>Choose the nearer permitted runway end with deterministic tie handling.</summary>
        public static bool PreferReverse(float distanceToStart, float distanceToEnd,
                                         bool reversable)
        {
            if (!reversable) return false;
            return distanceToEnd < distanceToStart;
        }

        /// <summary>Choose the runway end while preserving any active native direction lock.</summary>
        public static bool PreferReverse(float distanceToStart, float distanceToEnd,
                                         bool reversable, bool operatingLocked,
                                         bool currentlyReversed)
        {
            if (operatingLocked) return currentlyReversed;
            return PreferReverse(distanceToStart, distanceToEnd, reversable);
        }

        /// <summary>Confirm actual runway presence and alignment after spawn physics, before permitting
        /// native takeoff.</summary>
        public static bool OnRunway(bool aircraftOnRunway, float headingDot) =>
            aircraftOnRunway && headingDot >= HeadingTolerance;

        /// <summary>Minimum heading dot product for native full-power takeoff alignment.</summary>
        public const float HeadingTolerance = 0.95f;
    }
}
