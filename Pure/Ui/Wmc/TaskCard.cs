namespace WingCommand
{
    /// <summary>The ORDERS tab's task line (spec M7b §3): kind, leg, distance and ETA from the task lead, and what
    /// follows. Horizontal distance; no ETA below 1 m/s.</summary>
    internal static class TaskCard
    {
        public static string Text(WingTask task, int leg, Vec3 lead, float leadSpeed)
        {
            if (task == null || task.Kind == TaskKind.Form) return "FORM · on you";
            string kind = task.Kind.ToString().ToUpperInvariant();
            int n = task.Points?.Length ?? 0;
            if (n == 0) return kind + " · no points";
            bool path = task.Kind == TaskKind.Move || task.Kind == TaskKind.Route || task.Kind == TaskKind.Patrol;
            int i = path ? System.Math.Max(0, System.Math.Min(n - 1, leg)) : 0;
            Waypoint p = task.Points[i];
            float d = (new Vec3(p.X, 0f, p.Z) - lead.Horizontal).Length;
            string far = WmcText.Km(d) + " · ETA " + (leadSpeed > 1f ? WmcText.Clock(d / leadSpeed) : WmcText.Unknown);
            if (path)
            {
                string head = kind + " · leg " + (i + 1) + "/" + n + " · " + far;
                if (task.Kind == TaskKind.Patrol) return head + (task.Loop ? " · LOOP" : " · PING-PONG");
                return head + " · then " + (task.Then == FollowOn.Form ? "FORM" : "ORBIT");
            }
            string where = d <= WingPlanner.ArriveRadius ? "on station" : far;
            return kind + " · " + where + (task.Seconds > 0f ? " · for " + WmcText.Clock(task.Seconds) : "");
        }

        /// <summary>The task in an element header's width (spec WMC rebuild §TACTICAL): kind, leg of a multi-point path, ETA;
        /// "ON STATION" for a held point inside the arrive radius.</summary>
        public static string Short(WingTask task, int leg, Vec3 lead, float leadSpeed)
        {
            if (task == null || task.Kind == TaskKind.Form) return "FORM";
            string kind = task.Kind.ToString().ToUpperInvariant();
            int n = task.Points?.Length ?? 0;
            if (n == 0) return kind;
            bool path = task.Kind == TaskKind.Move || task.Kind == TaskKind.Route || task.Kind == TaskKind.Patrol;
            int i = path ? System.Math.Max(0, System.Math.Min(n - 1, leg)) : 0;
            Waypoint p = task.Points[i];
            float d = (new Vec3(p.X, 0f, p.Z) - lead.Horizontal).Length;
            string eta = "ETA " + (leadSpeed > 1f ? WmcText.Clock(d / leadSpeed) : WmcText.Unknown);
            if (path) return n > 1 ? kind + " · LEG " + (i + 1) + "/" + n + " · " + eta : kind + " · " + eta;
            return d <= WingPlanner.ArriveRadius ? kind + " · ON STATION" : kind + " · " + eta;
        }
    }
}
