using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON › STUDIO's view on the bezel (spec bezel v2 §5 SQUADRON › STUDIO): one scroll — the pilot picker with ‹ › and the draft's
    // chip; NEW · CLONE · DELETE and IMPORT · EXPORT · FOLDER; the portrait beside its layer steppers and RANDOM LOOK; CALLSIGN and NAME
    // with RANDOM, RADIO; BIO with its counter and GENERATE; the problem line; SAVE · REVERT and RECRUIT / DISCHARGE; the SERVICE line.
    internal sealed partial class WmcStudio
    {
        private static readonly LookLayer[] Layers = { LookLayer.Body, LookLayer.Face, LookLayer.Hair, LookLayer.Suit, LookLayer.Scene };
        private static readonly string[] LayerKeys = { "BODY", "FACE", "HAIR", "SUIT", "SCENE" };
        private static readonly string[] LayerIds = { "body", "face", "hair", "suit", "scene" };
        private const float Row = 24f, Field = 26f, StepPitch = 32f, PortraitW = 96f, PortraitH = 144f, KeyW = 64f, RandomW = 76f, BioH = 64f;
        private const float Foot = 4f + 22f + Field + 6f + 20f;

        private WmcScroll scroll;
        private RectTransform view;
        private Image chipRail, preview;
        private TMP_Text chip, bioCounter, problemText, service;
        private readonly TMP_Text[] layerValues = new TMP_Text[5];
        private TMP_Text radioValue;
        private WmcNameField callsignField, nameField, bioField;
        private AvButton picker, prevPilot, nextPilot, saveButton, revertButton, cloneButton, deleteButton, recruitButton;
        private readonly AvButton[] studioButtons = new AvButton[16];
        private readonly List<AvKit.PopupEntry> pickEntries = new List<AvKit.PopupEntry>(16);
        private readonly ConfirmGate deleteGate = new ConfirmGate(), dischargeGate = new ConfirmGate();
        private AvKit.Popup pickPopup;
        private int studioButtonCount, studioKey = int.MinValue, pickerKey = int.MinValue, recordKey = int.MinValue;
        private string recruitReason;

        /// <summary>The studio's scroll viewport (the text-fit audit reads it).</summary>
        public RectTransform View => view;

        private void BuildView()
        {
            scroll = WmcScroll.Build(root, new Rect(left, top, width + 8f, height), "StudioScroll");
            view = scroll.Content;
            RectTransform s = view;
            float w = scroll.Width, y = 0f;

            // The pilot picker: ‹ NAME › and the draft's chip.
            AvStyled.Label(s, new Rect(0f, y, 48f, Row), "PILOT", "metric-key");
            prevPilot = AvStyled.Button(s, new Rect(48f, y, 26f, Row), "‹", "btn", () => Step(-1), AvButtonStyle.Quiet);
            picker = AvStyled.Button(s, new Rect(78f, y, w - 78f - 30f - 112f, Row), "", "btn", OpenPicker);
            nextPilot = AvStyled.Button(s, new Rect(w - 30f - 108f, y, 26f, Row), "›", "btn", () => Step(1), AvButtonStyle.Quiet);
            prevPilot.WithTooltip("The previous pilot.");
            nextPilot.WithTooltip("The next pilot.");
            picker.WithTooltip("Pick a pilot: saved pilots first, then this mission's unsaved ones.");
            ids["sq.pick.prev"] = prevPilot;
            ids["sq.pick"] = picker;
            ids["sq.pick.next"] = nextPilot;
            var chipRect = new Rect(w - 104f, y, 104f, Row);
            AvStyled.Box(s, chipRect, "chip");
            chipRail = AvStyled.Rail(s, new Rect(chipRect.x, chipRect.y, 3f, chipRect.height), "inert");
            chip = WmcKit.Text(s, new Rect(chipRect.x + 8f, chipRect.y, chipRect.width - 10f, chipRect.height), "row-sub");
            y -= Row + 4f;

            float bw = (w - 5f * WmcUi.Gap) / 6f;
            Button(s, "sq.new", "NEW", 0f, y, bw, NewPilot, "A new pilot as a draft: a random identity, look and bio. Nothing is saved until SAVE.");
            cloneButton = Button(s, "sq.clone", "CLONE", bw + WmcUi.Gap, y, bw, Clone, "Copy this pilot into a new draft under the next free callsign.");
            deleteButton = Button(s, "sq.delete", "DELETE", 2f * (bw + WmcUi.Gap), y, bw, Delete,
                "Delete this saved pilot (press twice). This mission keeps them.");
            Button(s, "sq.import", "IMPORT", 3f * (bw + WmcUi.Gap), y, bw, Import,
                "Add pilots from the Pilots folder and 0.9's folder: new callsigns only, the files are left as they are.");
            Button(s, "sq.export", "EXPORT", 4f * (bw + WmcUi.Gap), y, bw, Export, "Write every saved pilot to Pilots/exported_pilots.json.");
            Button(s, "sq.folder", "FOLDER", 5f * (bw + WmcUi.Gap), y, bw, () => WingSavedPilots.OpenFolder(), "Open the Pilots folder.");
            y -= Row + 6f;

            // The portrait beside its layer steppers and RANDOM LOOK.
            var frame = new Rect(0f, y, PortraitW, PortraitH);
            AvKit.Panel(s, frame, AvTheme.SurfaceInert).raycastTarget = false;
            AvKit.Outline(s, frame, AvTheme.Frame);
            preview = AvKit.Panel(s, new Rect(frame.x + 1f, frame.y - 1f, frame.width - 2f, frame.height - 2f), Color.white);
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            float sx = PortraitW + 8f, sw = w - sx;
            for (int i = 0; i < Layers.Length; i++)
            {
                LookLayer layer = Layers[i];
                float ly = y - i * StepPitch;
                WmcKit.Text(s, new Rect(sx, ly, 48f, 28f), "metric-key").text = LayerKeys[i];
                AvButton[] step = AvKit.Stepper(s, sx + 50f, ly, sw - 50f, out layerValues[i], () => StepLook(layer, -1), () => StepLook(layer, 1),
                    "Step this layer of the portrait.");
                ids["sq.look." + LayerIds[i] + ".prev"] = step[0];
                ids["sq.look." + LayerIds[i] + ".next"] = step[1];
                Track(step[0]);
                Track(step[1]);
            }
            Track(Button(s, "sq.look.random", "RANDOM LOOK", sx, y - Layers.Length * StepPitch, sw, RandomLook, "A random face, hair, uniform and scene."));
            y -= Layers.Length * StepPitch + Row + 8f;

            // CALLSIGN and NAME with RANDOM, then RADIO.
            float fw = w - KeyW - RandomW - 6f;
            WmcKit.Text(s, new Rect(0f, y, KeyW, Field), "metric-key").text = "CALLSIGN";
            callsignField = WmcNameField.Build(s, new Rect(KeyW, y, fw, Field), PilotText.CallsignChars, CommitCallsign,
                "The callsign the wing and the radio use: letters, digits and hyphens, 14 at most. SAVE keeps it.", "CALLSIGN");
            Track(Button(s, "sq.callsign.random", "RANDOM", w - RandomW, y, RandomW, RandomCallsign, "A random callsign nobody in the squadron uses."));
            y -= Field + 4f;
            WmcKit.Text(s, new Rect(0f, y, KeyW, Field), "metric-key").text = "NAME";
            nameField = WmcNameField.Build(s, new Rect(KeyW, y, fw, Field), PilotText.NameChars, CommitName,
                "The pilot's name, 24 characters at most. SAVE keeps it.", "NAME");
            Track(Button(s, "sq.name.random", "RANDOM", w - RandomW, y, RandomW, RandomName, "A random name."));
            y -= Field + 4f;
            WmcKit.Text(s, new Rect(0f, y, KeyW, 28f), "metric-key").text = "RADIO";
            AvButton[] radio = AvKit.Stepper(s, KeyW, y, w - KeyW, out radioValue, () => StepRadio(-1), () => StepRadio(1), "How this pilot talks on the radio.");
            ids["sq.radio.prev"] = radio[0];
            ids["sq.radio.next"] = radio[1];
            Track(radio[0]);
            Track(radio[1]);
            y -= StepPitch + 4f;

            // BIO: the counter and GENERATE on its head, the field below.
            AvStyled.Label(s, new Rect(0f, y, 120f, 20f), StudioWords.BioTitle, "section-title");
            bioCounter = WmcKit.Text(s, new Rect(w - RandomW - 90f, y, 84f, 20f), "row-sub", TextAlignmentOptions.MidlineRight);
            Track(Button(s, "sq.bio.generate", "GENERATE", w - RandomW, y, RandomW, GenerateBio, "A new bio in this pilot's radio voice."));
            y -= 24f;
            bioField = WmcNameField.Build(s, new Rect(0f, y, w, BioH), PilotText.BioChars, CommitBio,
                "A few lines about the pilot, 280 characters at most. SAVE keeps it.", "BIO", multiline: true);
            y -= BioH + 6f;
            float content = -y;
            scroll.SetContentHeight(content);

            // Why SAVE cannot go; SAVE · REVERT and RECRUIT / DISCHARGE; SERVICE — under the fields, pinned on a short dock (review
            // U3-U4: at H 596 they sat below the fold).
            var go = new GameObject("StudioFoot", typeof(RectTransform));
            var foot = (RectTransform)go.transform;
            foot.SetParent(root, false);
            AvKit.Place(foot, new Rect(left, top - content, w, Foot));
            float fy = -4f;
            problemText = WmcKit.Text(foot, new Rect(0f, fy, w, 18f), "row-sub");
            fy -= 22f;
            float third = (w - 2f * WmcUi.Gap) / 3f;
            saveButton = Button(foot, "sq.save", "SAVE", 0f, fy, third, Save, "Keep this pilot for every mission: identity, look and bio.", AvButtonStyle.Primary);
            revertButton = Button(foot, "sq.revert", "REVERT", third + WmcUi.Gap, fy, third, Revert, "Drop the edits since the last save or pick.");
            recruitButton = Button(foot, "sq.recruit", "RECRUIT", 2f * (third + WmcUi.Gap), fy, third, RecruitOrDischarge, StudioWords.RecruitTip);
            fy -= Field + 6f;
            service = WmcKit.Text(foot, new Rect(0f, fy, w, 18f), "row-sub");
            new WmcFooter(scroll, foot, new Rect(left, top, width + 8f, height - Foot)).Fit(content);
            pickPopup = new AvKit.Popup(popupParent, panelWidth);
        }

        private AvButton Button(RectTransform s, string id, string text, float x, float y, float w, System.Action click, string tip,
            AvButtonStyle style = AvButtonStyle.Default)
        {
            AvButton b = AvStyled.Button(s, new Rect(x, y, w, Row), text, "btn", click, style);
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

        // ---------------------------------------------------------------- the picker

        private void OpenPicker()
        {
            if (entries.Count == 0) return;
            int at = IndexOf(selected);
            pickEntries.Clear();
            foreach (Entry e in entries)
            {
                int n = 0;
                PilotStatus s = e.Live != null ? WmcPilots.StatusOf(e.Live, wing, out n) : PilotStatus.Free;
                string detail = (e.Saved != null ? "SAVED" : "NOT SAVED") + " · " + (client ? "" : e.Live != null ? SquadronWords.Row(s, false, n) : "NOT IN MISSION");
                pickEntries.Add(new AvKit.PopupEntry(WmcText.Cut(e.Callsign, PilotText.CallsignChars), detail, pickEntries.Count == at));
            }
            pickPopup.Show(WmcKit.RectIn(popupParent, (RectTransform)picker.transform), pickEntries, Open);
            WmcKit.FitAll(popupParent);
        }

        /// <summary>‹ and ›: the next pilot in the picker's order (asking before dropping edits, as a pick does).</summary>
        private void Step(int dir)
        {
            if (entries.Count == 0) return;
            int at = draftNew ? -1 : IndexOf(selected);
            int next = at < 0 ? (dir > 0 ? 0 : entries.Count - 1) : ((at + dir) % entries.Count + entries.Count) % entries.Count;
            Open(next);
        }

        private void RefreshPicker()
        {
            int at = IndexOf(selected);
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + at * 131 + (client ? 3 : 0) + (draftNew ? 5 : 0) + draftRevision * 7 + entries.Count * 1009;
            }
            if (key == pickerKey) return;
            pickerKey = key;
            string name = draft == null ? (entries.Count == 0 ? (WingSavedPilots.Problem != null ? "SAVED PILOTS UNREADABLE" : "NO PILOTS · NEW STARTS ONE") : "PICK A PILOT")
                : WmcText.Cut(draft.Callsign, PilotText.CallsignChars);
            picker.SetText(name + " ›");
            picker.WithTooltip(WingSavedPilots.Problem ?? "Pick a pilot: saved pilots first, then this mission's unsaved ones.");
            bool several = entries.Count > 1 || (draftNew && entries.Count > 0);
            prevPilot.SetEnabled(several);
            nextPilot.SetEnabled(several);
            picker.SetEnabled(entries.Count > 0);
            bool saved = !draftNew && draftOriginal != null;
            cloneButton.SetEnabled(draft != null);
            deleteButton.SetEnabled(saved);
            deleteButton.SetText(saved && deleteGate.IsArmed(draftOriginal, Time.unscaledTime) ? "DELETE?" : "DELETE");
            deleteButton.WithTooltip(saved ? "Delete this saved pilot (press twice). This mission keeps them." : "Only a saved pilot can be deleted.");
        }

        // ---------------------------------------------------------------- the studio and the record

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
            PortraitSelection sel = has ? draft.Selection : PilotPortraitGenerator.DefaultSelection;
            WmcKit.Set(layerValues[0], PilotPortraitGenerator.BodyLabel(sel.Body));
            WmcKit.Set(layerValues[1], StudioWords.Face(sel.Face));
            WmcKit.Set(layerValues[2], StudioWords.Hair(sel.Hair));
            WmcKit.Set(layerValues[3], PilotPortraitGenerator.UniformLabel(sel.Uniform));
            WmcKit.Set(layerValues[4], StudioWords.Scene(sel.Backdrop));
            WmcKit.Set(radioValue, has ? StudioWords.Radio(draft.Persona) : WmcText.Unknown);
            WmcKit.Set(bioCounter, StudioWords.BioCounter(has ? (draft.Background ?? "").Length : 0));
            for (int i = 0; i < studioButtonCount; i++) studioButtons[i].SetEnabled(has);
            DraftState state = has ? StateOf() : DraftState.Saved;
            saveButton.SetEnabled(has && state != DraftState.Saved);
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

        private void RefreshRecord()
        {
            CustomPilotRecord stored = !draftNew && draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
            WingPilot live = draftNew || client ? null : draftLive;
            int n = 0;
            PilotStatus s = live != null ? WmcPilots.StatusOf(live, wing, out n) : PilotStatus.Free;
            bool asking = live != null && dischargeGate.IsArmed(live.Callsign, Time.unscaledTime);
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + draftSerial * 7 + (int)s * 131 + n * 1009 + (asking ? 3 : 0) + (client ? 5 : 0)
                    + (draftNew ? 11 : 0) + (stored != null ? 13 : 0);
            }
            if (key == recordKey) return;
            recordKey = key;
            // RECRUIT a saved pilot not in this mission; DISCHARGE a free pilot of this mission (press twice).
            recruitReason = client ? StudioWords.ClientRecord
                : draft == null ? StudioWords.NoPilot
                : live == null && stored == null ? "Save the pilot first: RECRUIT adds a saved pilot to this mission."
                : live != null ? StudioWords.DischargeWhy(s) : null;
            recruitButton.SetText(StudioWords.RecruitLabel(live != null, asking));
            recruitButton.SetLatched(asking);
            recruitButton.SetEnabled(recruitReason == null);
            recruitButton.WithTooltip(recruitReason ?? (live != null ? StudioWords.DischargeTip : StudioWords.RecruitTip));
            WmcKit.Set(service, stored != null
                ? StudioWords.Service(stored.Missions, stored.Sorties, stored.Kills) + " · " + StudioWords.BestRank(stored.Missions, stored.Xp)
                : draft == null ? "" : live != null ? SquadronWords.Row(s, ReferenceEquals(live, WingPilotRoster.Upcoming), n) + " · " + StudioWords.NotSaved
                : StudioWords.NotSaved);
        }
    }
}
