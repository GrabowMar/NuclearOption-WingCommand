using System;
using System.Numerics;
using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationNativeGuidanceTests
    {
        [Theory]
        [InlineData(250f, 180f, 35f, -15f, 55f)]
        [InlineData(300f, 200f, 45f, 0f, -55f)]
        public void HighSpeedRejoinDoesNotTriggerNativeCloseTargetZoom(
            float speed, float cornerSpeed, float pitch, float aimPitch, float heading)
        {
            Vector3 velocity = Direction(0f, pitch) * speed;
            Vector3 offset = Direction(heading, aimPitch) *
                Math.Max(WingTuning.FormationMinLookAhead, speed * WingTuning.FormationLookAheadSeconds);
            Assert.True(NativeZoom(velocity, offset, speed, cornerSpeed) > 0f,
                "The original guidance must reproduce the installed AutoAim zoom condition.");

            Vector3 aim = FormationGuidance.SteeringAim(offset);
            Assert.Equal(0f, NativeZoom(velocity, aim, speed, cornerSpeed));
            Assert.InRange(Vector3.Distance(Vector3.Normalize(offset), Vector3.Normalize(aim)), 0f, 0.00001f);
        }

        [Fact]
        public void ClimbAuthorityKeepsAResidualClimbAtMinimumAirspeedAndRestoresWithMargin()
        {
            Assert.Equal(6.25f, FormationGuidance.ClimbAuthority(60f, 60f, 25f));
            Assert.Equal(15.625f, FormationGuidance.ClimbAuthority(80f, 60f, 25f));
            Assert.Equal(25f, FormationGuidance.ClimbAuthority(100f, 60f, 25f));
            Assert.True(FormationGuidance.ClimbAuthority(70f, 60f, 25f) <
                FormationGuidance.ClimbAuthority(90f, 60f, 25f));
        }

        [Fact]
        public void SteeringRayPreservesLongAndDegenerateAims()
        {
            Assert.Equal(Vector3.Zero, FormationGuidance.SteeringAim(Vector3.Zero));
            var distant = new Vector3(2000f, -500f, 2000f);
            Assert.Equal(distant, FormationGuidance.SteeringAim(distant));
        }

        // Only the offending branch from the installed AutopilotPlane, not an aircraft simulator.
        private static float NativeZoom(Vector3 velocity, Vector3 offset, float airspeed, float cornerSpeed)
        {
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(velocity),
                Vector3.Normalize(offset)), -1f, 1f)) * 180f / MathF.PI;
            return angle > 60f && offset.Length() < 2000f && -offset.Y < 1000f
                ? 2000f * Math.Clamp(airspeed / cornerSpeed - 1f, 0f, 1f) : 0f;
        }

        private static Vector3 Direction(float heading, float pitch)
        {
            heading *= MathF.PI / 180f;
            pitch *= MathF.PI / 180f;
            return new Vector3(MathF.Sin(heading) * MathF.Cos(pitch),
                MathF.Sin(pitch), MathF.Cos(heading) * MathF.Cos(pitch));
        }
    }
}
