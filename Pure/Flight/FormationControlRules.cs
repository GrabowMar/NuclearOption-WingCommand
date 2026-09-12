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

        // Native AutoAim scales bankAllowed by Clamp(radarAlt * 0.003f - 1f, 0.6f, 1.2f).
        // Scale desiredDegrees by the inverse so native autopilot respects the intended angle without
        // artificially suppressing high-altitude maneuvering.
        public static float BankInput(float desiredDegrees, float radarAltitude)
        {
            float altitudeFactor = Math.Max(0.6f, Math.Min(1.2f, radarAltitude * 0.003f - 1f));
            return Math.Max(0f, desiredDegrees) / altitudeFactor;
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
            // Ease arrival damping while behind, restoring it over the last 50 m
            // so a captured aircraft still settles quietly. Keep the braking cap.
            float capture = closing > 0f ? Math.Max(0f, Math.Min(1f, (gap - 50f) / 250f)) : 0f;
            float rawClosure = gapGain * gap * aggression - closingDamp * closing * damping * (1f - 0.5f * capture);
            float responseLoss = Math.Max(0f, maxDecel) * Math.Max(0f, responseSeconds);
            float overspeedCap = (float)Math.Sqrt(responseLoss * responseLoss +
                2f * Math.Max(0f, maxDecel) * Math.Max(gap, 0f)) - responseLoss;

            // Allow bounded overspeed only behind the slot; ahead, cap positive closure at zero. Limit
            // negative demand to avoid excessive slowing.
            return Math.Max(-maxStationClosure, Math.Min(overspeedCap, rawClosure));
        }

        /// <summary>Restrict bank toward level when pitch-down recovery is needed to arrest an uncontrolled
        /// climb or severe nose-high divergence. Normal descents retain full bank authority.</summary>
        public static float PitchDownBankAuthority(
            float currentPitchDeg, float demandedPitchDeg,
            float verticalSpeed, float verticalError,
            float requestedBankDeg, float levelBankDeg)
        {
            float pitchDeficit = currentPitchDeg - demandedPitchDeg;
            // Only engage when actively climbing above the slot or severely nose-high relative to demand.
            bool divergingClimb = verticalSpeed > 2f && verticalError < -5f;
            bool severePitchHigh = currentPitchDeg > 10f && pitchDeficit > 8f;

            if (divergingClimb || severePitchHigh)
            {
                // Preserve safe maneuvering floor (35 deg) during moderate deficits; only collapse toward
                // levelBank during extreme climb rates or violent zoom divergences.
                float safeRecoveryCeiling = Math.Max(levelBankDeg, 35f);
                float baseAllowed = Math.Min(requestedBankDeg, safeRecoveryCeiling);

                // Severe climbs (>15 m/s) or high pitch (>25 deg) collapse smoothly toward levelBank.
                if (verticalSpeed > 15f || currentPitchDeg > 25f)
                {
                    float extremeScale = Math.Max(0f, Math.Min(1f, Math.Max((verticalSpeed - 15f) / 15f, (currentPitchDeg - 25f) / 15f)));
                    return levelBankDeg + (baseAllowed - levelBankDeg) * (1f - extremeScale);
                }

                return baseAllowed;
            }

            return requestedBankDeg;
        }

        /// <summary>Bound vertical closure using stopping distance v²/(2a) to reduce altitude
        /// overshoot.</summary>
        public static float KinematicVerticalCorrection(
            float verticalGap, float verticalDrift, float maxCorrection,
            float positionGain, float driftDamping, float aggression, float damping,
            float maxDecel = 6.0f)
        {
            float rawCorrection = (verticalGap * positionGain * aggression)
                                  - (verticalDrift * driftDamping * damping);

            if (verticalGap > 0f)
            {
                // Maximum relative climb rate that can stop within the upward slot gap.
                float vCap = (float)Math.Sqrt(2f * Math.Max(0.1f, maxDecel) * verticalGap);
                if (verticalDrift > vCap)
                {
                    float excess = verticalDrift - vCap;
                    rawCorrection -= excess * driftDamping * damping;
                }
            }
            else if (verticalGap < 0f)
            {
                // Maximum relative descent rate that can stop within the downward slot gap.
                float vCap = (float)Math.Sqrt(2f * Math.Max(0.1f, maxDecel) * (-verticalGap));
                if (-verticalDrift > vCap)
                {
                    float excess = -verticalDrift - vCap;
                    rawCorrection += excess * driftDamping * damping;
                }
            }

            return Math.Max(-maxCorrection, Math.Min(maxCorrection, rawCorrection));
        }

        /// <summary>Limit throttle while climbing away above the slot so excess upward energy can
        /// dissipate. Never cap throttle during normal rejoins unless in a severe runaway zoom climb.</summary>
        public static float ClimbThrottleCap(float rawThrottle, float verticalSpeed, float verticalError,
                                             float maxCap = 0.45f, float airspeed = float.MaxValue,
                                             float minimumSpeed = 0f, float gap = 0f, float distance = 0f)
        {
            // Preserve recovery power below minimum safe airspeed
            if (airspeed < minimumSpeed) return 1f;

            // Never cap throttle during normal rejoins, UNLESS climbing violently above the slot.
            bool runawayZoomClimb = verticalSpeed > 15f && verticalError < -80f;
            if (!runawayZoomClimb && (gap > 80f || distance > 200f)) return rawThrottle;

            if (verticalSpeed > 2f && verticalError < -30f)
            {
                // Tighten the throttle cap with increasing altitude divergence.
                float severity = Math.Max(0f, Math.Min(1f, (-verticalError - 30f) / 70f));
                float effectiveCap = maxCap - severity * 0.15f; // Cap reaches 0.30 at 100 m above the slot.
                return Math.Min(rawThrottle, Math.Max(0.2f, effectiveCap));
            }
            return rawThrottle;
        }

        /// <summary>Anticipate climb rate from vertical acceleration and pitch rate, especially in HOLD.</summary>
        public static float EffectiveClimb(float climbRate, float verticalAccel, float pitchRate,
                                           float horizontalSpeed, float holdBlend, float leadSeconds = 0.55f)
        {
            float accelRise = verticalAccel * leadSeconds;
            float pitchRise = Math.Max(0f, horizontalSpeed) * (float)Math.Sin(pitchRate * leadSeconds);
            float leadWeight = 0.5f + 0.5f * Math.Max(0f, Math.Min(1f, holdBlend));
            float predicted = climbRate + (accelRise + pitchRise) * leadWeight;
            return Math.Max(-150f, Math.Min(250f, predicted));
        }

        /// <summary>Compute target bank blending navigation turn demand with leader bank matching and roll rate lead.</summary>
        public static float TargetBank(float turnBank, float leaderBank, float leaderRollRate,
                                       float holdBlend, float outOfPosition, float bankAllowed,
                                       float leadSeconds = 0.25f)
        {
            float anticipatedLeaderBank = leaderBank + leaderRollRate * (180f / (float)Math.PI) * leadSeconds;
            float stationWeight = Math.Max(0f, Math.Min(1f, 1f - outOfPosition));
            float matchWeight = stationWeight * (0.6f + 0.4f * Math.Max(0f, Math.Min(1f, holdBlend)));
            float blended = turnBank * (1f - matchWeight) + anticipatedLeaderBank * matchWeight;
            return Math.Max(-bankAllowed, Math.Min(bankAllowed, blended));
        }

        /// <summary>Calculate signed shortest bank error in degrees.</summary>
        public static float BankError(float ownBank, float targetBank) =>
            FormationTracking.WrapDegrees(targetBank - ownBank);

        /// <summary>Calculate proportional-derivative roll demand for bank matching without raw stick passthrough.</summary>
        public static float RollFeedforward(float bankErrorDeg, float leaderRollRateRad, float ownRollRateRad,
                                            float holdBlend, float outOfPosition)
        {
            float p = Math.Max(-1f, Math.Min(1f, bankErrorDeg / 28f));
            float rateDiff = leaderRollRateRad - ownRollRateRad;
            float d = Math.Max(-0.6f, Math.Min(0.6f, rateDiff / 2.0f));
            float demand = p * 0.75f + d * 0.25f;
            float stationScale = Math.Max(0f, Math.Min(1f, (1f - outOfPosition) * (0.5f + 0.5f * holdBlend)));
            return Math.Max(-1f, Math.Min(1f, demand * stationScale));
        }

        /// <summary>Calculate pitch demand assist for rapid pull-ups / push-overs without raw stick passthrough.</summary>
        public static float PitchFeedforward(float pitchRateErrorRad, float verticalAccel,
                                             float holdBlend, float outOfPosition)
        {
            float rateDemand = Math.Max(-0.8f, Math.Min(0.8f, pitchRateErrorRad / 1.2f));
            float accelDemand = Math.Max(-0.4f, Math.Min(0.4f, verticalAccel / 25f));
            float demand = rateDemand * 0.65f + accelDemand * 0.35f;
            float scale = Math.Max(0f, Math.Min(1f, (1f - outOfPosition) * (0.4f + 0.6f * holdBlend)));
            return Math.Max(-1f, Math.Min(1f, demand * scale));
        }
    }
}
