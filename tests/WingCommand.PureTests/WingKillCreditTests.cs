using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand
{
    internal sealed class Missile : Unit { }
    internal static partial class Plugin
    {
        internal static readonly TestSettings Settings = new TestSettings();
        internal sealed class TestSettings
        {
            public readonly TestFlag PilotProgression = new TestFlag();
        }
        internal sealed class TestFlag { public bool Value = true; }
    }
    internal static class WingPilotRoster
    {
        internal static readonly List<Unit> Kills = new List<Unit>();
        public static void NoteKill(Aircraft shooter, Unit victim) => Kills.Add(victim);
    }
}

namespace WingCommand.PureTests
{
    [Collection("Runtime state")]
    public sealed class WingKillCreditTests : IDisposable
    {
        public WingKillCreditTests()
        {
            WingKillCredit.Reset();
            WingPilotRoster.Kills.Clear();
            UnityEngine.Time.timeSinceLevelLoad = 0f;
        }

        public void Dispose()
        {
            WingKillCredit.Reset();
            WingPilotRoster.Kills.Clear();
        }

        [Fact]
        public void DuplicateClaimsDoNotSkipOtherKillsOrOverrunTheList()
        {
            var shared = new Unit();
            var other = new Unit();
            WingKillCredit.NoteShot(new Aircraft(), shared);
            WingKillCredit.NoteShot(new Aircraft(), other);
            WingKillCredit.NoteShot(new Aircraft(), shared);
            shared.disabled = other.disabled = true;

            WingKillCredit.Tick();
            Assert.Equal(2, WingPilotRoster.Kills.Count);
            Assert.Contains(shared, WingPilotRoster.Kills);
            Assert.Contains(other, WingPilotRoster.Kills);
            UnityEngine.Time.timeSinceLevelLoad = 1f;
            WingKillCredit.Tick();
            Assert.Equal(2, WingPilotRoster.Kills.Count);
        }
    }
}
