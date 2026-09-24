namespace WingCommand
{
    /// <summary>What a wing event says on the radio: its class, the chatter line's name and the dedupe scope.</summary>
    internal struct RadioCall
    {
        public RadioClass Class;
        public string Name;
        public bool WingWide;
    }

    /// <summary>Spec M7 §1.3: wing events to radio calls. Every event not listed says nothing.</summary>
    internal static class RadioCalls
    {
        public static bool For(in WingEvent e, out RadioCall call)
        {
            call = default;
            switch (e.Kind)
            {
                case WingEventKind.BehaviourChanged:
                    // A maneuver speaks when it starts (by order, later by a rule) and when it is flown out.
                    if (e.To == BehaviourId.React) return Make(RadioClass.Tactical, "MANEUVERING", true, out call);
                    if (e.From == BehaviourId.React && e.Reason == TransitionReason.ManeuverDone)
                        return Make(RadioClass.Status, "MANEUVERDONE", false, out call);
                    switch (e.Reason)
                    {
                        // PANIC, not DEFENDING: some DEFENDING lines say "covering" or "all clear" (review M7a I1).
                        case TransitionReason.MissileInbound: return Make(RadioClass.Emergency, "PANIC", false, out call);
                        case TransitionReason.MissileClear: return Make(RadioClass.Tactical, "DEFENSIVECLEAR", false, out call);
                        default: return false;
                    }
                case WingEventKind.Engaged: return Make(RadioClass.Tactical, "ENGAGING", true, out call);
                case WingEventKind.Disengaged:
                    switch (e.Reason)
                    {
                        case TransitionReason.Winchester: return Make(RadioClass.Status, "WINCHESTER", false, out call);
                        case TransitionReason.Outnumbered: return Make(RadioClass.Tactical, "FALLINGBACK", true, out call);
                        case TransitionReason.Leash:
                        case TransitionReason.NoTarget:
                        case TransitionReason.Commanded: return Make(RadioClass.Status, "REJOINING", true, out call);
                        default: return false;
                    }
                case WingEventKind.Joker: return Make(RadioClass.Status, "JOKER", false, out call);
                case WingEventKind.Bingo: return Make(RadioClass.Status, "BINGO", false, out call);
                case WingEventKind.FallingBehind: return Make(RadioClass.Status, "FALLINGBEHIND", false, out call);
                case WingEventKind.GcasActivated: return Make(RadioClass.Emergency, "PULLUP", false, out call);
                case WingEventKind.CollisionEmergency: return Make(RadioClass.Emergency, "BREAKOFF", false, out call);
                // Pushed only when an escortee is gone; the wing then forms on the player (review M7a I1).
                case WingEventKind.AnchorLost: return Make(RadioClass.Tactical, "ESCORTLOST", true, out call);
                case WingEventKind.Airborne: return Make(RadioClass.Status, "AIRBORNEREJOINING", false, out call);
                case WingEventKind.Landed: return Make(RadioClass.Chatter, "DOWN", false, out call);
                case WingEventKind.LandingFailed: return Make(RadioClass.Status, "GOAROUND", false, out call);
                case WingEventKind.TaskCompleted: return Make(RadioClass.Status, "TASKDONE", true, out call);
                case WingEventKind.TaskFailed: return Make(RadioClass.Status, "UNABLEORDER", true, out call);
                case WingEventKind.MemberLost:
                    switch (e.Reason)
                    {
                        case TransitionReason.Killed: return Make(RadioClass.Tactical, "AIRFRAMELOST", false, out call);
                        case TransitionReason.Ejected: return Make(RadioClass.Tactical, "EJECTED", false, out call);
                        default: return false;
                    }
                case WingEventKind.Damaged: return Make(RadioClass.Status, "DAMAGED", false, out call);
                // In formation nothing sends the member home: OUTOFAMMO, never WINCHESTER's RTB lines.
                case WingEventKind.Winchester: return Make(RadioClass.Status, "OUTOFAMMO", false, out call);
                default: return false;
            }
        }

        /// <summary>The Winchester line for what the member does next: WINCHESTER's lines go home, OUTOFAMMO's rejoin
        /// (review M7a I1: the default follow-on rejoins).</summary>
        public static string WinchesterLine(WinchesterAction after) => after == WinchesterAction.Rejoin ? "OUTOFAMMO" : "WINCHESTER";

        /// <summary>Spec M7 §3: the launch call, as 0.9 (review M5g I2): at an aircraft Fox 2 (infrared) or Fox 3; at the
        /// ground Magnum (anti-radiation) or Rifle.</summary>
        public static string ShotLine(bool air, bool infrared, bool antiRadar) =>
            air ? (infrared ? "FOX2" : "FOX3") : antiRadar ? "MAGNUM" : "RIFLE";

        private static bool Make(RadioClass c, string name, bool wing, out RadioCall call)
        {
            call = new RadioCall { Class = c, Name = name, WingWide = wing };
            return true;
        }
    }

    /// <summary>Reads each event of a <see cref="WingEventRing"/> once, by its running total; events overwritten before
    /// they were read are skipped.</summary>
    internal struct EventCursor
    {
        public long Seen;

        public bool Next(WingEventRing ring, out WingEvent e)
        {
            e = default;
            long oldest = ring.Total - ring.Count;
            if (Seen < oldest) Seen = oldest;
            if (Seen >= ring.Total) return false;
            e = ring[(int)(Seen - oldest)];
            Seen++;
            return true;
        }
    }
}
