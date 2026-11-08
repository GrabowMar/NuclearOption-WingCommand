using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class UnitMapIconRepaintTests
    {
        public class BaseIcon
        {
            public int BaseRepaints;
            public void UpdateColor() => BaseRepaints++;
        }

        public class CurrentIcon : BaseIcon
        {
            public int UnitRepaints;
            public new void UpdateColor() => UnitRepaints++;
            public void UpdateColor(bool selected) => throw new InvalidOperationException("Wrong overload");
        }

        public class LegacyIcon : BaseIcon
        {
            public int UnitRepaints;
            public void UnitMapIcon_UpdateColor() => UnitRepaints++;
        }

        public class BothNames : LegacyIcon
        {
            public new void UpdateColor() => UnitRepaints++;
            public new void UnitMapIcon_UpdateColor() => throw new InvalidOperationException("Legacy fallback used");
        }

        public class MissingUnitRepaint : BaseIcon { }
        public class WrongSignature : BaseIcon { public new int UpdateColor() => 1; }

        [Fact]
        public void CurrentMethodRepaintsUnitInsteadOfBaseOrOverload()
        {
            var icon = new CurrentIcon();
            var method = UnitMapIconRepaint.Resolve(typeof(CurrentIcon));
            var repaint = (Action<CurrentIcon>)Delegate.CreateDelegate(typeof(Action<CurrentIcon>), method);
            repaint(icon);
            Assert.Equal(typeof(CurrentIcon), method.DeclaringType);
            Assert.Equal(1, icon.UnitRepaints);
            Assert.Equal(0, icon.BaseRepaints);
        }

        [Fact]
        public void LegacyMethodWinsOverInheritedBaseUpdateColor()
        {
            var icon = new LegacyIcon();
            var method = UnitMapIconRepaint.Resolve(typeof(LegacyIcon));
            var repaint = (Action<LegacyIcon>)Delegate.CreateDelegate(typeof(Action<LegacyIcon>), method);
            repaint(icon);
            Assert.Equal("UnitMapIcon_UpdateColor", method.Name);
            Assert.Equal(1, icon.UnitRepaints);
            Assert.Equal(0, icon.BaseRepaints);
        }

        [Fact]
        public void PrefersCurrentNameWhenBothAreDeclared()
        {
            var icon = new BothNames();
            var method = UnitMapIconRepaint.Resolve(typeof(BothNames));
            method.Invoke(icon, null);
            Assert.Equal("UpdateColor", method.Name);
            Assert.Equal(1, icon.UnitRepaints);
        }

        [Theory]
        [InlineData(typeof(MissingUnitRepaint))]
        [InlineData(typeof(WrongSignature))]
        public void UnsupportedUnitApiReportsBothNamesInsteadOfUsingBase(Type type)
        {
            var error = Assert.Throws<MissingMethodException>(() => UnitMapIconRepaint.Resolve(type));
            Assert.Contains(type.FullName, error.Message);
            Assert.Contains("UnitMapIcon_UpdateColor", error.Message);
            Assert.Contains("UpdateColor", error.Message);
        }
    }
}
