using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand.PureTests
{
    public class RollbackJournalTests
    {
        [Fact]
        public void Rollback_RetriesOnlyFailuresInReverseOrder_ThenCloses()
        {
            var journal = new RollbackJournal();
            var calls = new List<int>();
            var errors = new List<Exception>();
            bool fail = true;
            for (int i = 0; i < 4; i++)
            {
                int id = i;
                journal.Add(() =>
                {
                    calls.Add(id);
                    if (fail && id % 2 == 0) throw new InvalidOperationException();
                });
            }

            Assert.False(journal.Rollback(errors.Add));
            Assert.Equal(new[] { 3, 2, 1, 0 }, calls);
            Assert.Equal(2, errors.Count);

            fail = false;
            calls.Clear();
            Assert.True(journal.Rollback());
            Assert.Equal(new[] { 2, 0 }, calls);

            calls.Clear();
            journal.Add(() => calls.Add(99));
            Assert.True(journal.Rollback());
            Assert.Empty(calls);
        }

        [Fact]
        public void Commit_DiscardsCompensationsAndCloses()
        {
            var journal = new RollbackJournal();
            journal.Add(() => throw new InvalidOperationException());
            journal.Commit();
            journal.Add(() => throw new InvalidOperationException());
            Assert.True(journal.Rollback());
        }
    }
}
