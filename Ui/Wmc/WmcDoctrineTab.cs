using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>DOCT (spec M7b §3): pattern presets and one stepper per doctrine axis.</summary>
    internal sealed class WmcDoctrineTab : IWmcTab
    {
        private static readonly DoctrineAxis[] Axes =
            { DoctrineAxis.Guard, DoctrineAxis.Response, DoctrineAxis.Interval, DoctrineAxis.Spread, DoctrineAxis.Targets, DoctrineAxis.Reach };

        private readonly Dictionary<string, AvButton> ids;
        private readonly TMP_Text[] values = new TMP_Text[Axes.Length];
        private AvButton reserve, escort, sweep;
        private TMP_Text pattern, shots;
        private WmcContext last;

        public WmcDoctrineTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => 0f;

        public string Hint => "What the wing does on its own while it holds formation.";

        public void Build(RectTransform page, Rect body)
        {
            float y = WmcUi.Title(page, body, body.y, "PATTERN");
            float w = (body.width - WmcUi.Gap * 2f) / 3f;
            reserve = Preset(page, body.x, y, w, "RESERVE", "doct.reserve", WingDoctrine.Reserve);
            escort = Preset(page, body.x + w + WmcUi.Gap, y, w, "ESCORT", "doct.escort", WingDoctrine.Escort);
            sweep = Preset(page, body.x + 2f * (w + WmcUi.Gap), y, w, "SWEEP", "doct.sweep", WingDoctrine.Sweep);
            y -= WmcUi.Row + WmcUi.Gap;
            pattern = AvStyled.Label(page, new Rect(body.x, y, body.width, 18f), "", "row-sub");
            y -= 18f + WmcUi.Gap;
            y = WmcUi.Title(page, body, y, "AXES");
            for (int i = 0; i < Axes.Length; i++)
            {
                DoctrineAxis axis = Axes[i];
                AvStyled.Label(page, new Rect(body.x, y, body.width * 0.5f, WmcUi.Row), DoctrineSteps.Label(axis), "form-key");
                AvButton[] step = AvKit.Stepper(page, body.x + body.width * 0.5f, y, body.width * 0.5f, out values[i],
                    () => Step(axis, -1), () => Step(axis, 1));
                ids["doct." + axis.ToString().ToLowerInvariant() + ".prev"] = step[0];
                ids["doct." + axis.ToString().ToLowerInvariant() + ".next"] = step[1];
                y -= WmcUi.Row + WmcUi.Gap;
            }
            shots = AvStyled.Label(page, new Rect(body.x, y, body.width, 18f), "", "row-sub");
        }

        private AvButton Preset(RectTransform page, float x, float y, float w, string text, string id, WingDoctrine d)
        {
            AvButton b = AvStyled.Button(page, new Rect(x, y, w, WmcUi.Row), text, "btn",
                () => WmcUi.Order(last, () => Set(d)), AvButtonStyle.Toggle);
            ids[id] = b;
            return b;
        }

        private void Step(DoctrineAxis axis, int dir) =>
            WmcUi.Order(last, () => Set(DoctrineSteps.Cycle(last.Wing.Doctrine, axis, dir)));

        private void Set(WingDoctrine d)
        {
            last.Wing.SetDoctrine(d);
            WingToast.Show("Doctrine " + d.PatternName);
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            if (c.Wing == null || c.Client)
            {
                pattern.text = "Doctrine is the host's.";
                return;
            }
            WingDoctrine d = c.Wing.Doctrine;
            reserve.SetLatched(d.Equals(WingDoctrine.Reserve));
            escort.SetLatched(d.Equals(WingDoctrine.Escort));
            sweep.SetLatched(d.Equals(WingDoctrine.Sweep));
            pattern.text = "Current: " + d.PatternName;
            for (int i = 0; i < Axes.Length; i++) values[i].text = DoctrineSteps.Word(d, Axes[i]);
            shots.text = "Standing shots this mission: " + c.Wing.StandingShots;
        }
    }
}
