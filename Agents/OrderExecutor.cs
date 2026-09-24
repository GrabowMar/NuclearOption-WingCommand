using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Spec WMC program §3.2: applies a <see cref="WingOrder"/> on the host and answers in words — the only place an
    /// order is refused. A task goes to the element its scope resolves to (detaching the selected members when needed);
    /// an immediate order acts on the members its scope names. <see cref="OrderResult.Reason"/> and
    /// <see cref="OrderResult.Ack"/> are the full text to show; an empty ack shows nothing (the radio said it).</summary>
    internal static class OrderExecutor
    {
        private static readonly List<Unit> units = new List<Unit>();

        public static OrderResult Execute(WingOrder o)
        {
            string invalid = OrderValidator.Check(o);
            if (invalid != null) return OrderResult.Refused("Wing cannot: " + invalid);
            WingService w = WingService.Instance;
            if (w?.Selection == null) return OrderResult.Refused("Wing Command is not ready");
            if (o.Kind == OrderKind.Task) return Task(w, o);
            // Review P2 I5: an order that reaches nobody is refused, and nobody moves.
            string nobody = w.Roster.Check(o.Scope);
            if (nobody != null) return OrderResult.Refused("Wing cannot: " + nobody);
            switch (o.Kind)
            {
                case OrderKind.FormUp: return FormUp(w, o.Scope);
                case OrderKind.SkipLeg:
                {
                    int e = ElementFor(w, o.Scope);
                    return w.SkipLeg(e) ? OrderResult.Acked("Next leg", e) : OrderResult.Refused("No route to skip");
                }
                case OrderKind.RenameElement:
                {
                    int e = ElementFor(w, o.Scope);
                    string why = w.Roster.Rename(e, o.Text);
                    return why == null ? OrderResult.Acked($"Element {ElementRoster.Letter(e)} is {w.Roster.Name(e)}", e)
                        : OrderResult.Refused("Cannot rename: " + why);
                }
            }
            return Immediate(w, o, Who(w, o.Scope));
        }

        private static OrderResult Task(WingService w, WingOrder o)
        {
            ScopeTarget t = w.Roster.Resolve(o.Scope);
            if (t.Element < 0) return OrderResult.Refused("Wing cannot: " + t.Reason);
            if (t.Everyone) MergeAll(w);
            OrderResult r = w.OrderElement(t.Element, o.Task);
            if (!r.Accepted)
            {
                // A detach only happens to carry an order: a refused one puts everyone back (review P2 m9).
                if (t.Detached) w.Roster.UndoDetach();
                return OrderResult.Refused("Wing cannot: " + r.Reason);
            }
            string who = t.Element == 0 ? "Wing" : w.Roster.Name(t.Element);
            return OrderResult.Acked($"{who}: {TaskWords(o.Task)}", t.Element);
        }

        private static string TaskWords(WingTask t)
        {
            if (t == null) return "forming up";
            switch (t.Kind)
            {
                case TaskKind.Move: return t.Scout ? "scouting" : "moving";
                case TaskKind.Route: return "flying the route";
                case TaskKind.Patrol: return "patrolling";
                case TaskKind.Orbit: return "orbiting";
                case TaskKind.Hold: return "holding";
                default: return "forming up";
            }
        }

        private static OrderResult FormUp(WingService w, in WingScope scope)
        {
            if (scope.Kind == ScopeKind.Wing)
            {
                MergeAll(w);
                w.FormUp();
                return OrderResult.Acked("Form up", 0);
            }
            Func<WingMember, bool> who = Who(w, scope);
            // The members first: merging moves them to A, after which the element filter matches nobody (review P2 I4).
            var chosen = new List<WingMember>();
            foreach (WingMember m in w.Members)
                if (who(m)) chosen.Add(m);
            // ponytail: forming up some members brings their whole elements back to A.
            for (int e = 1; e < ElementRoster.MaxElements; e++)
                foreach (WingMember m in chosen)
                    if (w.ElementOf(m) == e)
                    {
                        w.MergeElement(e);
                        break;
                    }
            if (scope.Kind == ScopeKind.Element && scope.Element == 0 && w.Planner.Active) w.Order(WingTask.Form());
            Func<WingMember, bool> these = chosen.Contains;
            w.Disengage(these);
            w.TakeOff(these);
            foreach (WingMember m in chosen) m.Brain.FormUp(w.MissionTime, w.Events);
            return OrderResult.Acked(chosen.Count == 1 ? "Rejoining" : $"{chosen.Count} rejoining", 0);
        }

        private static OrderResult Immediate(WingService w, WingOrder o, Func<WingMember, bool> who)
        {
            switch (o.Kind)
            {
                case OrderKind.Rtb: return Recover(w, RecoveryIntent.Rtb, "returning to base", who);
                case OrderKind.Refit: return Recover(w, RecoveryIntent.Refit, "going to refit", who);
                case OrderKind.Engage:
                {
                    if (!w.MayEngage(out int hostiles, out int members))
                        return OrderResult.Refused($"Outnumbered {hostiles} to {members} - Engage again to fight anyway");
                    int n = w.Engage(who);
                    return n > 0 ? OrderResult.Acked($"{n} engaging") : OrderResult.Refused("Nobody can engage");
                }
                case OrderKind.Attack:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("No enemy target selected");
                    int n = w.Attack(units, who);
                    string plural = units.Count == 1 ? "" : "s";
                    return n > 0 ? OrderResult.Acked($"{n} attacking {units.Count} target{plural}") : OrderResult.Refused("Nobody can attack");
                }
                case OrderKind.Splash:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("Splash: select a target first");
                    int fired = w.Splash(units[0], out int capable, who);
                    if (fired > 0) WingRadioAudio.Play(WingRadioAudio.Earcon.Splash);
                    return fired > 0 ? OrderResult.Acked($"Splash: {fired} firing on {units[0].unitName}")
                        : OrderResult.Refused(capable > 0 ? "Splash: launchers not ready" : "Splash: nobody in range");
                }
                case OrderKind.BreakOff:
                {
                    int n = w.Disengage(who);
                    return n > 0 ? OrderResult.Acked($"{n} disengaging") : OrderResult.Refused("Nobody engaged");
                }
                case OrderKind.BogeyDope:
                {
                    if (w.Player == null) return OrderResult.Refused("Not flying");
                    // The radio says it; when it will not (radio off, or the line dropped), the answer is shown (review M7a I4).
                    return OrderResult.Acked(WingCommands.BogeyDopeCall(w, out string answer) ? "" : answer);
                }
                case OrderKind.ClearSix:
                {
                    if (w.Player == null) return OrderResult.Refused("Not flying");
                    int found = w.ClearMySix(out int engaged);
                    return found == 0 ? OrderResult.Acked("Your six is clear")
                        : engaged > 0 ? OrderResult.Acked($"{engaged} clearing your six ({found} bandits)") : OrderResult.Refused("Nobody can engage");
                }
                case OrderKind.LandHere:
                {
                    int n = w.LandHere(out string refusal, who);
                    return n > 0 ? OrderResult.Acked($"{n} landing here") : OrderResult.Refused("Cannot land here: " + refusal);
                }
                case OrderKind.TakeOff:
                {
                    int n = w.TakeOff(who);
                    return n > 0 ? OrderResult.Acked($"{n} lifting off") : OrderResult.Refused("Nobody is down");
                }
                case OrderKind.DeliverCargo:
                {
                    int n = w.DeliverCargo(out string refusal, who);
                    return n > 0 ? OrderResult.Acked($"Deliver cargo: {n} landing") : OrderResult.Refused("Deliver cargo: " + refusal);
                }
                case OrderKind.Rescue:
                {
                    // ponytail: the rescue picks the best-placed helicopter of the wing whatever the scope.
                    WingMember m = w.Rescue(out string result);
                    return m != null ? OrderResult.Acked("Rescue: " + result) : OrderResult.Refused("Rescue: " + result);
                }
                case OrderKind.EscortMe:
                    if (!w.Escorting) return OrderResult.Refused("Already on you");
                    w.SetEscort(null);
                    return OrderResult.Acked("Escorting you");
                case OrderKind.EscortTarget:
                {
                    Units(o.Units);
                    if (units.Count == 0) return OrderResult.Refused("No friendly to escort");
                    w.SetEscort(units[0]);
                    return OrderResult.Acked("Escorting " + units[0].unitName);
                }
                case OrderKind.Release:
                {
                    if (who == null) return OrderResult.Refused("Select who to release");
                    int n = 0;
                    foreach (WingMember m in w.Members)
                        if (!m.Released && who(m))
                        {
                            w.Release(m, "released by order");
                            n++;
                        }
                    return n > 0 ? OrderResult.Acked(n == 1 ? "Wingman released" : $"{n} wingmen released") : OrderResult.Refused("Nobody to release");
                }
                case OrderKind.Dismiss:
                    if (w.Members.Count == 0) return OrderResult.Refused("No wingmen to dismiss");
                    w.Dismiss();
                    return OrderResult.Acked("Wing dismissed");
                case OrderKind.Recruit:
                {
                    Units(o.Units);
                    if (units.Count == 0 || !(units[0] is Aircraft target)) return OrderResult.Refused("No friendly aircraft to recruit");
                    return WingRecruitment.TryRecruit(w, target, out _, out string reason)
                        ? OrderResult.Acked(target.unitName + " joins the wing")
                        : OrderResult.Refused("Cannot recruit " + target.unitName + ": " + reason);
                }
                case OrderKind.Call: return Call(w, (int)o.Number);
                case OrderKind.SetShape:
                    return w.SetShape(o.Text, ElementFor(w, o.Scope)) ? OrderResult.Acked("") : OrderResult.Refused("No such shape: " + o.Text);
                case OrderKind.NextShape:
                    w.NextShape();
                    return OrderResult.Acked("");
                case OrderKind.NextFamily:
                    w.NextFamily();
                    return OrderResult.Acked("");
                case OrderKind.SetSpacing:
                    w.SetSpacing((SpacingPreset)(int)o.Number);
                    return OrderResult.Acked("");
                case OrderKind.Stack:
                    w.SetStack(o.Number);
                    return OrderResult.Acked(string.IsNullOrEmpty(o.Text) ? "Stack set" : o.Text);
                case OrderKind.Afterburner:
                    w.SetAfterburner(o.Flag);
                    return OrderResult.Acked(o.Flag ? "Gate: afterburner allowed" : "Buster: military power, no afterburner");
                case OrderKind.SetDoctrine:
                {
                    if (!WingDoctrine.TryParse(o.Text, out WingDoctrine d)) return OrderResult.Refused("Unknown doctrine: " + o.Text);
                    int e = ElementFor(w, o.Scope);
                    w.SetDoctrine(d, e);
                    return OrderResult.Acked((e > 0 ? w.Roster.Name(e) + ": doctrine " : "Doctrine ") + d.PatternName);
                }
                case OrderKind.NextDoctrine:
                {
                    int e = ElementFor(w, o.Scope);
                    WingDoctrine d = e > 0 ? w.DoctrineOf(e).NextPattern() : w.NextDoctrine();
                    if (e > 0) w.SetDoctrine(d, e);
                    string what = d.Targets == TargetPolicy.Hold ? "holding fire"
                        : d.Targets == TargetPolicy.Cover ? "covering you"
                        : "targets of opportunity";
                    return OrderResult.Acked($"Doctrine {d.PatternName}: {what}");
                }
                default:
                    return OrderResult.Refused("Not supported yet");
            }
        }

        private static OrderResult Recover(WingService w, RecoveryIntent intent, string what, Func<WingMember, bool> who)
        {
            int n = w.RecoverAll(intent, who);
            return n > 0 ? OrderResult.Acked($"{n} wingm{(n == 1 ? "an" : "en")} {what}")
                : OrderResult.Refused("No wingman can go: no friendly field, or already going");
        }

        /// <summary>Wingmen always launch from a field (the picked one, else the nearest friendly one).</summary>
        private static OrderResult Call(WingService w, int n)
        {
            if (SpawnService.Instance == null) return OrderResult.Refused("Wing Command is not ready");
            Aircraft caller = w.Player;
            if (caller == null) return OrderResult.Refused("Not flying");
            AircraftDefinition type = WingCommands.CallAirframe() ?? caller.definition;
            Airbase field = w.FieldFor(caller, type);
            if (field == null) return OrderResult.Refused("No friendly field to launch from");
            SpawnService.Instance.LaunchFromField(field, type, n);
            return OrderResult.Acked("");
        }

        /// <summary>Who an immediate order is for: everyone (null), an element's members, or the named aircraft.</summary>
        private static Func<WingMember, bool> Who(WingService w, WingScope scope)
        {
            switch (scope.Kind)
            {
                case ScopeKind.Element:
                    int e = scope.Element;
                    return m => w.ElementOf(m) == e;
                case ScopeKind.Members:
                    uint[] named = scope.Members;
                    return m => (object)m.Aircraft != null && Array.IndexOf(named, m.Aircraft.persistentID.Id) >= 0;
                default:
                    return null;
            }
        }

        /// <summary>The element a scope points at without detaching anyone (the first named member's element).</summary>
        private static int ElementFor(WingService w, in WingScope scope)
        {
            if (scope.Kind == ScopeKind.Element) return scope.Element;
            if (scope.Kind == ScopeKind.Members && scope.Members != null && scope.Members.Length > 0)
                return w.Roster.ElementOf(scope.Members[0]);
            return 0;
        }

        private static void MergeAll(WingService w)
        {
            for (int e = 1; e < ElementRoster.MaxElements; e++)
                if (w.Roster.InUse(e)) w.MergeElement(e);
        }

        private static void Units(uint[] from)
        {
            units.Clear();
            if (from == null) return;
            foreach (uint id in from)
                if (new PersistentID { Id = id }.TryGetUnit(out Unit u) && u != null && !u.disabled) units.Add(u);
        }
    }
}
