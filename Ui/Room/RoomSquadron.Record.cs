using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON's RECORD column: RECRUIT / DISCHARGE with its reason, the lifetime SERVICE record (saved pilots), THIS MISSION (rank and
    // XP, kills and sorties, the state in WING's words, the last airframe, the loss) and this mission's perks.
    internal sealed partial class RoomSquadron
    {
        private const int PerkLines = 7;

        private AvButton recruitButton;
        private TMP_Text recruitWhy, service1, service2, missionState, missionRank, missionRecord, missionLast, missionLoss;
        private readonly TMP_Text[] perkLines = new TMP_Text[PerkLines];
        private Image missionXp;
        private float xpWidth;
        private readonly ConfirmGate dischargeGate = new ConfirmGate();
        private int recordKey = int.MinValue;
        private string recruitReason;

        private void BuildRecord()
        {
            float w = area.width, x = SquadronLayout.RecordX(w), rw = SquadronLayout.RecordW(w);
            AvStyled.Label(body, new Rect(x, -SquadronLayout.Pad, rw, SquadronLayout.StudioHead), StudioWords.RecordTitle, "section-title");
            recruitButton = AvStyled.Button(body, new Rect(x, -40f, rw, SquadronLayout.FieldH), "RECRUIT", "btn", RecruitOrDischarge);
            ids["sq.recruit"] = recruitButton;
            recruitWhy = AvStyled.Label(body, new Rect(x, -72f, rw, 28f), "", "row-sub");
            recruitWhy.raycastTarget = false;

            AvStyled.Label(body, new Rect(x, -108f, rw, SquadronLayout.SectionHead), StudioWords.ServiceTitle, "section-title");
            service1 = WmcKit.Text(body, new Rect(x, -130f, rw, 16f), "row-sub");
            service2 = WmcKit.Text(body, new Rect(x, -148f, rw, 16f), "row-sub");

            AvStyled.Label(body, new Rect(x, -178f, rw, SquadronLayout.SectionHead), StudioWords.MissionTitle, "section-title");
            missionState = WmcKit.Text(body, new Rect(x, -200f, rw, 18f), "row-value");
            missionRank = WmcKit.Text(body, new Rect(x, -220f, rw, 16f), "row-sub");
            xpWidth = rw;
            missionXp = WmcUi.Bar(body, new Rect(x, -240f, rw, 6f));
            for (int i = 1; i < 5; i++) AvKit.Panel(body, new Rect(x + rw * PilotXp.Tick(i), -238f, 1f, 10f), AvTheme.Frame).raycastTarget = false;
            missionRecord = WmcKit.Text(body, new Rect(x, -252f, rw, 16f), "row-sub");
            missionLast = WmcKit.Text(body, new Rect(x, -270f, rw, 16f), "row-sub");
            missionLoss = AvStyled.Label(body, new Rect(x, -288f, rw, 30f), "", "row-sub");
            missionLoss.raycastTarget = false;

            AvStyled.Label(body, new Rect(x, -330f, rw, SquadronLayout.SectionHead), StudioWords.PerksTitle, "section-title");
            for (int i = 0; i < PerkLines; i++) perkLines[i] = WmcKit.Text(body, new Rect(x, -352f - i * 18f, rw, 16f), "row-sub");
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
            WmcKit.Set(recruitWhy, recruitReason ?? (live != null ? StudioWords.DischargeTip : StudioWords.RecruitTip));
            recruitWhy.color = recruitReason != null ? AvTheme.Dim : AvTheme.TextPrimary;

            // SERVICE: the lifetime record of a saved pilot.
            if (stored != null)
            {
                WmcKit.Set(service1, StudioWords.Service(stored.Missions, stored.Sorties, stored.Kills));
                WmcKit.Set(service2, StudioWords.BestRank(stored.Missions, stored.Xp));
            }
            else
            {
                WmcKit.Set(service1, draft == null ? "" : StudioWords.NotSaved);
                WmcKit.Set(service2, "");
            }

            // THIS MISSION: the live pilot's numbers in WING's words.
            if (live == null)
            {
                WmcKit.Set(missionState, client ? StudioWords.ClientRecord : draft == null ? "" : StudioWords.NotInMission);
                missionState.color = AvTheme.Dim;
                WmcKit.Set(missionRank, "");
                WmcUi.SetBar(missionXp, xpWidth, 0f, AvTheme.Friendly);
                WmcKit.Set(missionRecord, "");
                WmcKit.Set(missionLast, "");
                WmcKit.Set(missionLoss, "");
                for (int i = 0; i < PerkLines; i++) WmcKit.Set(perkLines[i], i == 0 ? (draft == null ? "" : "NONE THIS MISSION") : "");
                return;
            }
            WmcKit.Set(missionState, SquadronWords.Row(s, ReferenceEquals(live, WingPilotRoster.Upcoming), n));
            missionState.color = WmcUi.LevelColor(SquadronWords.Level(s));
            WmcKit.Set(missionRank, PilotXp.RankLine(live.Xp));
            WmcUi.SetBar(missionXp, xpWidth, PilotXp.Fill(live.Xp), s == PilotStatus.Kia ? AvTheme.Dim : AvTheme.Friendly);
            WmcKit.Set(missionRecord, SquadronWords.Record(live.Kills, live.Sorties));
            WmcKit.Set(missionLast, SquadronWords.Airframe(null, live.LastAircraft));
            WmcKit.Set(missionLoss, s == PilotStatus.Kia ? SquadronWords.KiaSlot(live.LossCause, live.KilledBy) : "");
            for (int i = 0; i < PerkLines; i++)
                WmcKit.Set(perkLines[i], i < live.Perks.Count ? PilotPerks.Name(live.Perks[i]).ToUpperInvariant()
                    : i == 0 && live.Perks.Count == 0 ? "NONE THIS MISSION" : "");
        }

        private void RecruitOrDischarge()
        {
            if (client || draft == null || recruitReason != null) return;
            WingPilot live = draftNew ? null : draftLive;
            if (live == null)
            {
                CustomPilotRecord stored = draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
                WingPilot joined = stored != null ? WingPilotRoster.Enlist(stored) : null;
                WingToast.Show(joined != null ? "Recruited " + joined.Callsign + " for this mission" : "Could not recruit " + draft.Callsign);
                WmcRoom.Instance?.RefreshNow();
                return;
            }
            if (!dischargeGate.Press(live.Callsign, Time.unscaledTime))
            {
                WingToast.Show("Discharge " + live.Callsign + " from this mission? Press DISCHARGE again");
                recordKey = int.MinValue;
                WmcRoom.Instance?.RefreshNow();
                return;
            }
            WingToast.Show(WingPilotRoster.RemoveFromSquadron(live) ? "Discharged " + live.Callsign + " from this mission"
                : "Could not discharge " + live.Callsign);
            WmcRoom.Instance?.RefreshNow();
        }
    }
}
