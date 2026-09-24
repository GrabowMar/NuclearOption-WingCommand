using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class RadioQueueTests
    {
        private static RadioLine L(int speaker, RadioClass c, string key, string text = "Copy.", bool wing = false) =>
            new RadioLine { Speaker = speaker, Class = c, Key = key, Text = text, WingWide = wing };

        [Fact]
        public void HigherClassesGoFirstAndFifoWithinAClass()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Status, "a"), 0f);
            q.Enqueue(L(2, RadioClass.Tactical, "b"), 0f);
            q.Enqueue(L(3, RadioClass.Tactical, "c"), 0f);
            Assert.True(q.Next(0f, out RadioLine first));
            Assert.Equal("b", first.Key);
            Assert.False(q.Next(0.1f, out _));                                   // the channel is busy
            float air = RadioQueue.Airtime("Copy.");
            Assert.True(q.Next(air, out RadioLine second));
            Assert.Equal("c", second.Key);
            Assert.True(q.Next(2f * air, out RadioLine third));
            Assert.Equal("a", third.Key);
        }

        [Fact]
        public void AnEmergencyCutsTheCurrentLineButNotAnotherEmergency()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Status, "status", new string('x', 80)), 0f);
            Assert.True(q.Next(0f, out _));
            q.Enqueue(L(2, RadioClass.Emergency, "e1"), 0.5f);
            Assert.True(q.Next(0.5f, out RadioLine e1));
            Assert.Equal("e1", e1.Key);
            q.Enqueue(L(3, RadioClass.Emergency, "e2"), 0.6f);
            Assert.False(q.Next(0.6f, out _));
            Assert.True(q.Next(0.5f + RadioQueue.Airtime("Copy."), out RadioLine e2));
            Assert.Equal("e2", e2.Key);
        }

        [Fact]
        public void ASpeakerWaitsTheGapAfterTheirLastLine()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Status, "a"), 0f);
            q.Enqueue(L(1, RadioClass.Status, "b"), 0f);
            Assert.True(q.Next(0f, out _));
            float end = RadioQueue.Airtime("Copy.");
            Assert.False(q.Next(end + 0.1f, out _));
            Assert.True(q.Next(end + RadioQueue.SpeakerGap, out RadioLine b));
            Assert.Equal("b", b.Key);
            Assert.True(q.MinSpeakerGap >= RadioQueue.SpeakerGap - 1e-4f);
        }

        [Fact]
        public void HigherClassWaitingOnItsGapHoldsLowerClasses()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Tactical, "t1"), 0f);
            Assert.True(q.Next(0f, out _));
            float end = RadioQueue.Airtime("Copy.");
            q.Enqueue(L(1, RadioClass.Tactical, "t2"), end);
            q.Enqueue(L(2, RadioClass.Chatter, "c"), end);
            Assert.False(q.Next(end + 0.1f, out _));                             // chatter does not jump the gapped tactical line
            Assert.True(q.Next(end + RadioQueue.SpeakerGap, out RadioLine t2));
            Assert.Equal("t2", t2.Key);
        }

        [Fact]
        public void EmergenciesIgnoreTheSpeakerGap()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Status, "a"), 0f);
            Assert.True(q.Next(0f, out _));
            float end = RadioQueue.Airtime("Copy.");
            q.Enqueue(L(1, RadioClass.Emergency, "e"), end);
            Assert.True(q.Next(end, out RadioLine e));
            Assert.Equal("e", e.Key);
        }

        [Fact]
        public void StaleLinesAreDroppedUnsent()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Tactical, "t"), 0f);
            Assert.False(q.Next(RadioQueue.TacticalAge + 0.1f, out _));
            Assert.Equal(1, q.DroppedStale);
            Assert.Equal(0, q.Queued);
        }

        [Fact]
        public void TheSameKeyQueuedIsReplacedInPlace()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Status, "bingo", "old"), 0f);
            q.Enqueue(L(1, RadioClass.Status, "bingo", "new"), 1f);
            Assert.Equal(1, q.Queued);
            Assert.True(q.Next(1f, out RadioLine line));
            Assert.Equal("new", line.Text);
        }

        [Fact]
        public void AKeySentRecentlyIsDroppedWithinItsScope()
        {
            var q = new RadioQueue();
            q.Enqueue(L(1, RadioClass.Tactical, "engage", wing: true), 0f);
            Assert.True(q.Next(0f, out _));
            Assert.False(q.Enqueue(L(2, RadioClass.Tactical, "engage", wing: true), 1f));   // wing-wide: another speaker too
            Assert.True(q.Enqueue(L(2, RadioClass.Tactical, "bingo"), 1f));
            Assert.True(q.Enqueue(L(2, RadioClass.Tactical, "bingo"), 1f));                 // already queued → replaced, not added
            Assert.Equal(1, q.Queued);
            Assert.True(q.Enqueue(L(1, RadioClass.Tactical, "engage", wing: true), RadioQueue.RepeatSeconds + 0.1f));
            Assert.Equal(1, q.DroppedRepeat);
        }

        [Fact]
        public void AFullQueueEvictsTheOldestLowestClassBelowTheNewLine()
        {
            var q = new RadioQueue();
            for (int i = 0; i < RadioQueue.Capacity; i++) q.Enqueue(L(i % RadioQueue.MaxSpeakers, i == 0 ? RadioClass.Chatter : RadioClass.Status, "k" + i), 0f);
            Assert.True(q.Enqueue(L(1, RadioClass.Tactical, "t"), 0f));
            Assert.Equal(RadioQueue.Capacity, q.Queued);
            Assert.False(q.Enqueue(L(1, RadioClass.Chatter, "c"), 0f));
            Assert.Equal(1, q.DroppedFull);
        }

        [Fact]
        public void AirtimeGrowsWithTheTextWithinItsBounds()
        {
            Assert.Equal(RadioQueue.AirMin, RadioQueue.Airtime("Copy."), 3);
            Assert.Equal(RadioQueue.AirMax, RadioQueue.Airtime(new string('x', 500)), 3);
            Assert.True(RadioQueue.Airtime(new string('x', 60)) > RadioQueue.AirMin);
        }

        [Fact]
        public void EnqueueAndNextAllocateNothing()
        {
            var q = new RadioQueue();
            RadioLine line = L(1, RadioClass.Status, "a");
            q.Enqueue(line, 0f);
            q.Next(0f, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                q.Enqueue(line, i * 10f);
                q.Next(i * 10f, out _);
            }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
