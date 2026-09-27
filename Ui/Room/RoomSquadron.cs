using System.Collections.Generic;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    /// <summary>SQUADRON (spec WMC rebuild §Big panel; research squadron-studio): the PILOTS list (saved pilots, then this mission's
    /// unsaved ones), the STUDIO that edits one pilot as a draft (identity, radio, look, bio; nothing is written until SAVE, and a
    /// rename saves in place), and the RECORD (the lifetime SERVICE record, THIS MISSION's numbers, perks, RECRUIT / DISCHARGE). Saved
    /// pilots join every mission as ROOKIEs (the 2026-09-25 decision). The draft lives in the page, so a re-layout rebuilds the widgets
    /// and refills them; a field that has the keyboard lets go before the room gives it back.</summary>
    internal sealed partial class RoomSquadron : IRoomPage
    {
        private sealed class Entry
        {
            public CustomPilotRecord Saved;
            public WingPilot Live;
            public string Callsign;
        }

        private readonly Dictionary<string, AvButton> ids = new Dictionary<string, AvButton>();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<WingPilot> roster = new List<WingPilot>();
        private readonly System.Random rng = new System.Random();
        private RectTransform body;
        private Rect area;
        private bool built;

        private WmcContext last;
        private WingService wing;
        private bool client, scanClient;
        private int storeVersion = int.MinValue, rosterVersion = int.MinValue, savedCount;

        // The draft: what the studio edits, where it came from, and its place among the rows.
        private string selected;
        private CustomPilotRecord draft, draftStart;
        private string draftOriginal;
        private WingPilot draftLive;
        private bool draftNew, touched, cloned;
        private int draftSerial, draftRevision;
        private string problem;
        private WingPilot focusRequest;
        private readonly ConfirmGate rowGate = new ConfirmGate();

        public IReadOnlyDictionary<string, AvButton> Controls => ids;

        public string Hint => StudioWords.Hint;

        /// <summary>The pilot the studio edits (automation reads it).</summary>
        public CustomPilotRecord Draft => draft;

        public DraftState State => StateOf();

        public void Build(RectTransform pageBody, Rect pageArea)
        {
            body = pageBody;
            area = pageArea;
            ids.Clear();
            // Review R7: the page is rebuilt every mission and on a re-layout; the studio's buttons are this build's.
            studioButtonCount = 0;
            System.Array.Clear(studioButtons, 0, studioButtons.Length);
            BuildList();
            BuildStudio();
            BuildRecord();
            WmcKit.FitAll(body);
            built = true;
            listKey = studioKey = recordKey = int.MinValue;
            FillFields();
        }

        public void Show(WmcContext c) => Refresh(c);

        public void Hide()
        {
            // A field lets go before the room gives the keyboard back (critic §5 R7 2: the toolkit's guard must release first).
            WmcNameField.BlurAny();
            AvKit.Popup.CloseAny();
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            Scan(c);
            if (focusRequest != null)
            {
                Entry e = Find(focusRequest.Callsign);
                focusRequest = null;
                // Review R7: STUDIO › asks before dropping unsaved edits, as a row press does (a second STUDIO › or the row drops them).
                bool same = e != null && !draftNew && string.Equals(e.Callsign, selected, System.StringComparison.OrdinalIgnoreCase);
                if (e != null && !same)
                {
                    if (Dirty && !rowGate.Press(e.Callsign, Time.unscaledTime)) WingToast.Show(StudioWords.UnsavedAsk);
                    else Select(e);
                }
            }
            if (!built) return;
            RefreshList();
            RefreshStudio();
            RefreshRecord();
        }

        public void Tick(WmcContext c)
        {
        }

        /// <summary>WING's STUDIO ›: the room opens on this pilot.</summary>
        public void Focus(WingPilot p) => focusRequest = p;

        // ---------------------------------------------------------------- the rows' pilots

        /// <summary>Saved pilots in store order (each with its live copy this mission), then this mission's unsaved pilots; rebuilt only
        /// when the store or the roster moved. A client lists its own saved pilots only.</summary>
        private void Scan(WmcContext c)
        {
            client = c != null && c.Client;
            wing = client || c == null ? null : c.Wing;
            SavedPilotStore store = WingSavedPilots.Store;
            if (store.Version == storeVersion && WingPilotRoster.Version == rosterVersion && client == scanClient) return;
            bool sourceMoved = storeVersion != int.MinValue;
            storeVersion = store.Version;
            rosterVersion = WingPilotRoster.Version;
            scanClient = client;
            roster.Clear();
            if (!client) WingPilotRoster.Roster(roster);
            entries.Clear();
            foreach (CustomPilotRecord r in store.Records)
                entries.Add(new Entry { Saved = r, Live = client ? null : WingPilotRoster.FindByCallsign(r.Callsign), Callsign = r.Callsign });
            savedCount = entries.Count;
            foreach (WingPilot p in roster)
                if (store.Find(p.Callsign) == null) entries.Add(new Entry { Live = p, Callsign = p.Callsign });
            listKey = studioKey = recordKey = int.MinValue;

            // The draft follows its pilot: a row that went away gives way to the first; a source changed elsewhere (the interop, a
            // save, a delete) reloads a draft the player has not touched — never while a field has the keyboard.
            if (draftNew) return;
            Entry sel = Find(selected);
            if (sel == null)
            {
                if (entries.Count > 0) Select(entries[0]);
                else Clear();
                return;
            }
            draftLive = sel.Live;
            draftOriginal = sel.Saved?.Callsign;
            if (!sourceMoved || touched || WmcNameField.Typing) return;
            if (!PilotStudio.Same(draftStart, sel.Saved ?? FromLive(sel.Live))) Select(sel);
        }

        private Entry Find(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return null;
            foreach (Entry e in entries)
                if (string.Equals(e.Callsign, callsign, System.StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        private int IndexOf(string callsign)
        {
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].Callsign, callsign, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // ---------------------------------------------------------------- the draft

        private void Select(Entry e)
        {
            selected = e.Callsign;
            draft = e.Saved != null ? PilotStudio.DraftOf(e.Saved) : FromLive(e.Live);
            draftStart = draft.Clone();
            draftOriginal = e.Saved?.Callsign;
            draftLive = e.Live;
            draftNew = false;
            NewDraft();
            int at = IndexOf(selected);
            if (at >= 0) listPage = Pages.Of(at, rowsPerPage);
        }

        private void Clear()
        {
            selected = null;
            draft = draftStart = null;
            draftOriginal = null;
            draftLive = null;
            draftNew = false;
            NewDraft();
        }

        /// <summary>A new draft (another pilot, NEW, CLONE, REVERT): field commits for the old one are dropped, the fields refill.</summary>
        private void NewDraft()
        {
            draftSerial++;
            draftRevision++;
            touched = false;
            cloned = false;
            problem = null;
            FillFields();
        }

        /// <summary>This mission's unsaved pilot as a draft: its identity and the face the game already shows (frozen).</summary>
        private static CustomPilotRecord FromLive(WingPilot p)
        {
            var r = new CustomPilotRecord
            {
                Callsign = p.Callsign, Name = p.Name, DialogueTag = p.DialogueTag, Persona = p.Persona, Background = p.Background ?? "",
            };
            r.ApplySelection(PilotStudio.Frozen(p.Name, p.Callsign, p.PortraitSelection));
            return r;
        }

        private int stateRevision = -1, stateStore = int.MinValue, stateRoster = int.MinValue;
        private DraftState stateShown;

        /// <summary>NEW (never saved), SAVED (as stored), EDITED (differs from its source) or NOT SAVED (this mission's pilot as it is);
        /// worked out once per edit or source change.</summary>
        private DraftState StateOf()
        {
            if (draftRevision == stateRevision && storeVersion == stateStore && rosterVersion == stateRoster) return stateShown;
            stateRevision = draftRevision;
            stateStore = storeVersion;
            stateRoster = rosterVersion;
            CustomPilotRecord stored = draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
            stateShown = draft == null || draftNew ? DraftState.New
                : stored != null ? (PilotStudio.Same(draft, stored) ? DraftState.Saved : DraftState.Edited)
                : draftLive != null ? (PilotStudio.Same(draft, FromLive(draftLive)) ? DraftState.NotSaved : DraftState.Edited)
                : DraftState.New;
            return stateShown;
        }

        /// <summary>The draft holds work a switch would drop: edits against its source, or a NEW / CLONE that was touched or cloned.</summary>
        private bool Dirty => draft != null && (draftNew ? touched || cloned : StateOf() == DraftState.Edited);

        private void Edited()
        {
            touched = true;
            draftRevision++;
            problem = null;
            WmcRoom.Instance?.RefreshNow();
        }

        /// <summary>A callsign the studio must not hand out: saved, or flying this mission.</summary>
        private static bool Taken(string callsign) => WingSavedPilots.IsSaved(callsign) || WingPilotRoster.ContainsCallsign(callsign);
    }
}
