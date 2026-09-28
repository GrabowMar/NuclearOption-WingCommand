using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // WING's dossier (portrait, identity, stamp, rank line, XP bar with rank ticks, record, radio, RELEASE) and its PERKS 2×2.
    internal sealed partial class WmcWing
    {
        private const float TextX = 82f, TextW = 268f, BarW = 368f, StampX = 356f, StampW = 94f;

        private Image dossierRail, stampRail, xpFill;
        private TMP_Text identity, stamp, rankLine, record, radio;
        private readonly TMP_Text[] rankLetters = new TMP_Text[5];
        private WmcPortrait portrait;
        private AvButton release;
        private bool releaseOn;
        private readonly ConfirmGate releaseGate = new ConfirmGate();
        private int dossierKey = int.MinValue;

        private void BuildDossier(RectTransform r, float y)
        {
            const float h = BezelLayout.DossierH;
            AvStyled.Box(r, new Rect(0f, y, width, h), "card");
            dossierRail = AvStyled.Rail(r, new Rect(0f, y, 3f, h), "inert");
            portrait = WmcPortrait.Build(r, new Rect(8f, y - 16f, 64f, 88f));
            identity = WmcKit.Text(r, new Rect(TextX, y - 8f, TextW, 18f), "row-name");
            var chip = new Rect(StampX, y - 8f, StampW, 18f);
            AvStyled.Box(r, chip, "chip");
            stampRail = AvStyled.Rail(r, new Rect(chip.x, chip.y, 3f, chip.height), "inert");
            stamp = WmcKit.Text(r, new Rect(chip.x + 7f, chip.y, chip.width - 9f, chip.height), "row-sub");
            rankLine = WmcKit.Text(r, new Rect(TextX, y - 30f, BarW, 14f), "row-sub");
            xpFill = WmcUi.Bar(r, new Rect(TextX, y - 48f, BarW, 6f));
            for (int i = 1; i < 5; i++)
                AvKit.Panel(r, new Rect(TextX + BarW * PilotXp.Tick(i), y - 46f, 1f, 10f), AvTheme.Frame).raycastTarget = false;
            float cell = BarW / 5f;
            for (int i = 0; i < 5; i++)
                rankLetters[i] = WmcKit.Text(r, new Rect(TextX + i * cell, y - 56f, cell, 12f), "metric-key", TextAlignmentOptions.Center);
            record = WmcKit.Text(r, new Rect(TextX, y - 74f, TextW, 14f), "row-sub");
            radio = WmcKit.Text(r, new Rect(TextX, y - 90f, TextW, 14f), "row-sub");
            release = AvStyled.Button(r, new Rect(StampX, y - 74f, StampW, 22f), "RELEASE", "btn", Release);
            release.WithTooltip(SquadronWords.ReleaseTip);
            ids["wing.release"] = release;
        }

        /// <summary>The dossier of the inspected pilot, rebuilt only when the roster, the pilot or RELEASE's ask changed.</summary>
        private void RefreshDossier()
        {
            WingPilot p = client ? null : inspected;
            int at = IndexOf(p);
            PilotStatus s = at >= 0 ? status[at] : PilotStatus.Free;
            bool asking = p != null && releaseGate.IsArmed(p.Callsign, Time.unscaledTime);
            int key;
            unchecked
            {
                key = scanVersion * 31 + at * 7 + (client ? 3 : 0) + (asking ? 5 : 0);
            }
            if (key == dossierKey) return;
            dossierKey = key;
            portrait.Set(p);
            if (p == null)
            {
                WmcKit.Set(identity, client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                WmcKit.Set(stamp, WmcText.Unknown);
                WmcUi.SetRail(stampRail, "inert");
                WmcUi.SetRail(dossierRail, "inert");
                WmcKit.Set(rankLine, "");
                WmcUi.SetBar(xpFill, BarW, 0f, AvTheme.Friendly);
                for (int i = 0; i < 5; i++) SetLetter(i, false);
                WmcKit.Set(record, "");
                WmcKit.Set(radio, "");
                SetRelease(false, false, client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                return;
            }
            bool next = ReferenceEquals(p, upcoming);
            WmcKit.Set(identity, PilotPick.NameLine(p.Callsign, p.Name));
            WmcKit.Set(stamp, SquadronWords.Stamp(s, next, number[at]));
            WmcUi.SetRail(stampRail, SquadronWords.Rail(s, next));
            WmcUi.SetRail(dossierRail, SquadronWords.Rail(s, next));
            WmcKit.Set(rankLine, PilotXp.RankLine(p.Xp));
            WmcUi.SetBar(xpFill, BarW, PilotXp.Fill(p.Xp), s == PilotStatus.Kia ? AvTheme.Dim : AvTheme.Friendly);
            for (int i = 0; i < 5; i++) SetLetter(i, i == (int)p.Rank);
            WmcKit.Set(record, SquadronWords.Record(p.Kills, p.Sorties));
            WmcKit.Set(radio, SquadronWords.Persona(p.Persona.ToString()));
            SetRelease(s == PilotStatus.Flying, asking, SquadronWords.ReleaseWhy(s, false));
        }

        private void SetLetter(int i, bool lit)
        {
            WmcKit.Set(rankLetters[i], SquadronWords.Badge((WingRank)i));
            rankLetters[i].color = lit ? RankColor((WingRank)i) : AvTheme.Dim;
        }

        private void SetRelease(bool on, bool asking, string why)
        {
            releaseOn = on;
            release.SetText(SquadronWords.ReleaseLabel(asking));
            release.SetLatched(asking);
            release.SetEnabled(on);
            release.WithTooltip(on ? SquadronWords.ReleaseTip : why);
        }

        /// <summary>RELEASE, pressed twice: the member flying the dossier's pilot goes to the game's AI (the Release order).</summary>
        private void Release()
        {
            WingPilot p = inspected;
            if (last == null || p == null || client) return;
            WmcUi.Order(last, () =>
            {
                WingMember m = MemberOf(p);
                if (m == null) return;
                if (!releaseGate.Press(p.Callsign, Time.unscaledTime))
                {
                    WingToast.Show(SquadronWords.ReleaseAsk(WmcText.Cut(p.Callsign, PilotPick.CallsignChars), m.Number));
                    dossierKey = int.MinValue;
                    WmcPanel.Instance?.Refresh();
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Release, Scope = WingScope.OfMembers(m.Aircraft.persistentID.Id) });
                dossierKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
            });
        }

        // ---------------------------------------------------------------- PERKS 2×2

        private sealed class PerkView
        {
            public Image Rail;
            public TMP_Text Title, Line;
            public AvTooltipTarget Tip;
        }

        private static readonly PilotPerk[] NoPerks = new PilotPerk[0];
        private TMP_Text perksNote;
        private readonly PerkView[] perks = new PerkView[PerkCards.Slots];
        private int perksKey = int.MinValue;

        private void BuildPerks(RectTransform r, float y)
        {
            AvStyled.Label(r, new Rect(0f, y, 120f, BezelLayout.SquadHead), SquadronWords.PerksTitle, "section-title");
            perksNote = WmcKit.Text(r, new Rect(120f, y, width - 120f, BezelLayout.SquadHead), "section-title-note", TextAlignmentOptions.MidlineRight);
            float w = BezelLayout.PerkCardW(width), top = y - BezelLayout.SquadHead - BezelLayout.HeadGap;
            for (int i = 0; i < perks.Length; i++)
            {
                var at = new Rect((i % 2) * (w + BezelLayout.PerkGap), top - (i / 2) * (BezelLayout.PerkCardH + BezelLayout.PerkGap), w, BezelLayout.PerkCardH);
                Image box = AvStyled.Box(r, at, "row");
                var v = new PerkView { Rail = AvStyled.Rail(r, new Rect(at.x, at.y, 3f, at.height), "inert") };
                if (box != null)
                {
                    // Not a click target: hovering shows the perk's whole description.
                    box.raycastTarget = true;
                    v.Tip = box.gameObject.AddComponent<AvTooltipTarget>();
                    v.Tip.Initialise("");
                }
                v.Title = WmcKit.Text(r, new Rect(at.x + 8f, at.y - 3f, at.width - 12f, 16f), "row-name");
                v.Line = AvStyled.Label(r, new Rect(at.x + 8f, at.y - 20f, at.width - 12f, 26f), "", "row-sub");
                v.Line.raycastTarget = false;
                perks[i] = v;
            }
        }

        /// <summary>The inspected pilot's perks in order, the ones not active in 1.0 marked, locked slots naming the rank and XP
        /// that earn them, and "+n MORE" on the fourth card past four (PerkCards).</summary>
        private void RefreshPerks()
        {
            WingPilot p = client ? null : inspected;
            bool off = !Plugin.Settings.PilotProgression.Value || Plugin.Settings.RankEffect.Value <= 0f;
            int key;
            unchecked
            {
                key = scanVersion * 31 + IndexOf(p) * 7 + (off ? 3 : 0) + (client ? 5 : 0);
            }
            if (key == perksKey) return;
            perksKey = key;
            WingRank rank = p != null ? p.Rank : WingRank.Rookie;
            WmcKit.Set(perksNote, p == null ? WmcText.Unknown : PerkCards.Head(p.Perks.Count, rank, p.Lost, off));
            for (int i = 0; i < perks.Length; i++)
            {
                PerkCard card = PerkCards.For(i, p != null ? p.Perks : (System.Collections.Generic.IReadOnlyList<PilotPerk>)NoPerks, rank);
                PerkView v = perks[i];
                WmcKit.Set(v.Title, card.Title);
                WmcKit.Set(v.Line, card.Line);
                v.Title.color = card.Locked ? AvTheme.Dim : AvTheme.TextPrimary;
                v.Line.color = card.Inactive ? WmcUi.LevelColor("warn") : AvTheme.Dim;
                WmcUi.SetRail(v.Rail, card.Locked ? "inert" : card.Inactive || off ? "warn" : "live");
                v.Tip?.SetText(card.Tip);
            }
        }
    }
}
