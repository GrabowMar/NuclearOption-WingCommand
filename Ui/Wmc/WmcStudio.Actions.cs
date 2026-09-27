using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON › STUDIO's pilot actions: open a pilot (asking before dropping edits), NEW, CLONE, DELETE, IMPORT, EXPORT, RECRUIT and
    // DISCHARGE.
    internal sealed partial class WmcStudio
    {
        /// <summary>A pilot from the picker; with unsaved edits the first press asks, the second drops them.</summary>
        private void Open(int entry)
        {
            if (entry < 0 || entry >= entries.Count) return;
            Entry e = entries[entry];
            if (!draftNew && string.Equals(e.Callsign, selected, System.StringComparison.OrdinalIgnoreCase)) return;
            if (Dirty && !rowGate.Press(e.Callsign, Time.unscaledTime))
            {
                WingToast.Show(StudioWords.UnsavedAsk);
                return;
            }
            WmcNameField.BlurAny();
            Select(e);
            WmcPanel.Instance?.Refresh();
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
            WmcPanel.Instance?.Refresh();
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
            // Review R7: a CLONE is the player's pilot even before an edit; a row click asks before dropping it.
            cloned = true;
            WmcPanel.Instance?.Refresh();
        }

        private void Delete()
        {
            if (draftNew || draftOriginal == null) return;
            string callsign = draftOriginal;
            if (!deleteGate.Press(callsign, Time.unscaledTime))
            {
                WingToast.Show(StudioWords.DeleteAsk(callsign));
                pickerKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
                return;
            }
            if (!WingSavedPilots.Delete(callsign, out string why)) problem = why;
            else WingToast.Show("Deleted " + callsign + " from the saved pilots");
            WmcPanel.Instance?.Refresh();
        }

        private void Import()
        {
            WingSavedPilots.Import(out int added, out int skipped, out int files);
            WingToast.Show(StudioWords.ImportToast(added, skipped, files));
            WmcPanel.Instance?.Refresh();
        }

        private void Export()
        {
            bool ok = WingSavedPilots.Export(out int n);
            WingToast.Show(ok ? StudioWords.ExportToast(n) : "EXPORT failed: see the log");
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
                WmcPanel.Instance?.Refresh();
                return;
            }
            if (!dischargeGate.Press(live.Callsign, Time.unscaledTime))
            {
                WingToast.Show("Discharge " + live.Callsign + " from this mission? Press DISCHARGE again");
                recordKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
                return;
            }
            WingToast.Show(WingPilotRoster.RemoveFromSquadron(live) ? "Discharged " + live.Callsign + " from this mission"
                : "Could not discharge " + live.Callsign);
            WmcPanel.Instance?.Refresh();
        }
    }
}
