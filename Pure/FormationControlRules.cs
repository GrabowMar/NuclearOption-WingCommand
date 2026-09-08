using System;

namespace WingCommand
{
    internal static class FormationControlRules
    {
     /// <summary>Build vertical aim over the horizontal baseline using slot climb speed, not leader
     /// flight-path angle. Keep damping independent of pursuit distance.</summary>
        public static float VerticalAimRise(float horizontalDistance, float horizontalSpeed,
            float lookAhead, float slotClimb, float verticalCorrection) =>
            Math.Max(0f, horizontalDistance) *
            (slotClimb / Math.Max(1f, horizontalSpeed) + verticalCorrection / Math.Max(1f, lookAhead));

        // Account for native altitude/vertical bank amplification up to 1.2 each; do not compensate
        // protective low-speed or low-altitude reductions.
        public static float BankInput(float desiredDegrees, float radarAltitude)
        {
            float altitudeFactor = Math.Max(0.6f, Math.Min(1.2f, radarAltitude * 0.003f - 1f));
            return Math.Max(0f, desiredDegrees) / Math.Max(1f, altitudeFactor * 1.2f);
        }

        public static float HorizontalAngle(float vx, float vz, float ax, float az)
        {
            double length = Math.Sqrt(((double)vx * vx + (double)vz * vz) *
                                      ((double)ax * ax + (double)az * az));
            if (length < 1e-6) return 0f;
            double dot = ((double)vx * ax + (double)vz * az) / length;
            return (float)(Math.Acos(Math.Max(-1d, Math.Min(1d, dot))) * 180d / Math.PI);
        }

        public static bool CollisionThreat(float missSquared, float radius) =>
            radius > 0f && missSquared >= 0f && missSquared < radius * radius;

        public static void EscapeDirection(float closestX, float closestY, float closestZ,
            float relativeVx, float relativeVz, int pairOrder, out float x, out float y, out float z)
        {
            x = -closestX;
            y = -closestY;
            z = -closestZ;
            float length = (float)Math.Sqrt(x * x + y * y + z * z);
            if (length < 1f)
            {
                // At coincident closest approach, use a relative-velocity normal so the pair chooses
                // opposite horizontal escapes.
                x = -relativeVz;
                y = 0f;
                z = relativeVx;
                length = (float)Math.Sqrt(x * x + z * z);
                if (length < 1f)
                {
                    x = pairOrder < 0 ? -1f : 1f;
                    z = 0f;
                    length = 1f;
                }
            }
            x /= length;
            y /= length;
            z /= length;
        }

     /// <summary>Rotate rejoin direction mainly through horizontal heading while bounding pitch against
     /// zoom climbs and terrain dives.</summary>
        public static void SafeRejoinDirection(
            float curDirX, float curDirY, float curDirZ,
            float reqX, float reqY, float reqZ,
            float allowedAngleDeg,
            float maxPitchUpDeg,
            float maxPitchDownDeg,
            float radarAlt,
            out float outX, out float outY, out float outZ)
        {
            // Horizontal heading.
            double curHLen = Math.Sqrt((double)curDirX * curDirX + (double)curDirZ * curDirZ);
            double chX = curHLen > 1e-6 ? curDirX / curHLen : 0.0;
            double chZ = curHLen > 1e-6 ? curDirZ / curHLen : 1.0;

            double reqHLen = Math.Sqrt((double)reqX * reqX + (double)reqZ * reqZ);
            double rotHX;
            double rotHZ;

            if (reqHLen < 1e-4)
            {
                // Preserve heading when the target is vertically aligned.
                rotHX = chX;
                rotHZ = chZ;
            }
            else
            {
                double rhX = reqX / reqHLen;
                double rhZ = reqZ / reqHLen;

                // Compute signed horizontal turn from cross and dot products.
                double crossY = chZ * rhX - chX * rhZ;
                double dot = Math.Max(-1.0, Math.Min(1.0, chX * rhX + chZ * rhZ));
                double angleRad = Math.Atan2(crossY, dot);

                // Choose right for an ambiguous directly rearward target.
                if (Math.Abs(Math.Abs(angleRad) - Math.PI) < 1e-4 && Math.Abs(crossY) < 1e-4)
                {
                    angleRad = Math.PI;
                }

                double maxTurnRad = Math.Max(0.0, allowedAngleDeg) * (Math.PI / 180.0);
                double turnRad = Math.Max(-maxTurnRad, Math.Min(maxTurnRad, angleRad));

                double cosA = Math.Cos(turnRad);
                double sinA = Math.Sin(turnRad);
                rotHX = chX * cosA + chZ * sinA;
                rotHZ = -chX * sinA + chZ * cosA;
            }

            // Vertical pitch.
            double safeReqHLen = Math.Max(1.0, reqHLen);
            double pitchRad = Math.Atan2(reqY, safeReqHLen);
            double pitchDeg = pitchRad * (180.0 / Math.PI);

            double maxUp = Math.Max(0.0, maxPitchUpDeg);
            double maxDown = Math.Max(0.0, maxPitchDownDeg);

            // Reduce allowed descent below 250 m radar altitude.
            if (radarAlt < 250f)
            {
                float floorScale = Math.Max(0f, Math.Min(1f, (radarAlt - 60f) / 190f));
                maxDown *= floorScale;
            }

            pitchDeg = Math.Max(-maxDown, Math.Min(maxUp, pitchDeg));

            // Forbid commanded descent below 60 m radar altitude.
            if (radarAlt < 60f && pitchDeg < 0.0)
            {
                pitchDeg = 0.0;
            }

            double clampedPitchRad = pitchDeg * (Math.PI / 180.0);
            double cosPitch = Math.Cos(clampedPitchRad);
            double sinPitch = Math.Sin(clampedPitchRad);

            outX = (float)(rotHX * cosPitch);
            outY = (float)sinPitch;
            outZ = (float)(rotHZ * cosPitch);
        }

     /// <summary>Cap closure by the speed the remaining along-track gap can shed at the supplied
     /// deceleration.</summary>
        public static float RejoinClosure(
            float gap, float closing, float maxDecel, float aggression, float damping,
            float gapGain, float closingDamp, float maxStationClosure, float responseSeconds = 0f)
        {
            float rawClosure = gapGain * gap * aggression - closingDamp * closing * damping;
            float responseLoss = Math.Max(0f, maxDecel) * Math.Max(0f, responseSeconds);
            float overspeedCap = (float)Math.Sqrt(responseLoss * responseLoss +
                2f * Math.Max(0f, maxDecel) * Math.Max(gap, 0f)) - responseLoss;

            // Allow bounded overspeed only behind the slot; ahead, cap positive closure at zero. Limit
            // negative demand to avoid excessive slowing.
            return Math.Max(-maxStationClosure, Math.Min(overspeedCap, rawClosure));
        }

        /// <summary>
        /// Restricts bank authority to wings-level when the aircraft needs downward pitch authority
        /// to arrest a climb or execute a descent.
        /// 
        /// In the native AutopilotPlane.AutoAim implementation, if the aim direction is lower in pitch
        /// than the current flight path, AutoAim calculates a desired bank of 120° to 180° (attempting to
        /// roll inverted to pull positive Gs downward). Because bankAllowed clamps this roll, the aircraft
        /// locks into a steep knife-edge bank (up to bankAllowed). In knife-edge flight, the elevator is
        /// oriented horizontally and AutoAim suppresses elevator authority by up to 90% due to roll error,
        /// completely disabling the aircraft's ability to pitch down and trapping it in a runaway climb.
        /// 
        /// Clamping bankAllowed to near-zero (level bank) forces AutoAim to keep wings level, prevents elevator
        /// suppression, and gives the elevator 100% downward pitch authority to bunt down immediately.
        /// </summary>
        public static float PitchDownBankAuthority(
            float currentPitchDeg, float demandedPitchDeg,
            float verticalSpeed, float verticalError,
            float requestedBankDeg, float levelBankDeg)
        {
            float pitchDeficit = currentPitchDeg - demandedPitchDeg;
            bool divergingClimb = verticalSpeed > 2f && verticalError < -5f;

            if (divergingClimb || pitchDeficit > 0f)
            {
                // When pitch deficit is >= 3 degrees, full collapse to levelBank.
                // If diverging in climb (e.g. climbing at > 5 m/s above slot), collapse immediately.
                float deficitScale = Math.Max(0f, Math.Min(1f, pitchDeficit / 3f));
                if (divergingClimb)
                {
                    float climbScale = Math.Max(0f, Math.Min(1f, verticalSpeed / 10f));
                    deficitScale = Math.Max(deficitScale, Math.Max(0.5f, climbScale));
                }
                float safeExcess = Math.Max(0f, requestedBankDeg - levelBankDeg);
                return levelBankDeg + safeExcess * (1f - deficitScale);
            }

            return requestedBankDeg;
        }

        /// <summary>
        /// Kinematically-limited vertical correction. Prevents high-speed climb or dive overshoot
        /// when closing on slot altitude by anticipating the stopping distance (v^2 / 2a) required
        /// to round out smoothly.
        /// </summary>
        public static float KinematicVerticalCorrection(
            float verticalGap, float verticalDrift, float maxCorrection,
            float positionGain, float driftDamping, float aggression, float damping,
            float maxDecel = 6.0f)
        {
            float rawCorrection = (verticalGap * positionGain * aggression)
                                  - (verticalDrift * driftDamping * damping);

            if (verticalGap > 0f)
            {
                // Slot is above: vCap is the max safe closing climb rate (relative to slot)
                // that can be shed before overshooting the slot.
                float vCap = (float)Math.Sqrt(2f * Math.Max(0.1f, maxDecel) * verticalGap);
                if (verticalDrift > vCap)
                {
                    float excess = verticalDrift - vCap;
                    rawCorrection -= excess * driftDamping * damping;
                }
            }
            else if (verticalGap < 0f)
            {
                // Slot is below: vCap is max safe descent rate relative to slot.
                float vCap = (float)Math.Sqrt(2f * Math.Max(0.1f, maxDecel) * (-verticalGap));
                if (-verticalDrift > vCap)
                {
                    float excess = -verticalDrift - vCap;
                    rawCorrection += excess * driftDamping * damping;
                }
            }

            return Math.Max(-maxCorrection, Math.Min(maxCorrection, rawCorrection));
        }

        /// <summary>
        /// Caps engine throttle when the aircraft is diverging above slot altitude while still climbing.
        /// Cuts full afterburner so upward momentum and kinetic energy wash off rapidly under gravity.
        /// </summary>
        public static float ClimbThrottleCap(float rawThrottle, float verticalSpeed, float verticalError,
                                             float maxCap = 0.45f, float airspeed = float.MaxValue,
                                             float minimumSpeed = 0f)
        {
            // Altitude correction must never starve a slow aircraft of recovery power.
            if (airspeed < minimumSpeed) return 1f;
            if (verticalSpeed > 2f && verticalError < -30f)
            {
                // Scale cap down as vertical divergence increases
                float severity = Math.Max(0f, Math.Min(1f, (-verticalError - 30f) / 70f));
                float effectiveCap = maxCap - severity * 0.15f; // Drops to 0.30 at -100m error
                return Math.Min(rawThrottle, Math.Max(0.2f, effectiveCap));
            }
            return rawThrottle;
        }
    }
}
