using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    /// <summary>FORM (spec M7b §3): family segment, the family's shapes, spacing, stack and Buster/Gate. The shape
    /// editor (EDIT SHAPES) arrives with the room in M7b-3.</summary>
    internal sealed class WmcFormTab : IWmcTab
    {
        private const int MaxShapes = 12, MaxFamilies = 4;

        private readonly Dictionary<string, AvButton> ids;
        private readonly List<FormationDefinition> shapes = new List<FormationDefinition>();
        private readonly List<string> families = new List<string>();
        private readonly AvButton[] familyButtons = new AvButton[MaxFamilies];
        private readonly AvButton[] shapeButtons = new AvButton[MaxShapes];
        private readonly AvButton[] spacingButtons = new AvButton[4];
        private readonly string[] shapeIds = new string[MaxShapes];
        private AvButton high, level, low, buster, gate;
        private string family;
        private WmcContext last;

        public WmcFormTab(Dictionary<string, AvButton> controls) => ids = controls;

        public float ContentHeight => 350f;

        public string Hint => "Shape, spacing and stack for the whole wing.";

        public void Build(RectTransform page, Rect body)
        {
            body = WmcUi.Page(page, body, ContentHeight);
            float y = WmcUi.Head(page, body, body.y, "FAMILY");
            float w = (body.width - WmcUi.Gap * (MaxFamilies - 1)) / MaxFamilies;
            for (int i = 0; i < MaxFamilies; i++)
            {
                int k = i;
                familyButtons[i] = AvStyled.Button(page, new Rect(body.x + i * (w + WmcUi.Gap), y, w, WmcUi.Row), "", "btn",
                    () => PickFamily(k), AvButtonStyle.Toggle);
                ids["form.family" + i] = familyButtons[i];
            }
            y -= WmcUi.Row + WmcUi.Gap;
            y = WmcUi.Head(page, body, y, "SHAPE");
            float sw = (body.width - WmcUi.Gap * 2f) / 3f;
            for (int i = 0; i < MaxShapes; i++)
            {
                int k = i;
                shapeButtons[i] = AvStyled.Button(page,
                    new Rect(body.x + (i % 3) * (sw + WmcUi.Gap), y - (i / 3) * (WmcUi.Row + WmcUi.Gap), sw, WmcUi.Row), "", "btn",
                    () => PickShape(k), AvButtonStyle.Toggle);
                ids["form.shape" + i] = shapeButtons[i];
            }
            y -= (MaxShapes / 3) * (WmcUi.Row + WmcUi.Gap);
            y = WmcUi.Head(page, body, y, "SPACING");
            string[] names = { "CLOSE", "STANDARD", "OPEN", "SPREAD" };
            float pw = (body.width - WmcUi.Gap * 3f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var preset = (SpacingPreset)i;
                spacingButtons[i] = AvStyled.Button(page, new Rect(body.x + i * (pw + WmcUi.Gap), y, pw, WmcUi.Row), names[i], "btn",
                    () => WmcUi.Order(last, () => WingCommands.SetSpacing(preset)), AvButtonStyle.Toggle);
                ids["form.spacing" + i] = spacingButtons[i];
            }
            y -= WmcUi.Row + WmcUi.Gap;
            y = WmcUi.Head(page, body, y, "STACK AND POWER");
            float fw = (body.width - WmcUi.Gap * 4f) / 5f;
            high = Toggle(page, body.x, y, fw, "HIGH", "form.high", () => WingCommands.Stack(WingCommands.GoHighMetres, "Going high"));
            level = Toggle(page, body.x + (fw + WmcUi.Gap), y, fw, "LEVEL", "form.level", () => WingCommands.Stack(0f, "Level with you"));
            low = Toggle(page, body.x + 2f * (fw + WmcUi.Gap), y, fw, "LOW", "form.low", () => WingCommands.Stack(WingCommands.GoLowMetres, "Going low"));
            buster = Toggle(page, body.x + 3f * (fw + WmcUi.Gap), y, fw, "BUSTER", "form.buster", () => WingCommands.Afterburner(false));
            gate = Toggle(page, body.x + 4f * (fw + WmcUi.Gap), y, fw, "GATE", "form.gate", () => WingCommands.Afterburner(true));
        }

        private AvButton Toggle(RectTransform page, float x, float y, float w, string text, string id, System.Action act)
        {
            AvButton b = AvStyled.Button(page, new Rect(x, y, w, WmcUi.Row), text, "btn", () => WmcUi.Order(last, act), AvButtonStyle.Toggle);
            ids[id] = b;
            return b;
        }

        private void PickFamily(int i)
        {
            if (i >= families.Count) return;
            WmcUi.Order(last, () =>
            {
                FormationDefinition first = shapes.Find(d => d.Family == families[i]);
                if (first != null && last.Wing.SetShape(first.Id)) WingToast.Show("Formation " + first.Name);
            });
        }

        private void PickShape(int i)
        {
            string id = shapeIds[i];
            if (id == null) return;
            WmcUi.Order(last, () =>
            {
                if (last.Wing.SetShape(id)) WingToast.Show("Formation " + last.Wing.Selection.Current.Name);
            });
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            FormationSelection sel = c.Wing?.Selection;
            if (sel == null || c.Client)
            {
                foreach (AvButton b in familyButtons) b.gameObject.SetActive(false);
                foreach (AvButton b in shapeButtons) b.gameObject.SetActive(false);
                return;
            }
            sel.Suitable(shapes);
            FormationSelection.Families(shapes, families);
            family = sel.Current.Family;
            for (int i = 0; i < MaxFamilies; i++)
            {
                bool on = i < families.Count;
                familyButtons[i].gameObject.SetActive(on);
                if (!on) continue;
                familyButtons[i].SetText(families[i].ToUpperInvariant());
                familyButtons[i].SetLatched(families[i] == family);
            }
            int n = 0;
            foreach (FormationDefinition d in shapes)
            {
                if (d.Family != family || n >= MaxShapes) continue;
                shapeIds[n] = d.Id;
                shapeButtons[n].SetText(d.Name.ToUpperInvariant());
                shapeButtons[n].SetLatched(d.Id == sel.Current.Id);
                shapeButtons[n++].gameObject.SetActive(true);
            }
            for (int i = n; i < MaxShapes; i++)
            {
                shapeIds[i] = null;
                shapeButtons[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < spacingButtons.Length; i++) spacingButtons[i].SetLatched((int)sel.Spacing == i);
            float stack = c.Wing.Stack;
            high.SetLatched(stack > 1f);
            level.SetLatched(Mathf.Abs(stack) <= 1f);
            low.SetLatched(stack < -1f);
            buster.SetLatched(!c.Wing.AfterburnerAllowed);
            gate.SetLatched(c.Wing.AfterburnerAllowed);
        }
    }
}
