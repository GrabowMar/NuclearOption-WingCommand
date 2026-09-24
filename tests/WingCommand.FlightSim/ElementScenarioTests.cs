using System;
using Xunit;

namespace WingCommand.FlightSim
{
    /// <summary>Spec WMC program §3.3: elements split, fly their own tasks and rejoin without conflicts.</summary>
    public class ElementScenarioTests
    {
        private static ElementSimWing FourShip() =>
            ElementSimWing.InSlots(new VirtualLeader(new Vec3(0f, 3000f, 0f), 200f, 0f),
                SimFormations.Get("finger-four-right"), FormationCatalog.Standard, 4);

        [Fact]
        public void E1_APairSplitsOffToOrbitAndRejoinsWithoutConflict()
        {
            ElementSimWing w = FourShip();
            float min = float.MaxValue;
            for (int i = 0; i < 5 * 60; i++) { w.Step(); min = Math.Min(min, w.MinSeparation()); }
            // Seats #4 and #5 orbit a point 2 km right of the wing's track, 27 km ahead: the wing flies past them.
            int b = w.Detach(WingTask.Orbit(Waypoint.At(2000f, 27000f)), 2u, 3u);
            Assert.Equal(1, b);
            for (int i = 0; i < 125 * 60; i++) { w.Step(); min = Math.Min(min, w.MinSeparation()); }
            Assert.Equal(2, w.Roster.Count(1));
            float away = (w.Plants[2].Position - w.Leader.Position).Length;
            w.Merge(b);
            float merged = w.Time, captured = float.NaN;
            for (int i = 0; i < 150 * 60; i++)
            {
                w.Step();
                min = Math.Min(min, w.MinSeparation());
                bool all = true;
                for (int m = 0; m < 4; m++) all &= w.SlotError(m) < 0.3f * FormationCatalog.Standard;
                if (all && float.IsNaN(captured)) captured = w.Time;
            }
            Assert.True(min >= w.SafeRadius, $"min separation {min:0.0} m < {w.SafeRadius:0.0} m");
            Assert.False(float.IsNaN(captured), "the pair never re-captured its slots");
            // The pair orbits ~10 km around its point, so it merges ~6 km from the wing; the rejoin planner's overtake cap bounds
            // the closure (J1 measures join speed). Measured 109 s; the limit guards that the merge completes.
            Assert.True(captured - merged < 150f, $"re-capture took {captured - merged:0} s from {away:0} m away");
            Assert.Equal(0, w.Events.CountOf(WingEventKind.CollisionEmergency));
        }

        [Fact]
        public void E2_OneMemberDetachedAndReturnedRecapturesUnderNinetySeconds()
        {
            ElementSimWing w = FourShip();
            for (int i = 0; i < 5 * 60; i++) w.Step();
            int e = w.Detach(WingTask.Move(Waypoint.At(-4000f, 15000f)), 1u);
            Assert.Equal(1, e);
            for (int i = 0; i < 60 * 60; i++) w.Step();
            w.Merge(e);
            float start = w.Time, captured = float.NaN;
            for (int i = 0; i < 120 * 60 && float.IsNaN(captured); i++)
            {
                w.Step();
                if (w.SlotError(1) < 0.3f * FormationCatalog.Standard) captured = w.Time;
            }
            Assert.False(float.IsNaN(captured), "#3 never re-captured");
            Assert.True(captured - start < 90f, $"re-capture took {captured - start:0} s");
        }
    }
}
