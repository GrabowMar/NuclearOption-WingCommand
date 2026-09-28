using Xunit;

namespace WingCommand.PureTests
{
    public class ElementCardTests
    {
        [Fact]
        public void HeaderNamesLetterNameTaskAndSize()
        {
            Assert.Equal("A · WING · FORM · 3", ElementCard.Header(0, "A", "FORM", 3));
            Assert.Equal("B · VIPER · PATROL · 2", ElementCard.Header(1, "VIPER", "PATROL", 2));
            Assert.Equal("C · C · — · 0", ElementCard.Header(2, null, null, 0));
        }

        [Fact]
        public void MembersListsTheElementsNumbers()
        {
            var rows = new[]
            {
                new SnapshotMember { Id = 1, Slot = 0, Element = 0 }, new SnapshotMember { Id = 2, Slot = 1, Element = 1 },
                new SnapshotMember { Id = 3, Slot = 2, Element = 1 },
            };
            Assert.Equal("#3 #4", ElementCard.Members(rows, 3, 1));
            Assert.Equal("#2", ElementCard.Members(rows, 3, 0));
            Assert.Equal("—", ElementCard.Members(rows, 3, 3));
        }
    }
}
