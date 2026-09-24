using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class TaskLeadTests
    {
        private const float Dt = 1f / 30f;

        [Fact]
        public void ALeadOnALegConvergesOntoTheLineAtItsAltitudeAndSpeedWithinTheBankLimit()
        {
            var lead = new TaskLead(new Vec3(1000f, 1000f, 0f), new Vec3(0f, 0f, 150f), false);
            float maxBank = 0f;
            for (int i = 0; i < 200 * 30; i++)
            {
                lead.FlyLeg(Vec3.Zero, new Vec3(0f, 0f, 60000f), 2000f, 180f, Dt);
                maxBank = Math.Max(maxBank, Math.Abs(lead.BankDeg));
            }
            Assert.True(Math.Abs(lead.Position.X) < 50f, $"cross-track {lead.Position.X}");
            Assert.Equal(2000f, lead.Position.Y, 0);
            Assert.Equal(180f, lead.Speed, 1);
            Assert.True(maxBank <= TaskLead.MaxBankDeg + 0.01f);
        }

        [Fact]
        public void AnOrbitSettlesOnItsRadiusTurningRight()
        {
            float radius = TaskLead.OrbitRadius(150f);
            var lead = new TaskLead(new Vec3(radius + 2000f, 2000f, 0f), new Vec3(0f, 0f, 150f), false);
            for (int i = 0; i < 400 * 30; i++) lead.FlyOrbit(Vec3.Zero, radius, false, 2000f, 150f, Dt);
            Assert.Equal(radius, lead.Position.Horizontal.Length, radius * 0.05f);
            Assert.True(lead.BankDeg > 5f, $"bank {lead.BankDeg}");
        }

        [Fact]
        public void ALeftOrbitTurnsLeft()
        {
            float radius = TaskLead.OrbitRadius(150f);
            var lead = new TaskLead(new Vec3(radius, 2000f, 0f), new Vec3(0f, 0f, -150f), false);
            for (int i = 0; i < 300 * 30; i++) lead.FlyOrbit(Vec3.Zero, radius, true, 2000f, 150f, Dt);
            Assert.True(lead.BankDeg < -5f, $"bank {lead.BankDeg}");
            Assert.Equal(radius, lead.Position.Horizontal.Length, radius * 0.05f);
        }

        [Fact]
        public void AHelicopterLeadStopsOverItsPointFacingTheHeading()
        {
            var lead = new TaskLead(new Vec3(0f, 300f, 0f), new Vec3(0f, 0f, 40f), true);
            for (int i = 0; i < 180 * 30; i++) lead.FlyHover(new Vec3(0f, 0f, 1500f), 90f, 300f, 50f, Dt);
            Assert.True((lead.Position - new Vec3(0f, 300f, 1500f)).Horizontal.Length < 30f, $"at {lead.Position}");
            Assert.True(lead.Speed < 1f);
            Assert.True(Math.Abs(Scalar.Wrap180(lead.HeadingDeg - 90f)) < 10f, $"heading {lead.HeadingDeg}");
        }

        [Fact]
        public void AHelicopterLeadStartingOnItsPointAtSpeedBrakesSmoothlyWithoutReversing()
        {
            // Review M4a C1: "Hold Here" puts the point under the moving player; the slide snapped the track toward the
            // point every tick while the speed only slewed, reversing the lead's velocity every tick.
            var lead = new TaskLead(new Vec3(0f, 300f, 0f), new Vec3(0f, 0f, 60f), true);
            Vec3 point = new Vec3(0f, 300f, 0f);
            Vec3 last = lead.Velocity.Horizontal;
            float worstStep = 0f;
            int reversals = 0;
            for (int i = 0; i < 180 * 30; i++)
            {
                lead.FlyHover(point, 90f, 300f, 60f, Dt);
                Vec3 v = lead.Velocity.Horizontal;
                worstStep = Math.Max(worstStep, (v - last).Length);
                if (v.Length > 1f && last.Length > 1f && Vec3.Dot(v, last) < 0f) reversals++;
                last = v;
            }
            Assert.True(worstStep <= TaskLead.SpeedRate * Dt * 1.5f + 1e-3f, $"velocity jumped {worstStep:0.00} m/s in a tick");
            Assert.Equal(0, reversals);
            Assert.True((lead.Position - point).Horizontal.Length < 30f, $"at {lead.Position}");
            Assert.True(lead.Speed < 1f, $"speed {lead.Speed:0.0}");
        }

        [Fact]
        public void TheSampleIsAPresentAirborneAnchorThatIsNotThePlayer()
        {
            var lead = new TaskLead(new Vec3(0f, 1000f, 0f), new Vec3(0f, 0f, 150f), false);
            AnchorSample s = lead.Sample();
            Assert.True(s.Present && s.Airborne && !s.IsPlayer && !s.CanHover);
            Assert.Equal(AnchorKind.Aircraft, s.Kind);
            Assert.Equal(150f, s.Vel.Length, 1);
        }
    }
}
