using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WingCommand.PureTests
{
    public class RouteStoreTests
    {
        private static RouteDraft Draft(int n)
        {
            var d = new RouteDraft();
            for (int i = 0; i < n; i++) d.Add(i * 1000f, i * 500f);
            return d;
        }

        [Fact]
        public void SaveNamesAndRoundTrips()
        {
            var s = new RouteStore();
            RouteDraft d = Draft(3);
            d.Select(1);
            d.StepAltitude(+1);
            d.CycleAction();
            d.CycleLoop();
            Assert.True(s.Save(d, out string name));
            Assert.Equal("ROUTE 1", name);
            var errors = new List<string>();
            RouteStore back = RouteStore.FromJson(s.ToJson(), errors);
            Assert.Empty(errors);
            Assert.Single(back.Routes);
            SavedRoute r = back.Routes[0];
            Assert.Equal("ROUTE 1", r.Name);
            Assert.Equal(RouteLoop.Loop, r.Loop);
            Assert.Equal(3, r.Points.Length);
            Assert.Equal(1000f, r.Points[1].X);
            Assert.Equal(500f, r.Points[1].Z);
            Assert.Equal(RouteDraft.Altitudes[1], r.Points[1].Altitude);
            Assert.True(float.IsNaN(r.Points[0].Altitude));
            Assert.True(float.IsNaN(r.Points[0].Speed));
            Assert.Equal(ArrivalAction.Orbit, r.Points[1].Action);
            Assert.Equal(RouteDraft.OrbitSeconds, r.Points[1].Seconds);
        }

        [Fact]
        public void AnEmptyDraftIsNotSavedAndTheStoreIsCapped()
        {
            var s = new RouteStore();
            Assert.False(s.Save(new RouteDraft(), out _));
            for (int i = 0; i < RouteStore.Max; i++) Assert.True(s.Save(Draft(2), out _));
            Assert.False(s.Save(Draft(2), out string name));
            Assert.Null(name);
            Assert.True(s.Remove(0));
            Assert.False(s.Remove(99));
            Assert.True(s.Save(Draft(2), out name));
            Assert.Equal("ROUTE 1", name);                 // the first free number
        }

        [Fact]
        public void LoadPutsARouteInTheDraft()
        {
            var s = new RouteStore();
            s.Save(Draft(4), out _);
            var d = new RouteDraft();
            s.Load(0, d);
            Assert.Equal(4, d.Count);
            s.Load(5, d);                                  // out of range: unchanged
            Assert.Equal(4, d.Count);
        }

        [Fact]
        public void CorruptFileLoadsNothing()
        {
            var errors = new List<string>();
            Assert.Empty(RouteStore.FromJson("{ not json", errors).Routes);
            Assert.Single(errors);
            errors.Clear();
            Assert.Empty(RouteStore.FromJson(null, errors).Routes);
            Assert.Empty(errors);                           // no file is not an error
        }

        [Fact]
        public void BadEntriesAreSkipped()
        {
            // Review focus 4: a route with a non-finite or huge coordinate, no points, or too many is dropped with a reason.
            string json = "{\"routes\":[" +
                "{\"name\":\"OK\",\"loop\":\"Once\",\"points\":[{\"x\":1,\"z\":2}]}," +
                "{\"name\":\"FAR\",\"loop\":\"Once\",\"points\":[{\"x\":1e9,\"z\":2}]}," +
                "{\"name\":\"EMPTY\",\"loop\":\"Once\",\"points\":[]}," +
                "{\"name\":\"LONG\",\"loop\":\"Once\",\"points\":[" + string.Join(",", Enumerable.Repeat("{\"x\":1,\"z\":2}", 17)) + "]}" +
                "]}";
            var errors = new List<string>();
            RouteStore s = RouteStore.FromJson(json, errors);
            Assert.Single(s.Routes);
            Assert.Equal("OK", s.Routes[0].Name);
            Assert.True(float.IsNaN(s.Routes[0].Points[0].Altitude));
            Assert.Equal(3, errors.Count);
        }
    }
}
