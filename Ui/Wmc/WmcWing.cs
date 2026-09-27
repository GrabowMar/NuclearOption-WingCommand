using System.Collections.Generic;
using System.Globalization;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>WING (spec WMC rebuild §WING), the 0.9 squadron page with its critique fixed: the SQUADRON roster in join order with
    /// one status word per pilot (the same word on the row, the dossier stamp and the tiles), a dossier (portrait, rank and XP bar with
    /// rank ticks, record, radio, RELEASE), PERKS 2×2, and an AIRFRAME ASSIGNMENT bar pinned to the floor with AIR SAR and LOCAL SAR.
    /// A row inspects only (R6 ruling): SUPPLY's pilot card picks who flies. The roster is the host's; a client sees one card.</summary>
    internal sealed partial class WmcWing : IWmcPage
    {
        private readonly Dictionary<string, AvButton> ids;
        private RectTransform page, content;
        private Rect body;
        private float width;
        private WmcScroll scroll;
        private int perPage;

        // This refresh's snapshot: Metrics takes it first, Refresh reuses it.
        private WmcContext last;
        private WingService wing;
        private bool client;

        // The roster as the rows show it, rebuilt only when the roster's version moves.
        private readonly List<WingPilot> roster = new List<WingPilot>();
        private PilotStatus[] status = new PilotStatus[0];
        private int[] number = new int[0];
        private int scanVersion = int.MinValue, flying, inbound, free, sar, kia, captured, lost;
        private bool scanClient;
        private WingService scanWing;
        private WingPilot upcoming;

        private WingPilot inspected;
        private int listPage;
        private string hint, alert;
        private int metricKey = int.MinValue, metricGeneration = -1, rowsKey = int.MinValue, alertVersion = int.MinValue;

        public WmcWing(Dictionary<string, AvButton> controls) => ids = controls;

        public string Hint => hint;

        public string Alert => alert;

        /// <summary>The pilot the dossier shows (R7's STUDIO › opens the studio on it).</summary>
        public WingPilot Inspected => inspected;

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            body = WmcUi.Page(page, shellBody, shellBody.height);
            width = body.width;
            float view = BezelLayout.WingView(shellBody.height);
            scroll = WmcScroll.Build(page, new Rect(body.x, body.y, width + 8f, view), "WingScroll");
            content = scroll.Content;
            perPage = BezelLayout.PilotRows(shellBody.height);
            BuildRoster(content);
            float footer = BezelLayout.SquadHead + BezelLayout.HeadGap + perPage * BezelLayout.PilotPitch + BezelLayout.HeadGap;
            float dossier = footer + BezelLayout.RosterFoot + BezelLayout.DossierGap;
            BuildFooter(content, -footer);
            BuildDossier(content, -dossier);
            BuildPerks(content, -(dossier + BezelLayout.DossierH + BezelLayout.DossierGap));
            BuildAssignment(body.y - view);
            scroll.SetContentHeight(BezelLayout.WingContent(perPage));
        }

        /// <summary>The wing and the roster's statuses, once a panel refresh (Metrics takes it, Refresh reuses it); the statuses are
        /// rebuilt only when the roster's version moved (every change WING shows bumps it: R6 T2/T3).</summary>
        private void Snapshot(WmcContext c)
        {
            last = c;
            client = c.Client;
            wing = client ? null : c.Wing;
            int v = WingPilotRoster.Version;
            if (v == scanVersion && client == scanClient && ReferenceEquals(wing, scanWing)) return;
            scanVersion = v;
            scanClient = client;
            scanWing = wing;
            roster.Clear();
            if (!client) WingPilotRoster.Roster(roster);
            if (status.Length < roster.Count)
            {
                status = new PilotStatus[roster.Count + 8];
                number = new int[roster.Count + 8];
            }
            flying = inbound = free = sar = kia = captured = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                status[i] = StatusOf(roster[i], out number[i]);
                switch (status[i])
                {
                    case PilotStatus.Free: free++; break;
                    case PilotStatus.Inbound: inbound++; break;
                    case PilotStatus.Flying: flying++; break;
                    case PilotStatus.Kia: kia++; break;
                    case PilotStatus.Captured: captured++; break;
                    default: sar++; break;
                }
            }
            lost = sar + kia + captured;
            upcoming = client ? null : WingPilotRoster.Upcoming;
            // The dossier follows its pilot; a pilot who left gives way to the next up, else the first.
            if (inspected == null || !roster.Contains(inspected)) inspected = upcoming ?? (roster.Count > 0 ? roster[0] : null);
            int at = inspected != null ? roster.IndexOf(inspected) : -1;
            listPage = at >= 0 ? Pages.Of(at, perPage) : Pages.Clamp(listPage, roster.Count, perPage);
        }

        private PilotStatus StatusOf(WingPilot p, out int n) => WmcPilots.StatusOf(p, wing, out n);

        private WingMember MemberOf(WingPilot p) => WmcPilots.MemberOf(p, wing);

        private int IndexOf(WingPilot p) => p != null ? roster.IndexOf(p) : -1;

        /// <summary>PILOTS · READY · LOST (spec WMC rebuild §bezel shell; LOST is every pilot out of action), rebuilt on change.</summary>
        public void Metrics(WmcContext c, WmcMetricRow m)
        {
            Snapshot(c);
            bool shown = m.Generation != metricGeneration;
            int key = client ? -1 : scanVersion;
            if (key == metricKey && !shown) return;
            metricKey = key;
            metricGeneration = m.Generation;
            if (client)
            {
                for (int i = 0; i < 3; i++) m.Set(i, WmcText.Unknown, "HOST ROSTER", 0f, AvTheme.Friendly);
                return;
            }
            int pilots = roster.Count - kia;
            m.Set(0, N(pilots), SquadronWords.PilotsCaption(flying, inbound), pilots > 0 ? (float)flying / pilots : 0f, AvTheme.Friendly);
            m.Set(1, N(free), SquadronWords.ReadyCaption(upcoming != null ? WmcText.Cut(upcoming.Callsign, PilotPick.CallsignChars) : null),
                pilots > 0 ? (float)free / pilots : 0f, AvTheme.Friendly);
            m.Set(2, N(lost), SquadronWords.LostCaption(sar, kia, captured), roster.Count > 0 ? (float)lost / roster.Count : 0f,
                lost > 0 ? AvTheme.Warning : AvTheme.Friendly);
        }

        public void Refresh(WmcContext c)
        {
            if (!ReferenceEquals(last, c)) Snapshot(c);
            RefreshRoster();
            RefreshDossier();
            RefreshPerks();
            RefreshAssignment();
            if (alertVersion == scanVersion) return;
            alertVersion = scanVersion;
            hint = SquadronWords.Hint(client, roster.Count);
            alert = null;
            for (int i = 0; i < roster.Count && alert == null; i++)
                alert = SquadronWords.Alert(status[i], WmcText.Cut(roster[i].Callsign, PilotPick.CallsignChars));
        }

        // ---------------------------------------------------------------- SQUADRON: head, rows, footer

        private sealed class RowView
        {
            public GameObject Root;
            public Image Fill, Rail, Badge;
            public TMP_Text Letter, Callsign, Name, State;
            public AvButton Hit;
            public WingPilot Pilot;
            public bool Selected, Styled;
        }

        private TMP_Text headNote, emptyText;
        private GameObject emptyRoot;
        private RowView[] rows;
        private WmcPager pager;
        private AvButton recruit, studio;

        private void BuildRoster(RectTransform r)
        {
            AvStyled.Label(r, new Rect(0f, 0f, 160f, BezelLayout.SquadHead), SquadronWords.Title, "section-title");
            headNote = WmcKit.Text(r, new Rect(160f, 0f, width - 160f, BezelLayout.SquadHead), "section-title-note", TextAlignmentOptions.MidlineRight);
            float top = BezelLayout.SquadHead + BezelLayout.HeadGap;
            rows = new RowView[perPage];
            for (int i = 0; i < perPage; i++) rows[i] = BuildRow(r, i, -(top + i * BezelLayout.PilotPitch));

            var go = new GameObject("WingEmpty", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(r, false);
            AvKit.Place(rt, new Rect(0f, -top, width, 48f));
            AvStyled.Box(rt, new Rect(0f, 0f, width, 48f), "card", "inert");
            emptyText = WmcKit.Text(rt, new Rect(12f, -6f, width - 24f, 36f), "row-name");
            emptyRoot = go;
            emptyRoot.SetActive(false);
        }

        private RowView BuildRow(RectTransform parent, int index, float y)
        {
            const float h = BezelLayout.PilotRowH;
            var go = new GameObject("WingPilot" + index, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, new Rect(0f, y, width, h));
            var v = new RowView { Root = go };
            v.Fill = AvStyled.Box(rt, new Rect(0f, 0f, width, h), "row");
            if (v.Fill != null) v.Fill.raycastTarget = false;
            v.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, h), "inert");
            v.Hit = AvKit.HitButton(rt, new Rect(0f, 0f, width, h), () => PressRow(index));
            v.Hit.WithTooltip(SquadronWords.RowTip);
            ids["wing.pilot" + index] = v.Hit;
            v.Badge = AvKit.Panel(rt, new Rect(6f, -5f, 20f, 20f), AvTheme.SurfaceInert);
            v.Badge.raycastTarget = false;
            v.Letter = WmcKit.Text(rt, new Rect(6f, -5f, 20f, 20f), "row-name", TextAlignmentOptions.Center);
            v.Callsign = WmcKit.Text(rt, new Rect(30f, 0f, 142f, h), "row-name");
            v.Name = WmcKit.Text(rt, new Rect(176f, 0f, 106f, h), "row-sub");
            v.State = WmcKit.Text(rt, new Rect(296f, 0f, width - 302f, h), "row-value", TextAlignmentOptions.MidlineRight);
            go.SetActive(false);
            return v;
        }

        private void BuildFooter(RectTransform r, float y)
        {
            const float h = BezelLayout.RosterFoot;
            pager = WmcPager.Build(r, new Rect(0f, y, 210f, h), "wing.pilots.", ids, TurnPage);
            recruit = AvStyled.Button(r, new Rect(216f, y, 116f, h), "RECRUIT", "btn", Recruit);
            recruit.WithTooltip(SquadronWords.RecruitTip);
            ids["wing.recruit"] = recruit;
            studio = AvStyled.Button(r, new Rect(338f, y, width - 338f, h), "STUDIO ›", "btn", OpenStudio, AvButtonStyle.Quiet);
            studio.WithTooltip(SquadronWords.StudioTip);
            ids["wing.studio"] = studio;
        }

        /// <summary>Rows, head, pager and footer: rebuilt only when the roster, the page, the dossier's pilot or the next up changed.</summary>
        private void RefreshRoster()
        {
            int key;
            unchecked
            {
                key = scanVersion * 31 + listPage * 7 + (client ? 3 : 0) + IndexOf(inspected) * 131 + IndexOf(upcoming) * 1009;
            }
            if (key == rowsKey) return;
            rowsKey = key;
            WmcKit.Set(headNote, client ? WmcText.Unknown : SquadronWords.Head(roster.Count, sar, kia));
            bool none = client || roster.Count == 0;
            emptyRoot.SetActive(none);
            WmcKit.Set(emptyText, client ? SquadronWords.ClientWhy : SquadronWords.Empty);
            int first = Pages.First(listPage, perPage);
            for (int i = 0; i < rows.Length; i++)
            {
                int at = first + i;
                RowView v = rows[i];
                bool on = !none && at < roster.Count;
                if (v.Root.activeSelf != on) v.Root.SetActive(on);
                if (!on)
                {
                    v.Pilot = null;
                    continue;
                }
                WingPilot p = roster[at];
                bool next = ReferenceEquals(p, upcoming);
                v.Pilot = p;
                WmcKit.Set(v.Letter, SquadronWords.Badge(p.Rank));
                v.Letter.color = RankColor(p.Rank);
                WmcKit.Set(v.Callsign, WmcText.Cut(p.Callsign, PilotPick.CallsignChars));
                WmcKit.Set(v.Name, WmcText.Cut(p.Name, SquadronWords.NameChars));
                WmcKit.Set(v.State, SquadronWords.Row(status[at], next, number[at]));
                v.State.color = WmcUi.LevelColor(SquadronWords.Level(status[at]));
                WmcUi.SetRail(v.Rail, SquadronWords.Rail(status[at], next));
                bool selected = ReferenceEquals(p, inspected);
                if (!v.Styled || v.Selected != selected)
                {
                    v.Styled = true;
                    v.Selected = selected;
                    if (v.Fill != null) v.Hit.SetRowHighlight(v.Fill, WmcUi.RowColor(WmcStyle.RowRest(selected)), WmcUi.RowColor(WmcStyle.RowHover(selected)));
                }
            }
            pager.Set(listPage, Pages.Count(roster.Count, perPage));
            pager.SetEnabled(!client, SquadronWords.ClientWhy);
            recruit.SetEnabled(!client);
            recruit.WithTooltip(client ? SquadronWords.ClientWhy : SquadronWords.RecruitTip);
        }

        /// <summary>The five rank letters in the 0.9 colours; the words, not the colour, carry the state.</summary>
        private static Color RankColor(WingRank r) =>
            r == WingRank.Legend ? AvTheme.RailCaution : r == WingRank.Ace ? AvTheme.RailReady : r == WingRank.Veteran ? AvTheme.RailInfo
            : r == WingRank.Wingman ? AvTheme.TextPrimary : AvTheme.Dim;

        /// <summary>A row opens its pilot's dossier (inspect only: R6 ruling).</summary>
        private void PressRow(int index)
        {
            WingPilot p = index >= 0 && index < rows.Length ? rows[index].Pilot : null;
            if (p == null) return;
            Inspect(p);
        }

        /// <summary>Show this pilot in the dossier and the bar; the page follows it.</summary>
        public void Inspect(WingPilot p)
        {
            if (p == null) return;
            inspected = p;
            int at = roster.IndexOf(p);
            if (at >= 0) listPage = Pages.Of(at, perPage);
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>‹ ›: the next page, and the dossier moves to its first pilot (the dossier is never off the page).</summary>
        private void TurnPage(int dir)
        {
            if (client || roster.Count == 0) return;
            listPage = Pages.Turn(listPage, dir, roster.Count, perPage);
            inspected = roster[Pages.First(listPage, perPage)];
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>STUDIO ›: the planning room's SQUADRON on the dossier's pilot (R7).</summary>
        private void OpenStudio()
        {
            WmcRoom room = WmcRoom.Instance;
            if (room == null) return;
            if (!client && inspected != null) room.Squadron.Focus(inspected);
            room.Open(RoomNotches.Squadron);
        }

        private void Recruit()
        {
            if (client) return;
            WingPilot p = WingPilotRoster.RecruitManual();
            if (p == null) return;
            WingToast.Show(SquadronWords.Recruited(p.Callsign, p.Name));
            if (last != null) Snapshot(last);
            Inspect(p);
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
