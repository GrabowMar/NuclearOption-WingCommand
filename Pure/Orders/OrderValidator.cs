using System;

namespace WingCommand
{
    /// <summary>Checks an order's scope and arguments before anything runs (spec WMC program §3.2). Null is valid; anything
    /// else is the refusal in words. The planner and the services still judge the world (points on the map, a target in
    /// reach).</summary>
    internal static class OrderValidator
    {
        public static float MaxStack = 1000f;

        public static string Check(WingOrder o)
        {
            if (o == null) return "no order";
            switch (o.Scope.Kind)
            {
                case ScopeKind.Element:
                    if (o.Scope.Element < 0 || o.Scope.Element >= WingScope.MaxElements) return "no such element";
                    break;
                case ScopeKind.Members:
                    if (o.Scope.Members == null || o.Scope.Members.Length == 0) return "nobody selected";
                    if (o.Scope.Members.Length > WingScope.MaxMembers) return "too many selected";
                    break;
            }
            switch (o.Kind)
            {
                case OrderKind.Task:
                    return o.Task == null ? "no task" : null;
                case OrderKind.Attack:
                case OrderKind.EscortTarget:
                    if (o.Units == null || o.Units.Length == 0) return "no target";
                    return o.Units.Length > WingOrder.MaxUnits ? "too many targets" : null;
                case OrderKind.Recruit:
                    if (o.Units == null || o.Units.Length == 0) return "nothing selected to recruit";
                    return o.Units.Length > WingOrder.MaxUnits ? "too many selected" : null;
                case OrderKind.SetShape:
                case OrderKind.SetDoctrine:
                case OrderKind.RenameElement:
                    if (string.IsNullOrEmpty(o.Text)) return "no name";
                    return o.Text.Length > WingOrder.MaxText ? "name too long" : null;
                case OrderKind.Call:
                    return o.Number >= 1f && o.Number <= 7f ? null : "call 1 to 7 aircraft";
                case OrderKind.Stack:
                    return !float.IsNaN(o.Number) && Math.Abs(o.Number) <= MaxStack ? null : "stack out of range";
                case OrderKind.SetSpacing:
                    return o.Number >= 0f && o.Number <= 3f ? null : "no such spacing";
                default:
                    return null;
            }
        }
    }
}
