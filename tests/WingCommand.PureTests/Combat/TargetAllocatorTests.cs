using Xunit;

namespace WingCommand.PureTests
{
    public class TargetAllocatorTests
    {
        private static bool[] All(int m, int t) { var a = new bool[m * t]; for (int i = 0; i < a.Length; i++) a[i] = true; return a; }
        private static int[] None(int m) { var a = new int[m]; for (int i = 0; i < m; i++) a[i] = -1; return a; }

        [Fact]
        public void TwoTargetsSplitFourMembersIntoPairsByDistance()
        {
            // members 0,1 near target 0; members 2,3 near target 1
            float[] d = { 1f, 9f, 2f, 8f, 9f, 1f, 8f, 2f };
            var result = new int[4];
            TargetAllocator.Assign(4, 2, All(4, 2), d, new[] { true, true }, None(4), result);
            Assert.Equal(new[] { 0, 0, 1, 1 }, result);
        }

        [Fact]
        public void AMemberIsNeverSentAtATargetItCannotAttack()
        {
            bool[] can = All(2, 2);
            can[0 * 2 + 0] = false;   // member 0 cannot attack target 0
            float[] d = { 1f, 9f, 2f, 9f };
            var result = new int[2];
            TargetAllocator.Assign(2, 2, can, d, new[] { true, true }, None(2), result);
            Assert.Equal(1, result[0]);
            Assert.Equal(0, result[1]);
            bool[] none = new bool[2 * 2];
            TargetAllocator.Assign(2, 2, none, d, new[] { true, true }, None(2), result);
            Assert.Equal(new[] { -1, -1 }, result);
        }

        [Fact]
        public void LeftoversJoinTheTargetWithTheFewestAttackers()
        {
            float[] d = { 1f, 5f, 1f, 5f, 1f, 5f, 1f, 5f, 1f, 5f };
            var result = new int[5];
            TargetAllocator.Assign(5, 2, All(5, 2), d, new[] { true, true }, None(5), result);
            int t0 = 0, t1 = 0;
            foreach (int r in result) { if (r == 0) t0++; if (r == 1) t1++; }
            Assert.Equal(5, t0 + t1);
            Assert.True(t0 >= 2 && t1 >= 2, $"{t0} on 0, {t1} on 1");
        }

        [Fact]
        public void AssignmentsStayWhileValidAndMoveWhenTheirTargetDies()
        {
            float[] d = { 9f, 1f, 1f, 9f };
            var result = new int[2];
            TargetAllocator.Assign(2, 2, All(2, 2), d, new[] { true, true }, new[] { 0, 1 }, result);
            Assert.Equal(new[] { 0, 1 }, result);   // kept, though each is nearer the other target
            TargetAllocator.Assign(2, 2, All(2, 2), d, new[] { false, true }, new[] { 0, 1 }, result);
            Assert.Equal(new[] { 1, 1 }, result);
            TargetAllocator.Assign(2, 2, All(2, 2), d, new[] { false, false }, new[] { 1, 1 }, result);
            Assert.Equal(new[] { -1, -1 }, result);
        }
    }
}
