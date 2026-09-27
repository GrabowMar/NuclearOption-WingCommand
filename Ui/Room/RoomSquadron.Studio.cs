using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON's STUDIO column: the chip, APPEARANCE (the portrait and its layer steppers), IDENTITY (callsign, name, radio), BIO, and
    // SAVE / REVERT with the problem line. Fields commit to the draft they were opened for; SAVE reads them first.
    internal sealed partial class RoomSquadron
    {
        private static readonly LookLayer[] Layers = { LookLayer.Body, LookLayer.Face, LookLayer.Hair, LookLayer.Suit, LookLayer.Scene };
        private static readonly string[] LayerKeys = { "BODY", "FACE", "HAIR", "SUIT", "SCENE" };
        private static readonly string[] LayerIds = { "body", "face", "hair", "suit", "scene" };

        private Image chipRail, preview;
        private TMP_Text chip, bioCounter, problemText;
        private readonly TMP_Text[] layerValues = new TMP_Text[5];
        private TMP_Text radioValue;
        private WmcNameField callsignField, nameField, bioField;
        private AvButton saveButton, revertButton;
        private readonly AvButton[] studioButtons = new AvButton[16];
        private int studioButtonCount, studioKey = int.MinValue;

        private void BuildStudio()
        {
            float w = area.width, h = area.height, x0 = SquadronLayout.StudioX(w), c = SquadronLayout.StudioW(w);
            AvStyled.Label(body, new Rect(x0, -SquadronLayout.Pad, 200f, SquadronLayout.StudioHead), StudioWords.StudioTitle, "section-title");
            var chipRect = new Rect(x0 + c - 120f, -SquadronLayout.Pad, 120f, SquadronLayout.StudioHead);
            AvStyled.Box(body, chipRect, "chip");
            chipRail = AvStyled.Rail(body, new Rect(chipRect.x, chipRect.y, 3f, chipRect.height), "inert");
            chip = WmcKit.Text(body, new Rect(chipRect.x + 8f, chipRect.y, chipRect.width - 10f, chipRect.height), "row-sub");

            // APPEARANCE: the portrait, then a stepper per layer and RANDOM LOOK.
            float top = -SquadronLayout.SubTop, pw = SquadronLayout.PortraitW(h), ph = SquadronLayout.PortraitH(h);
            float lookW = SquadronLayout.LookW(c, h), sw = SquadronLayout.StepperW(c);
            AvStyled.Label(body, new Rect(x0, top, lookW, SquadronLayout.SectionHead), StudioWords.AppearanceTitle, "section-title");
            float sub = top - SquadronLayout.SectionHead - SquadronLayout.SectionGap;
            var frame = new Rect(x0, sub, pw, ph);
            AvKit.Panel(body, frame, AvTheme.SurfaceInert).raycastTarget = false;
            AvKit.Outline(body, frame, AvTheme.Frame);
            preview = AvKit.Panel(body, new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f), Color.white);
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            float sx = x0 + pw + 8f;
            for (int i = 0; i < Layers.Length; i++)
            {
                LookLayer layer = Layers[i];
                float y = sub - i * SquadronLayout.StepPitch;
                WmcKit.Text(body, new Rect(sx, y, 56f, SquadronLayout.StepH), "metric-key").text = LayerKeys[i];
                AvButton[] step = AvKit.Stepper(body, sx + 60f, y, sw - 60f, out layerValues[i], () => StepLook(layer, -1), () => StepLook(layer, 1),
                    "Step this layer of the portrait.");
                ids["sq.look." + LayerIds[i] + ".prev"] = step[0];
                ids["sq.look." + LayerIds[i] + ".next"] = step[1];
                Track(step[0]);
                Track(step[1]);
            }
            Track(StudioButton("sq.look.random", "RANDOM LOOK", sx, sub - Layers.Length * SquadronLayout.StepPitch, sw, RandomLook,
                "A random face, hair, uniform and scene."));

            // IDENTITY: CALLSIGN and NAME fields with RANDOM, then RADIO.
            float ix = x0 + lookW + SquadronLayout.Gap, iw = SquadronLayout.IdentityW(c, h);
            AvStyled.Label(body, new Rect(ix, top, iw, SquadronLayout.SectionHead), StudioWords.IdentityTitle, "section-title");
            float fw = iw - SquadronLayout.FieldLabel - SquadronLayout.RandomW - 8f;
            WmcKit.Text(body, new Rect(ix, sub, SquadronLayout.FieldLabel, SquadronLayout.FieldH), "metric-key").text = "CALLSIGN";
            callsignField = WmcNameField.Build(body, new Rect(ix + SquadronLayout.FieldLabel, sub, fw, SquadronLayout.FieldH), PilotText.CallsignChars,
                CommitCallsign, "The callsign the wing and the radio use: letters, digits and hyphens, 14 at most. SAVE keeps it.", "CALLSIGN");
            Track(StudioButton("sq.callsign.random", "RANDOM", ix + iw - SquadronLayout.RandomW, sub, SquadronLayout.RandomW, RandomCallsign,
                "A random callsign nobody in the squadron uses."));
            float ny = sub - SquadronLayout.FieldPitch;
            WmcKit.Text(body, new Rect(ix, ny, SquadronLayout.FieldLabel, SquadronLayout.FieldH), "metric-key").text = "NAME";
            nameField = WmcNameField.Build(body, new Rect(ix + SquadronLayout.FieldLabel, ny, fw, SquadronLayout.FieldH), PilotText.NameChars,
                CommitName, "The pilot's name, 24 characters at most. SAVE keeps it.", "NAME");
            Track(StudioButton("sq.name.random", "RANDOM", ix + iw - SquadronLayout.RandomW, ny, SquadronLayout.RandomW, RandomName, "A random name."));
            float ry = ny - SquadronLayout.FieldPitch;
            WmcKit.Text(body, new Rect(ix, ry, SquadronLayout.FieldLabel, SquadronLayout.FieldH), "metric-key").text = "RADIO";
            AvButton[] radio = AvKit.Stepper(body, ix + SquadronLayout.FieldLabel, ry, iw - SquadronLayout.FieldLabel, out radioValue,
                () => StepRadio(-1), () => StepRadio(1), "How this pilot talks on the radio.");
            ids["sq.radio.prev"] = radio[0];
            ids["sq.radio.next"] = radio[1];
            Track(radio[0]);
            Track(radio[1]);

            // BIO: its counter and GENERATE on the head row, the field below.
            float by = -SquadronLayout.BioTop(h);
            AvStyled.Label(body, new Rect(x0, by, 120f, SquadronLayout.SectionHead), StudioWords.BioTitle, "section-title");
            bioCounter = WmcKit.Text(body, new Rect(x0 + c - SquadronLayout.RandomW - 8f - 80f, by, 80f, SquadronLayout.SectionHead), "row-sub",
                TextAlignmentOptions.MidlineRight);
            Track(StudioButton("sq.bio.generate", "GENERATE", x0 + c - SquadronLayout.RandomW, by + 4f, SquadronLayout.RandomW, GenerateBio,
                "A new bio in this pilot's radio voice."));
            float fieldTop = by - SquadronLayout.SectionHead - SquadronLayout.SectionGap, bioH = SquadronLayout.BioH(h);
            bioField = WmcNameField.Build(body, new Rect(x0, fieldTop, c, bioH), PilotText.BioChars, CommitBio,
                "A few lines about the pilot, 280 characters at most. SAVE keeps it.", "BIO", multiline: true);

            // The footer: why SAVE cannot go, then SAVE and REVERT.
            float fy = fieldTop - bioH - SquadronLayout.Gap;
            problemText = WmcKit.Text(body, new Rect(x0, fy, c, 16f), "row-sub");
            float by2 = fy - 22f;
            saveButton = StudioButton("sq.save", "SAVE", x0, by2, 140f, Save, "Keep this pilot for every mission: identity, look and bio.");
            saveButton.SetText("SAVE");
            revertButton = StudioButton("sq.revert", "REVERT", x0 + 148f, by2, 120f, Revert, "Drop the edits since the last save or pick.");
        }

        private AvButton StudioButton(string id, string text, float x, float y, float w, System.Action click, string tip)
        {
            AvButton b = AvStyled.Button(body, new Rect(x, y, w, SquadronLayout.FieldH), text, "btn", click,
                id == "sq.save" ? AvButtonStyle.Primary : AvButtonStyle.Default);
            b.WithTooltip(tip);
            ids[id] = b;
            return b;
        }

        private void Track(AvButton b)
        {
            if (studioButtonCount < studioButtons.Length) studioButtons[studioButtonCount++] = b;
        }

        /// <summary>The fields take the draft's text when a new draft starts (never while typed in; never on the refresh).</summary>
        private void FillFields()
        {
            if (callsignField == null) return;
            string id = draft != null ? draftSerial.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            foreach (WmcNameField f in new[] { callsignField, nameField, bioField })
            {
                f.EditingId = id;
                f.SetInteractable(draft != null);
            }
            callsignField.SetText(draft?.Callsign ?? "");
            nameField.SetText(draft?.Name ?? "");
            bioField.SetText(draft?.Background ?? "");
            studioKey = int.MinValue;
        }

        private void RefreshStudio()
        {
            int key;
            unchecked
            {
                key = draftRevision * 31 + draftSerial * 7 + storeVersion * 131 + rosterVersion * 17 + (problem != null ? problem.GetHashCode() : 0);
            }
            if (key == studioKey) return;
            studioKey = key;
            bool has = draft != null;
            WmcKit.Set(chip, has ? StudioWords.Chip(StateOf(), out _) : WmcText.Unknown);
            WmcUi.SetRail(chipRail, has ? RailOf(StateOf()) : "inert");
            preview.sprite = has ? PilotPortrait.Preview(draft.Selection) : null;
            preview.enabled = preview.sprite != null;
            PortraitSelection s = has ? draft.Selection : PilotPortraitGenerator.DefaultSelection;
            WmcKit.Set(layerValues[0], PilotPortraitGenerator.BodyLabel(s.Body));
            WmcKit.Set(layerValues[1], StudioWords.Face(s.Face));
            WmcKit.Set(layerValues[2], StudioWords.Hair(s.Hair));
            WmcKit.Set(layerValues[3], PilotPortraitGenerator.UniformLabel(s.Uniform));
            WmcKit.Set(layerValues[4], StudioWords.Scene(s.Backdrop));
            WmcKit.Set(radioValue, has ? StudioWords.Radio(draft.Persona) : WmcText.Unknown);
            WmcKit.Set(bioCounter, StudioWords.BioCounter(has ? (draft.Background ?? "").Length : 0));
            for (int i = 0; i < studioButtonCount; i++) studioButtons[i].SetEnabled(has);
            DraftState state = has ? StateOf() : DraftState.Saved;
            bool canSave = has && state != DraftState.Saved;
            saveButton.SetEnabled(canSave);
            saveButton.WithTooltip(!has ? StudioWords.NoPilot : state == DraftState.Saved ? "Saved: nothing to save."
                : "Keep this pilot for every mission: identity, look and bio.");
            revertButton.SetEnabled(has && (state == DraftState.Edited || (draftNew && touched)));
            WmcKit.Set(problemText, problem ?? (!has ? StudioWords.NoPilot : state == DraftState.Edited ? "EDITED · SAVE keeps it, REVERT drops it" : ""));
            problemText.color = problem != null ? WmcUi.LevelColor("warn") : AvTheme.Dim;
        }

        private static string RailOf(DraftState s)
        {
            StudioWords.Chip(s, out string rail);
            return rail;
        }

        // ---------------------------------------------------------------- edits

        private bool IsCurrent(string id) =>
            draft != null && id == draftSerial.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void CommitCallsign(string id, string text)
        {
            if (!IsCurrent(id)) return;
            string callsign = PilotText.Callsign(text);
            if (callsign.Length == 0 || callsign == draft.Callsign)
            {
                callsignField.SetText(draft.Callsign);
                return;
            }
            draft.DialogueTag = PilotStudio.TagAfterRename(draft.Callsign, callsign, draft.DialogueTag);
            draft.Callsign = callsign;
            callsignField.SetText(callsign);
            Edited();
        }

        private void CommitName(string id, string text)
        {
            if (!IsCurrent(id)) return;
            string name = PilotText.Name(text);
            if (name.Length == 0 || name == draft.Name)
            {
                nameField.SetText(draft.Name);
                return;
            }
            draft.Name = name;
            nameField.SetText(name);
            Edited();
        }

        private void CommitBio(string id, string text)
        {
            if (!IsCurrent(id)) return;
            string bio = PilotText.Bio(text);
            if (bio == (draft.Background ?? "")) return;
            draft.Background = bio;
            bioField.SetText(bio);
            Edited();
        }

        /// <summary>SAVE reads the fields first, so a click straight from a field keeps what was typed (0.9's rule).</summary>
        private void ReadFields()
        {
            if (draft == null || callsignField == null) return;
            string id = draftSerial.ToString(System.Globalization.CultureInfo.InvariantCulture);
            CommitCallsign(id, callsignField.Text);
            CommitName(id, nameField.Text);
            CommitBio(id, bioField.Text);
        }

        private void StepLook(LookLayer layer, int dir)
        {
            if (draft == null) return;
            draft.ApplySelection(PilotStudio.Step(draft.Selection, layer, dir));
            Edited();
        }

        private void RandomLook()
        {
            if (draft == null) return;
            draft.ApplySelection(PilotStudio.RandomLook(rng.Next));
            Edited();
        }

        private void StepRadio(int dir)
        {
            if (draft == null) return;
            draft.Persona = (ChatterPersona)((((int)draft.Persona + dir) % 4 + 4) % 4);
            Edited();
        }

        private void RandomCallsign()
        {
            if (draft == null) return;
            ReadFields();
            string callsign = PilotText.Callsign(PilotIdentity.Callsign(rng.Next, c => Taken(PilotText.Callsign(c))));
            draft.DialogueTag = PilotStudio.TagAfterRename(draft.Callsign, callsign, draft.DialogueTag);
            draft.Callsign = callsign;
            callsignField.SetText(callsign);
            Edited();
        }

        private void RandomName()
        {
            if (draft == null) return;
            ReadFields();
            draft.Name = PilotText.Name(PilotIdentity.Name(rng.Next));
            nameField.SetText(draft.Name);
            Edited();
        }

        private void GenerateBio()
        {
            if (draft == null) return;
            ReadFields();
            draft.Background = PilotText.Bio(PilotIdentity.Background(rng.Next, draft.Persona));
            bioField.SetText(draft.Background);
            Edited();
        }

        private void Save()
        {
            if (draft == null) return;
            ReadFields();
            WmcNameField.BlurAny();
            string callsign = draft.Callsign;
            if (!WingSavedPilots.Save(draft, draftNew ? null : draftOriginal, client || draftNew ? null : draftLive, out string why))
            {
                problem = why;
                WmcRoom.Instance?.RefreshNow();
                return;
            }
            WingToast.Show(StudioWords.Saved(callsign));
            draftNew = false;
            selected = callsign;
            storeVersion = int.MinValue;
            Scan(last);
            Entry e = Find(callsign);
            if (e != null) Select(e);
            WmcRoom.Instance?.RefreshNow();
        }

        private void Revert()
        {
            if (draft == null) return;
            WmcNameField.BlurAny();
            Entry e = draftNew ? null : Find(selected);
            if (e != null) Select(e);
            else if (draftStart != null)
            {
                draft = draftStart.Clone();
                NewDraft();
            }
            WmcRoom.Instance?.RefreshNow();
        }
    }
}
