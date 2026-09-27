using System;
using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    // SQUADRON › STUDIO for scenarios (Automation.Wmc sq_* arguments): pick a pilot, start a draft, type through the page's own commit
    // path, step the look, save, and report the page as numbers (the harness checks result_above / result_below only).
    internal sealed partial class WmcStudio
    {
        /// <summary>Open a pilot as a row press would, without the unsaved-edits ask: a callsign, or "live0" (this mission's first unsaved
        /// pilot). False when nobody matches.</summary>
        public bool Pick(string who)
        {
            if (string.IsNullOrEmpty(who)) return false;
            Scan(last);
            Entry e = null;
            if (string.Equals(who, "live0", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Entry x in entries)
                    if (x.Saved == null && x.Live != null)
                    {
                        e = x;
                        break;
                    }
            }
            else e = Find(who);
            if (e == null) return false;
            WmcNameField.BlurAny();
            Select(e);
            return true;
        }

        public void StartNew() => NewPilot();

        public void TypeCallsign(string text) => CommitCallsign(SerialText, text);

        public void TypeName(string text) => CommitName(SerialText, text);

        public void TypeBio(string text) => CommitBio(SerialText, text);

        public void SaveDraft() => Save();

        /// <summary>"face:+1", "hair:-1", "body:+1", "suit:+2", "scene:-1".</summary>
        public bool StepLook(string spec)
        {
            int colon = spec != null ? spec.IndexOf(':') : -1;
            if (colon <= 0 || !int.TryParse(spec.Substring(colon + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int dir)) return false;
            int i = Array.IndexOf(LayerIds, spec.Substring(0, colon).ToLowerInvariant());
            if (i < 0) return false;
            StepLook(Layers[i], dir);
            return true;
        }

        private string SerialText => draftSerial.ToString(CultureInfo.InvariantCulture);

        /// <summary>What the page shows (flags as 0/1; the chip as 0 NEW, 1 SAVED, 2 EDITED, 3 NOT SAVED).</summary>
        public void Report(Dictionary<string, object> into)
        {
            Scan(last);
            CustomPilotRecord stored = !draftNew && draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
            WingPilot live = draftNew ? null : draftLive;
            into["sq_saved"] = WingSavedPilots.Store.Records.Count;
            into["sq_roster"] = roster.Count;
            into["sq_rows"] = entries.Count;
            into["sq_draft"] = draft != null ? draft.Callsign : "";
            into["sq_chip"] = draft != null ? (int)StateOf() : -1;
            into["sq_problem"] = problem != null ? 1 : 0;
            into["sq_in_squadron"] = live != null ? 1 : 0;
            into["sq_missions"] = stored != null ? stored.Missions : 0;
            into["sq_sorties"] = stored != null ? stored.Sorties : 0;
            into["sq_kills"] = stored != null ? stored.Kills : 0;
            into["sq_joined_xp"] = live != null ? live.Xp : -1;
            into["sq_recruit"] = recruitReason == null ? 1 : 0;
            into["typing"] = WmcNameField.Typing ? 1 : 0;
        }
    }
}
