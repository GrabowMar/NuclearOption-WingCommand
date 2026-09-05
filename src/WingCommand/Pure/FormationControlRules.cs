using System;

namespace WingCommand
{
    internal static class FormationControlRules
    {
        // AutoAim multiplies bankAllowed by altitude (up to 1.2) and its vertical
        // factor (up to 1.2). Never amplify our requested ceiling to compensate
        // for reductions: those reductions may be protecting a slow/low aircraft.
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
                // At exact closest approach there is no "away" vector. A lateral
                // normal to relative velocity reverses for the other aircraft, so
                // both choose opposite world-space escape directions without climbing.
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

        /// <summary>
        /// Computes a safe 3D rejoin aim direction that rotates toward the requested bearing
        /// primarily in the horizontal plane (using bank) while strictly bounding pitch
        /// to prevent zoom-climbs or dives into the ground.
        /// </summary>
        public static void SafeRejoinDirection(
            float curDirX, float curDirY, float curDirZ,
            float reqX, float reqY, float reqZ,
            float allowedAngleDeg,
            float maxPitchUpDeg,
            float maxPitchDownDeg,
            float radarAlt,
            out float outX, out float outY, out float outZ)
        {
            // --- 1. Horizontal Heading (XZ plane) ---
            double curHLen = Math.Sqrt((double)curDirX * curDirX + (double)curDirZ * curDirZ);
            double chX = curHLen > 1e-6 ? curDirX / curHLen : 0.0;
            double chZ = curHLen > 1e-6 ? curDirZ / curHLen : 1.0;

            double reqHLen = Math.Sqrt((double)reqX * reqX + (double)reqZ * reqZ);
            double rotHX;
            double rotHZ;

            if (reqHLen < 1e-4)
            {
                // Target is directly above or below; maintain current horizontal heading.
                rotHX = chX;
                rotHZ = chZ;
            }
            else
            {
                double rhX = reqX / reqHLen;
                double rhZ = reqZ / reqHLen;

                // 2D cross product (Y component) and dot product between current and requested horizontal directions.
                double crossY = chZ * rhX - chX * rhZ;
                double dot = Math.Max(-1.0, Math.Min(1.0, chX * rhX + chZ * rhZ));
                double angleRad = Math.Atan2(crossY, dot);

                // If target is directly behind (~180°), default to a right turn if cross product is ambiguous.
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

            // --- 2. Vertical Pitch (Elevation Angle) ---
            double safeReqHLen = Math.Max(1.0, reqHLen);
            double pitchRad = Math.Atan2(reqY, safeReqHLen);
            double pitchDeg = pitchRad * (180.0 / Math.PI);

            double maxUp = Math.Max(0.0, maxPitchUpDeg);
            double maxDown = Math.Max(0.0, maxPitchDownDeg);

            // Ground safety: scale down allowed descent as radar altitude drops below 250m.
            if (radarAlt < 250f)
            {
                float floorScale = Math.Max(0f, Math.Min(1f, (radarAlt - 60f) / 190f));
                maxDown *= floorScale;
            }

            pitchDeg = Math.Max(-maxDown, Math.Min(maxUp, pitchDeg));

            // Low altitude floor: if below 60m radar altitude, never command a descent.
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

        /// <summary>
        /// Deceleration-limited closure demand. Ensures the closing speed never exceeds what
        /// the remaining along-track gap can safely shed at the given deceleration rate.
        /// </summary>
        public static float RejoinClosure(
            float gap, float closing, float maxDecel, float aggression, float damping,
            float gapGain, float closingDamp, float maxStationClosure)
        {
            float rawClosure = gapGain * gap * aggression - closingDamp * closing * damping;
            float overspeedCap = (float)Math.Sqrt(2f * Math.Max(0f, maxDecel) * Math.Max(gap, 0f));

            // When behind slot (gap > 0), overspeedCap defines the upper kinematic limit.
            // When ahead of slot (gap <= 0), overspeedCap is 0, forbidding positive closure.
            // Lower limit prevents aerodynamic stall / excessive negative demand.
            return Math.Max(-maxStationClosure, Math.Min(overspeedCap, rawClosure));
        }
    }
}
