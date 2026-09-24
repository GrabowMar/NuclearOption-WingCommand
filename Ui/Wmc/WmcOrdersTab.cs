using System.Collections.Generic;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>ORDERS (spec M7b §3): the task card, then TASK, COMBAT and HELO orders — the same entry points as the
    /// radial menu. Map orders arrive in M7b-2.</summary>
    internal sealed class WmcOrdersTab : IWmcTab
    {
        private readonly Dictionary<string, AvButton> ids;
        private readonly List<AvButton> own = new List<AvButton>();
        private TMP_Text card;
        private WmcContext last;

        public WmcOrdersTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => 360f;

        public string Hint => last != null && !last.CanOrder ? (last.Client ? "Orders are host only for now." : "Not ready.")
            : "Orders go to the whole wing. Form Up brings it back.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            var cardRect = new Rect(body.x, body.y, body.width, 34f);
            WmcUi.Card(page, cardRect, null, out _, out Image rail);
            WmcUi.SetRail(rail, "info");
            card = AvStyled.Label(page, new Rect(body.x + 12f, body.y - 8f, body.width - 22f, 18f), "", "row-main");
            card.enableWordWrapping = false;
            card.overflowMode = TextOverflowModes.Ellipsis;
            float y = body.y - 34f - WmcUi.Gap - 4f;
            y = WmcUi.Head(page, body, y, "TASK");
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.form", "FORM UP", () => O(WingCommands.FormUp), "Back to formation on you."),
                ("orders.orbit", "ORBIT HERE", () => O(WingCommands.OrbitHere), "Orbit over your position."),
                ("orders.hold", "HOLD HERE", () => O(WingCommands.HoldHere), "Hold over your position."),
                ("orders.move", "MOVE AHEAD", () => O(WingCommands.MoveAhead), "Fly to a point 10 km ahead, then orbit."),
                ("orders.patrol", "PATROL HERE", () => O(WingCommands.PatrolHere), "A 20 km racetrack along your heading."),
                ("orders.rtb", "RTB", () => O(WingCommands.Rtb), "Everyone home."),
                ("orders.refit", "REFIT", () => O(WingCommands.Refit), "Home to refuel and rearm, then back out."));
            y = WmcUi.Head(page, body, y, "COMBAT");
            y = WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.engage", "ENGAGE", () => O(WingCommands.Engage), "Fight the enemies in reach."),
                ("orders.attack", "ATTACK TGT", () => O(WingCommands.AttackTarget), "Attack your selected targets."),
                ("orders.splash", "SPLASH", () => O(WingCommands.Splash), "One missile at the nearest enemy aircraft."),
                ("orders.buddy", "BUDDY ATTACK", () => O(WingCommands.BuddyAttack), "The second element attacks your targets."),
                ("orders.bogey", "BOGEY DOPE", () => O(WingCommands.BogeyDope), "Nearest threat: bearing, range, altitude."),
                ("orders.breakoff", "BREAK OFF", () => O(WingCommands.Disengage), "Stop fighting and rejoin."));
            y = WmcUi.Head(page, body, y, "HELO");
            WmcUi.Buttons(page, body, y, 3, ids,
                ("orders.land", "LAND HERE", () => O(WingCommands.LandHere), "Helicopters land near you."),
                ("orders.takeoff", "TAKE OFF", () => O(WingCommands.TakeOff), "Landed helicopters lift off."),
                ("orders.cargo", "DELIVER CARGO", () => O(WingCommands.DeliverCargo), "Helicopters drop their cargo here."),
                ("orders.rescue", "RESCUE", () => O(WingCommands.Rescue), "A helicopter picks up a downed pilot."),
                ("orders.scout", "SCOUT AHEAD", () => O(WingCommands.ScoutAhead), "A low route ahead, reporting contacts."));
            foreach (KeyValuePair<string, AvButton> kv in ids)
                if (kv.Key.StartsWith("orders.")) own.Add(kv.Value);
        }

        private void O(System.Action order) => WmcUi.Order(last, order);

        public void Refresh(WmcContext c)
        {
            last = c;
            WingService w = c.Wing;
            card.text = c.Client ? "Task: the host's" : w == null ? "FORM · on you"
                : TaskCard.Text(w.Planner.Active ? w.Planner.Current : null, w.Planner.Leg,
                    w.Planner.Active ? w.Planner.Lead.Position : Vec3.Zero, w.Planner.Active ? w.Planner.Lead.Speed : 0f);
            foreach (AvButton b in own) b.SetEnabled(c.CanOrder);
        }
    }
}
