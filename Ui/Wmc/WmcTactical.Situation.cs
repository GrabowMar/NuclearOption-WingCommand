using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>TACTICAL's alerts (spec bezel v2 §5 TACTICAL): the wing's alerts, worst first, read by the cue row (the top one,
    /// clickable) and the status strip (an urgent one). The alert list, readout and RECENT that sat under the grid went on
    /// 2026-09-28 (the user: "remove logs and that info panel from tactical"): BEHAVIOUR › LOG and SQUADRON › INSPECT carry them.</summary>
    internal sealed partial class WmcTactical
    {
        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private int alertCount = -1, alertKey = int.MinValue;
        private string alertText;

        /// <summary>Alerts standing now (automation).</summary>
        public int AlertsShown => alertCount < 0 ? 0 : alertCount;

        /// <summary>The ids of the order-grid cells that cannot be pressed now (automation).</summary>
        public List<string> DisabledOrders()
        {
            var off = new List<string>();
            for (int k = 0; k < shownCells.Length; k++)
                if (!shownEnabled[k] && shownCells[k].Id != null) off.Add(shownCells[k].Id);
            return off;
        }

        private string AlertLine(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return AlertList.Word(a.Kind) + "  " + WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign)
                + " · " + AlertList.Detail(a);
        }

        private void RefreshAlerts(WmcContext c)
        {
            alertCount = AlertList.Fill(c.Rows, c.Count, c.Client ? null : c.Wing?.Events, c.MissionTime, alerts);
            // The strip's ALERT line carries only the urgent one; the text is rebuilt only when the top alert changes.
            int key = alertCount > 0 && alerts[0].Kind <= AlertKind.Damaged
                ? (int)alerts[0].Kind * 1000003 + (int)(alerts[0].Id % 1000003u) + alerts[0].Slot * 7 + (int)alerts[0].Why * 131
                : 0;
            if (key == alertKey) return;
            alertKey = key;
            alertText = key != 0 ? AlertLine(alerts[0], c) : null;
        }
    }
}
