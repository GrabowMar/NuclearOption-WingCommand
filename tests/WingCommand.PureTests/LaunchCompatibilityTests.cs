using System.Collections.Generic;
using Xunit;

namespace WingCommand
{
    public sealed class LaunchCompatibilityTests
    {
        [Fact]
        public void Evaluate_when_no_airframe_selected_returns_none_if_allowed()
        {
            var status = LaunchBaseStatusPolicy.Evaluate(allowed: true, canProduce: true, hasAirframeSelection: false);
            Assert.Equal(LaunchBaseStatus.None, status);
            Assert.Equal("", LaunchBaseStatusPolicy.BadgeText(status));
        }

        [Fact]
        public void Evaluate_when_no_airframe_selected_returns_blocked_if_not_allowed()
        {
            var status = LaunchBaseStatusPolicy.Evaluate(allowed: false, canProduce: true, hasAirframeSelection: false);
            Assert.Equal(LaunchBaseStatus.Blocked, status);
            Assert.Equal("BLOCKED", LaunchBaseStatusPolicy.BadgeText(status));
        }

        [Fact]
        public void Evaluate_when_airframe_selected_and_base_cannot_produce_returns_nopad_regardless_of_allowed()
        {
            var statusAllowed = LaunchBaseStatusPolicy.Evaluate(allowed: true, canProduce: false, hasAirframeSelection: true);
            var statusBlocked = LaunchBaseStatusPolicy.Evaluate(allowed: false, canProduce: false, hasAirframeSelection: true);

            Assert.Equal(LaunchBaseStatus.NoPad, statusAllowed);
            Assert.Equal(LaunchBaseStatus.NoPad, statusBlocked);
            Assert.Equal("NO PAD", LaunchBaseStatusPolicy.BadgeText(statusAllowed));
            Assert.Equal("NO PAD", LaunchBaseStatusPolicy.BadgeText(statusBlocked));
        }

        [Fact]
        public void Evaluate_when_airframe_selected_and_base_can_produce_returns_ready_if_allowed()
        {
            var status = LaunchBaseStatusPolicy.Evaluate(allowed: true, canProduce: true, hasAirframeSelection: true);
            Assert.Equal(LaunchBaseStatus.Ready, status);
            Assert.Equal("READY", LaunchBaseStatusPolicy.BadgeText(status));
        }

        [Fact]
        public void Evaluate_when_airframe_selected_and_base_can_produce_returns_blocked_if_not_allowed()
        {
            var status = LaunchBaseStatusPolicy.Evaluate(allowed: false, canProduce: true, hasAirframeSelection: true);
            Assert.Equal(LaunchBaseStatus.Blocked, status);
            Assert.Equal("BLOCKED", LaunchBaseStatusPolicy.BadgeText(status));
        }

        [Fact]
        public void Tooltip_reports_correct_context_and_diagnostics()
        {
            string tt1 = LaunchBaseStatusPolicy.Tooltip("AssaultCarrier1", "FS-20", allowed: true, canProduce: false);
            Assert.Contains("AssaultCarrier1 [CHECKED]", tt1);
            Assert.Contains("Cannot launch FS-20", tt1);
            Assert.Contains("no compatible hangar or helipad", tt1);

            string tt2 = LaunchBaseStatusPolicy.Tooltip("airbase_island5", "FS-20", allowed: true, canProduce: true);
            Assert.Contains("airbase_island5", tt2);
            Assert.Contains("Can launch FS-20", tt2);
            Assert.Contains("[ALLOWED]", tt2);

            string tt3 = LaunchBaseStatusPolicy.Tooltip("airbase_island5", "FS-20", allowed: false, canProduce: true);
            Assert.Contains("airbase_island5", tt3);
            Assert.Contains("Can launch FS-20", tt3);
            Assert.Contains("[BLOCKED - click to allow]", tt3);

            string tt4 = LaunchBaseStatusPolicy.Tooltip("airbase_island5", null, allowed: true, canProduce: true);
            Assert.Equal("airbase_island5 — launches allowed", tt4);
        }
    }
}
