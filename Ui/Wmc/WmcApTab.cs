using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>AP (spec M7b §3): your autopilot's modes and held values. It flies your own aircraft, so it works on a
    /// client too.</summary>
    internal sealed class WmcApTab : IWmcTab
    {
        private static readonly ApField[] Fields = { ApField.Heading, ApField.Altitude, ApField.VerticalSpeed, ApField.Speed };
        private static readonly string[] FieldNames = { "HEADING", "ALTITUDE", "VERTICAL SPEED", "SPEED" };

        private readonly Dictionary<string, AvButton> ids;
        private readonly TMP_Text[] values = new TMP_Text[Fields.Length];
        private AvButton lvl, hdg, alt, vs, spd, off;
        private TMP_Text line;

        public WmcApTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => 290f;

        public string Hint => "Your stick overrides a hold; it recaptures when you let go.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            float y = WmcUi.Head(page, body, body.y, "MODES");
            float w = (body.width - WmcUi.Gap * 5f) / 6f;
            lvl = Mode(page, body.x, y, w, "LVL", "ap.level", ApCommand.Level);
            hdg = Mode(page, body.x + (w + WmcUi.Gap), y, w, "HDG", "ap.heading", ApCommand.Heading);
            alt = Mode(page, body.x + 2f * (w + WmcUi.Gap), y, w, "ALT", "ap.altitude", ApCommand.Altitude);
            vs = Mode(page, body.x + 3f * (w + WmcUi.Gap), y, w, "VS", "ap.vs", ApCommand.VerticalSpeed);
            spd = Mode(page, body.x + 4f * (w + WmcUi.Gap), y, w, "SPD", "ap.speed", ApCommand.Speed);
            off = Mode(page, body.x + 5f * (w + WmcUi.Gap), y, w, "OFF", "ap.off", ApCommand.Off);
            y -= WmcUi.Row + WmcUi.Gap;
            line = AvStyled.Label(page, new Rect(body.x, y, body.width, 32f), "", "readout");
            line.enableWordWrapping = false;
            line.overflowMode = TextOverflowModes.Ellipsis;
            y -= 32f + WmcUi.Gap;
            y = WmcUi.Head(page, body, y, "HELD VALUES");
            for (int i = 0; i < Fields.Length; i++)
            {
                ApField f = Fields[i];
                AvStyled.Label(page, new Rect(body.x, y, body.width * 0.5f, WmcUi.Row), FieldNames[i], "form-key");
                AvButton[] step = AvKit.Stepper(page, body.x + body.width * 0.5f, y, body.width * 0.5f, out values[i],
                    () => PlayerAutopilot.Instance?.Adjust(f, -1), () => PlayerAutopilot.Instance?.Adjust(f, 1));
                ids["ap." + f.ToString().ToLowerInvariant() + ".down"] = step[0];
                ids["ap." + f.ToString().ToLowerInvariant() + ".up"] = step[1];
                y -= WmcUi.Row + WmcUi.Gap;
            }
        }

        private AvButton Mode(RectTransform page, float x, float y, float w, string text, string id, ApCommand command)
        {
            AvButton b = AvStyled.Button(page, new Rect(x, y, w, WmcUi.Row), text, "btn", () => WingCommands.Autopilot(command),
                AvButtonStyle.Toggle);
            ids[id] = b;
            return b;
        }

        public void Refresh(WmcContext c)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null)
            {
                line.text = "Autopilot unavailable.";
                return;
            }
            HoldSpec h = ap.Session.Spec;
            lvl.SetLatched(h.Lateral == LateralHold.Level);
            hdg.SetLatched(h.Lateral == LateralHold.Heading);
            alt.SetLatched(h.Vertical == VerticalHold.Altitude);
            vs.SetLatched(h.Vertical == VerticalHold.VerticalSpeed);
            spd.SetLatched(h.Speed);
            off.SetLatched(!ap.Session.Engaged);
            string text = WingHudText.Autopilot(h, ap.Session.LateralOverride, ap.Session.VerticalOverride);
            line.text = text.Length > 0 ? text : "AP off";
            for (int i = 0; i < Fields.Length; i++) values[i].text = ApSteps.Readout(h, Fields[i]);
        }
    }
}
