using System;
using System.Reflection;

namespace WingCommand
{
    /// <summary>Resolve the unit-specific repaint across Nuclear Option's method rename.</summary>
    internal static class UnitMapIconRepaint
    {
        internal static MethodInfo Resolve(Type iconType)
        {
            if (iconType == null) throw new ArgumentNullException(nameof(iconType));

            // DeclaredOnly matters: the inherited MapIcon.UpdateColor does not restore unit
            // filter dimming/player highlighting, and must not mask the legacy method.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (string name in new[] { "UpdateColor", "UnitMapIcon_UpdateColor" })
            {
                MethodInfo method = iconType.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(void) && !method.ContainsGenericParameters)
                    return method;
            }

            throw new MissingMethodException(iconType.FullName,
                "void UpdateColor() or void UnitMapIcon_UpdateColor() (declared instance method)");
        }
    }
}
