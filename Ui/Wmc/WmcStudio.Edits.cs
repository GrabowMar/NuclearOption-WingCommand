using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SQUADRON › STUDIO's edits: fields commit to the draft they were opened for; SAVE reads them first.
    internal sealed partial class WmcStudio
    {
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
                WmcPanel.Instance?.Refresh();
                return;
            }
            WingToast.Show(StudioWords.Saved(callsign));
            draftNew = false;
            selected = callsign;
            storeVersion = int.MinValue;
            Scan(last);
            Entry e = Find(callsign);
            if (e != null) Select(e);
            WmcPanel.Instance?.Refresh();
        }

        private void Revert()
        {
            if (draft == null) return;
            ReadFields();
            if (Dirty && !rowGate.Press("#revert", Time.unscaledTime))
            {
                WingToast.Show(StudioWords.UnsavedAsk);
                return;
            }
            WmcNameField.BlurAny();
            Entry e = draftNew ? null : Find(selected);
            if (e != null) Select(e);
            else if (draftStart != null)
            {
                draft = draftStart.Clone();
                NewDraft();
            }
            WmcPanel.Instance?.Refresh();
        }
    }
}
