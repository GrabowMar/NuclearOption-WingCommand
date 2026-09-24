using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class ContactWatchTests
    {
        private readonly int[] report = new int[4];

        private static ContactSample[] One(uint id, float km, bool air = true) =>
            new[] { new ContactSample { Id = id, Air = air, Distance = km * 1000f } };

        private int Update(ContactWatch w, ContactSample[] s, float now) => w.Update(s, s.Length, now, report);

        [Fact]
        public void AContactInRangeIsReportedOnce()
        {
            var w = new ContactWatch();
            Assert.Equal(1, Update(w, One(7, 30f), 0f));
            Assert.Equal(0, report[0]);
            Assert.Equal(0, Update(w, One(7, 30f), 1f));
        }

        [Fact]
        public void AContactOutOfRangeIsNotReported()
        {
            var w = new ContactWatch();
            Assert.Equal(0, Update(w, One(7, 45f), 0f));
            Assert.Equal(0, w.Known);
        }

        [Fact]
        public void AContactAtTheEdgeIsReportedOnce()
        {
            var w = new ContactWatch();
            Assert.Equal(1, Update(w, One(7, 39.9f), 0f));
            Assert.Equal(0, Update(w, One(7, 40.1f), 1f));
            Assert.Equal(0, Update(w, One(7, 39.9f), 2f));
            Assert.Equal(0, Update(w, One(7, 49f), 3f));
            Assert.Equal(0, Update(w, One(7, 51f), 4f));   // beyond 1.25 × range: forgotten
            Assert.Equal(0, w.Known);
            Assert.Equal(1, Update(w, One(7, 39f), 5f));    // popped up again
        }

        [Fact]
        public void AContactMissingForAMinuteIsForgotten()
        {
            var w = new ContactWatch();
            Assert.Equal(1, Update(w, One(7, 30f), 0f));
            Assert.Equal(0, Update(w, new ContactSample[0], ContactWatch.ForgetSeconds + 1f));
            Assert.Equal(0, w.Known);
            Assert.Equal(1, Update(w, One(7, 30f), ContactWatch.ForgetSeconds + 2f));
        }

        [Fact]
        public void TheNearestGoesFirstAndTheOtherNextSecond()
        {
            var w = new ContactWatch();
            var both = new[] { new ContactSample { Id = 1, Air = true, Distance = 30000f }, new ContactSample { Id = 2, Air = true, Distance = 20000f } };
            Assert.Equal(1, Update(w, both, 0f));
            Assert.Equal(1, report[0]);
            Assert.Equal(1, Update(w, both, 1f));
            Assert.Equal(0, report[0]);
            Assert.Equal(0, Update(w, both, 2f));
        }

        [Fact]
        public void GroundContactsOnlyWhileScouting()
        {
            var w = new ContactWatch();
            Assert.Equal(0, Update(w, One(9, 5f, air: false), 0f));
            Assert.Equal(0, w.Known);
            w.Ground = true;
            Assert.Equal(1, Update(w, One(9, 5f, air: false), 1f));
        }

        [Fact]
        public void AContactTheRadioDroppedIsReportedAgain()
        {
            // Review M7a-2 I1: a call dropped by the radio was never made again.
            var w = new ContactWatch();
            Assert.Equal(1, Update(w, One(7, 30f), 0f));
            w.Forget(7);
            Assert.Equal(1, Update(w, One(7, 30f), 1f));
        }

        [Fact]
        public void NothingIsReportedWhileTheReportArrayIsEmpty()
        {
            var w = new ContactWatch();
            ContactSample[] s = One(7, 30f);
            Assert.Equal(0, w.Update(s, 1, 0f, new int[0]));
            Assert.Equal(0, w.Known);
            Assert.Equal(1, Update(w, s, 1f));
        }

        [Theory]
        [InlineData(true, 45f, false, true)]      // air inside the forget distance: kept for hysteresis
        [InlineData(true, 51f, false, false)]     // beyond 1.25 × range
        [InlineData(false, 5f, false, false)]     // ground while not scouting (review M7a-2 C1: it filled the samples)
        [InlineData(false, 9f, true, true)]
        [InlineData(false, 11f, true, false)]
        public void OnlyContactsThatCanMatterAreSampled(bool air, float km, bool ground, bool kept)
        {
            var w = new ContactWatch { Ground = ground };
            Assert.Equal(kept, w.Considers(air, km * 1000f));
        }

        [Fact]
        public void ManyContactsStayBounded()
        {
            var w = new ContactWatch();
            var many = new ContactSample[100];
            for (int i = 0; i < many.Length; i++) many[i] = new ContactSample { Id = (uint)i, Air = true, Distance = 1000f + i };
            for (int t = 0; t < 100; t++) Update(w, many, t);
            Assert.True(w.Known <= ContactWatch.Capacity);
        }

        [Fact]
        public void UpdateAllocatesNothing()
        {
            var w = new ContactWatch();
            ContactSample[] s = One(7, 30f);
            Update(w, s, 0f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 1; t < 100; t++) Update(w, s, t);
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
