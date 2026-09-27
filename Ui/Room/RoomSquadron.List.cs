using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON's PILOTS column: saved pilots first (catalog order), then this mission's unsaved ones; a pager; NEW · CLONE · DELETE and
    // IMPORT · EXPORT · FOLDER.
    internal sealed partial class RoomSquadron
    {
        private sealed class RowView
        {
            public GameObject Root;
            public Image Fill, Rail, Thumb;
            public TMP_Text Callsign, Name, Saved, Mission;
            public AvButton Hit;
            public int Entry = -1;
            public bool Selected, Styled;
            public string ThumbFor;
            public int ThumbLook = int.MinValue;
        }

        private readonly RowView[] rows = new RowView[SquadronLayout.MaxRows];
        private TMP_Text listNote, emptyText;
        private GameObject emptyRoot;
        private WmcPager listPager;
        private AvButton cloneButton, deleteButton;
        private readonly ConfirmGate deleteGate = new ConfirmGate();
        private int rowsPerPage, listPage, listKey = int.MinValue;

        private void BuildList()
        {
            float x = SquadronLayout.ListX, w = SquadronLayout.ListW(area.width), h = area.height;
            AvStyled.Label(body, new Rect(x, -SquadronLayout.Pad, 120f, SquadronLayout.Head), StudioWords.Title, "section-title");
            listNote = WmcKit.Text(body, new Rect(x + 120f, -SquadronLayout.Pad, w - 120f, SquadronLayout.Head), "section-title-note",
                TextAlignmentOptions.MidlineRight);
            rowsPerPage = SquadronLayout.ListRows(h);
            for (int i = 0; i < rows.Length; i++) rows[i] = i < rowsPerPage ? BuildRow(i, x, w) : null;

            var go = new GameObject("SquadronEmpty", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(body, false);
            AvKit.Place(rt, new Rect(x, -SquadronLayout.RowsTop, w, 56f));
            AvStyled.Box(rt, new Rect(0f, 0f, w, 56f), "card", "inert");
            emptyText = AvStyled.Label(rt, new Rect(10f, -6f, w - 20f, 44f), "", "row-sub");
            emptyRoot = go;
            emptyRoot.SetActive(false);

            float foot = -(h - SquadronLayout.Pad - SquadronLayout.ListFooter);
            listPager = WmcPager.Build(body, new Rect(x, foot, w, SquadronLayout.Pager), "sq.list.", ids, TurnList);
            float y1 = foot - SquadronLayout.Pager - SquadronLayout.FooterGap, y2 = y1 - SquadronLayout.FooterRow - SquadronLayout.FooterGap;
            float bw = (w - 2f * WmcUi.Gap) / 3f;
            FooterButton("sq.new", "NEW", x, y1, bw, NewPilot, "A new pilot as a draft: a random identity, look and bio. Nothing is saved until SAVE.");
            cloneButton = FooterButton("sq.clone", "CLONE", x + bw + WmcUi.Gap, y1, bw, Clone, "Copy this pilot into a new draft under the next free callsign.");
            deleteButton = FooterButton("sq.delete", "DELETE", x + 2f * (bw + WmcUi.Gap), y1, bw, Delete,
                "Delete this saved pilot (press twice). This mission keeps them.");
            FooterButton("sq.import", "IMPORT", x, y2, bw, Import,
                "Add pilots from the Pilots folder and 0.9's folder: new callsigns only, the files are left as they are.");
            FooterButton("sq.export", "EXPORT", x + bw + WmcUi.Gap, y2, bw, Export, "Write every saved pilot to Pilots/exported_pilots.json.");
            FooterButton("sq.folder", "FOLDER", x + 2f * (bw + WmcUi.Gap), y2, bw, () => WingSavedPilots.OpenFolder(), "Open the Pilots folder.");
        }

        private AvButton FooterButton(string id, string text, float x, float y, float w, System.Action click, string tip)
        {
            AvButton b = AvStyled.Button(body, new Rect(x, y, w, SquadronLayout.FooterRow), text, "btn", click);
            b.WithTooltip(tip);
            ids[id] = b;
            return b;
        }

        private RowView BuildRow(int index, float x, float w)
        {
            const float h = SquadronLayout.RowH;
            var go = new GameObject("SquadronRow" + index, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(body, false);
            AvKit.Place(rt, new Rect(x, -SquadronLayout.RowsTop - index * SquadronLayout.RowPitch, w, h));
            var v = new RowView { Root = go };
            v.Fill = AvStyled.Box(rt, new Rect(0f, 0f, w, h), "row");
            if (v.Fill != null) v.Fill.raycastTarget = false;
            v.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, h), "inert");
            v.Hit = AvKit.HitButton(rt, new Rect(0f, 0f, w, h), () => PressRow(index));
            v.Hit.WithTooltip("Open this pilot in the studio.");
            ids["sq.list.row" + index] = v.Hit;
            v.Thumb = AvKit.Panel(rt, new Rect(7f, 0f, 26f, 39f), Color.white);
            v.Thumb.preserveAspect = true;
            v.Thumb.raycastTarget = false;
            float text = w - 40f - 118f;
            v.Callsign = WmcKit.Text(rt, new Rect(40f, -2f, text, 18f), "row-name");
            v.Name = WmcKit.Text(rt, new Rect(40f, -20f, text, 16f), "row-sub");
            v.Saved = WmcKit.Text(rt, new Rect(w - 116f, -2f, 110f, 18f), "row-sub", TextAlignmentOptions.MidlineRight);
            v.Mission = WmcKit.Text(rt, new Rect(w - 116f, -20f, 110f, 16f), "row-value", TextAlignmentOptions.MidlineRight);
            go.SetActive(false);
            return v;
        }

        private void RefreshList()
        {
            int at = IndexOf(selected);
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + listPage * 7 + at * 131 + (client ? 3 : 0) + WingPilotRoster.LookVersion * 1009
                    + (draftNew ? 5 : 0);
            }
            if (key == listKey) return;
            listKey = key;
            listPage = Pages.Clamp(listPage, entries.Count, rowsPerPage);
            WmcKit.Set(listNote, StudioWords.ListHead(savedCount, client ? 0 : entries.Count - savedCount));
            emptyRoot.SetActive(entries.Count == 0);
            WmcKit.Set(emptyText, StudioWords.EmptyList(0));
            int first = Pages.First(listPage, rowsPerPage);
            for (int i = 0; i < rowsPerPage; i++)
            {
                RowView v = rows[i];
                int e = first + i;
                bool on = e < entries.Count;
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                v.Entry = on ? e : -1;
                if (!on) continue;
                Entry entry = entries[e];
                WingPilot live = entry.Live;
                WmcKit.Set(v.Callsign, WmcText.Cut(entry.Callsign, PilotText.CallsignChars));
                WmcKit.Set(v.Name, WmcText.Cut(entry.Saved != null ? entry.Saved.Name : live.Name, PilotText.NameChars));
                WmcKit.Set(v.Saved, entry.Saved != null ? "SAVED" : "NOT SAVED");
                int n = 0;
                PilotStatus s = live != null ? WmcPilots.StatusOf(live, wing, out n) : PilotStatus.Free;
                WmcKit.Set(v.Mission, client ? "" : live != null ? SquadronWords.Row(s, false, n) : "NOT IN MISSION");
                v.Mission.color = live != null ? WmcUi.LevelColor(SquadronWords.Level(s)) : AvTheme.Dim;
                WmcUi.SetRail(v.Rail, entry.Saved != null ? "info" : "inert");
                SetThumb(v, entry);
                bool sel = !draftNew && e == at;
                if (!v.Styled || v.Selected != sel)
                {
                    v.Styled = true;
                    v.Selected = sel;
                    if (v.Fill != null) v.Hit.SetRowHighlight(v.Fill, WmcUi.RowColor(WmcStyle.RowRest(sel)), WmcUi.RowColor(WmcStyle.RowHover(sel)));
                }
            }
            listPager.Set(listPage, Pages.Count(entries.Count, rowsPerPage));
            bool saved = !draftNew && draftOriginal != null;
            cloneButton.SetEnabled(draft != null);
            deleteButton.SetEnabled(saved);
            deleteButton.SetText(saved && deleteGate.IsArmed(draftOriginal, Time.unscaledTime) ? "DELETE?" : "DELETE");
            deleteButton.WithTooltip(saved ? "Delete this saved pilot (press twice). This mission keeps them." : "Only a saved pilot can be deleted.");
        }

        /// <summary>The row's face, looked up only when its pilot or the look version changed.</summary>
        private static void SetThumb(RowView v, Entry e)
        {
            if (v.ThumbFor == e.Callsign && v.ThumbLook == WingPilotRoster.LookVersion) return;
            v.ThumbFor = e.Callsign;
            v.ThumbLook = WingPilotRoster.LookVersion;
            v.Thumb.sprite = e.Saved != null ? PilotPortrait.ForSelection(e.Saved.Selection) : PilotPortrait.For(e.Live);
            v.Thumb.enabled = v.Thumb.sprite != null;
        }

        /// <summary>A row opens its pilot; with unsaved edits the first press asks, the second drops them.</summary>
        private void PressRow(int index)
        {
            RowView v = index >= 0 && index < rows.Length ? rows[index] : null;
            if (v == null || v.Entry < 0 || v.Entry >= entries.Count) return;
            Entry e = entries[v.Entry];
            if (!draftNew && string.Equals(e.Callsign, selected, System.StringComparison.OrdinalIgnoreCase)) return;
            if ((StateOf() == DraftState.Edited || (draftNew && touched)) && !rowGate.Press(e.Callsign, Time.unscaledTime))
            {
                WingToast.Show(StudioWords.UnsavedAsk);
                return;
            }
            WmcNameField.BlurAny();
            Select(e);
            WmcRoom.Instance?.RefreshNow();
        }

        private void TurnList(int dir)
        {
            listPage = Pages.Turn(listPage, dir, entries.Count, rowsPerPage);
            listKey = int.MinValue;
            WmcRoom.Instance?.RefreshNow();
        }

        private void NewPilot()
        {
            WmcNameField.BlurAny();
            draft = PilotStudio.Generate(rng.Next, Taken);
            draftStart = draft.Clone();
            selected = null;
            draftOriginal = null;
            draftLive = null;
            draftNew = true;
            NewDraft();
            WmcRoom.Instance?.RefreshNow();
        }

        private void Clone()
        {
            if (draft == null) return;
            ReadFields();
            WmcNameField.BlurAny();
            string callsign = WingSavedPilots.Store.CloneName(draft.Callsign, WingPilotRoster.ContainsCallsign);
            CustomPilotRecord copy = draft.Clone(callsign);
            copy.DialogueTag = PilotStudio.TagAfterRename(draft.Callsign, callsign, draft.DialogueTag);
            draft = copy;
            draftStart = copy.Clone();
            selected = null;
            draftOriginal = null;
            draftLive = null;
            draftNew = true;
            NewDraft();
            WmcRoom.Instance?.RefreshNow();
        }

        private void Delete()
        {
            if (draftNew || draftOriginal == null) return;
            string callsign = draftOriginal;
            if (!deleteGate.Press(callsign, Time.unscaledTime))
            {
                WingToast.Show(StudioWords.DeleteAsk(callsign));
                listKey = int.MinValue;
                WmcRoom.Instance?.RefreshNow();
                return;
            }
            if (!WingSavedPilots.Delete(callsign, out string why)) problem = why;
            else WingToast.Show("Deleted " + callsign + " from the saved pilots");
            WmcRoom.Instance?.RefreshNow();
        }

        private void Import()
        {
            WingSavedPilots.Import(out int added, out int skipped, out int files);
            WingToast.Show(StudioWords.ImportToast(added, skipped, files));
            WmcRoom.Instance?.RefreshNow();
        }

        private void Export()
        {
            bool ok = WingSavedPilots.Export(out int n);
            WingToast.Show(ok ? StudioWords.ExportToast(n) : "EXPORT failed: see the log");
        }
    }
}
