using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    /// <summary>P3: the DEBRIEF (spec WMC rebuild §PLAN LOG and DEBRIEF): a sortie folded from the wing's events as they come (the ring
    /// keeps only the last 256), with kill credit, said in a few lines.</summary>
    public class SortieLogTests
    {
        private static WingEvent E(WingEventKind k, float t, TransitionReason r = TransitionReason.None) =>
            new WingEvent { Kind = k, Time = t, Reason = r };

        [Fact]
        public void ASortieCountsWhatHappenedToTheWing()
        {
            var log = new SortieLog();
            log.Begin(100f);
            log.Add(E(WingEventKind.GroundSpawned, 101f));
            log.Add(E(WingEventKind.GroundSpawned, 102f));
            log.Add(E(WingEventKind.Airborne, 150f));
            log.Add(E(WingEventKind.Airborne, 155f));
            log.Add(E(WingEventKind.TaskCompleted, 300f));
            log.Add(E(WingEventKind.TaskFailed, 320f));
            log.Add(E(WingEventKind.TargetDestroyed, 330f));
            log.Add(E(WingEventKind.MemberLost, 400f, TransitionReason.Killed));
            log.Add(E(WingEventKind.MemberLost, 410f, TransitionReason.Ejected));
            log.Add(E(WingEventKind.Landed, 500f));
            log.Add(E(WingEventKind.Relocated, 120f));
            log.Add(E(WingEventKind.GcasActivated, 200f));
            log.Kill("RAVEN", "FS-12 Revoker");
            log.Kill("RAVEN", "FS-12 Revoker");
            log.Kill("TORCH", "SAM Site");
            log.Finish(700f);
            Assert.Equal(2, log.Launched);
            Assert.Equal(2, log.Airborne);
            Assert.Equal(1, log.Landed);
            Assert.Equal(2, log.Lost);
            Assert.Equal(1, log.Ejected);
            Assert.Equal(3, log.Kills);
            Assert.Equal(1, log.TasksDone);
            Assert.Equal(1, log.TasksFailed);
            List<string> lines = log.Lines();
            Assert.Equal("SORTIE 10:00 · 2 LAUNCHED · 2 AIRBORNE · 1 LANDED · 2 LOST", lines[0]);
            Assert.Contains("KILLS 3 · RAVEN 2 · TORCH 1", lines);
            Assert.Contains("TASKS 1 DONE · 1 FAILED · 1 TARGET DOWN", lines);
            Assert.Contains("GROUND 1 MOVED · 0 ABORTED · SAFETY 1 GCAS", lines);
        }

        [Fact]
        public void AnEmptySortieSaysSoAndTheWordsUseSafeGlyphs()
        {
            var log = new SortieLog();
            log.Begin(0f);
            log.Finish(65f);
            List<string> lines = log.Lines();
            Assert.Equal("SORTIE 1:05 · NOTHING FLOWN", lines[0]);
            foreach (string s in lines)
                foreach (char ch in s)
                    Assert.False(ch == '…' || (ch >= '←' && ch <= '⇿') || (ch >= '─' && ch <= '◿'), s);
        }

        [Fact]
        public void TheLastTenSortiesSurviveTheFile()
        {
            var store = new DebriefStore();
            for (int i = 0; i < 12; i++)
            {
                var log = new SortieLog { Theatre = "T" + i };
                log.Begin(0f);
                log.Kill("RAVEN", "FS-12 Revoker");
                log.Finish(60f * (i + 1));
                store.Add(log.Lines(), log.Theatre);
            }
            Assert.Equal(DebriefStore.Max, store.Count);
            DebriefStore back = DebriefStore.FromJson(store.ToJson(), new List<string>());
            Assert.Equal(DebriefStore.Max, back.Count);
            Assert.Equal("T11", back.Theatre(0));           // newest first
            Assert.Equal(store.Lines(0), back.Lines(0));
        }
    }
}
