using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace WingCommand
{
    /// <summary>BEHAVIOUR › OPTIONS (the user, 2026-09-28: "everything we can set"): the scope's doctrine — PROFILE, TARGETS, RANGE,
    /// WEAPONS, RADAR (TACTICAL's DOCTRINE block, moved here) — then the whole wing's standing rules: FALL BACK when outnumbered, what
    /// a wingman does after WINCHESTER and BINGO, POWER, and the radio. Only settings the AI reads are shown.</summary>
    internal sealed class WmcOptions : IWmcPage
    {
        private const float KeyWidth = 80f, ToggleH = 22f, Pitch = 24f;
        private static readonly WingDoctrine[] Profiles = { WingDoctrine.Reserve, WingDoctrine.Escort, WingDoctrine.Sweep };
        private static readonly float[] FallBackRatios = { 0f, 1.5f, 2f, 3f };
        private const string HostOnly = "The host sets these";

        private readonly Dictionary<string, AvButton> ids;
        private readonly WmcScopeRow scope;
        private readonly List<AvKit.PopupEntry> popupEntries = new List<AvKit.PopupEntry>(3);
        private RectTransform page;
        private AvKit.Popup popup;
        private AvButton profileButton;
        private SegmentRow targets, reach, weapons, radar, fallBack, winchester, bingo, power, radio, contacts;
        private string profileShown;
        private WmcContext last;

        public WmcOptions(Dictionary<string, AvButton> controls)
        {
            ids = controls;
            scope = new WmcScopeRow(controls, "opt.scope.");
        }

        public string Hint => "The ENGAGEMENT rows are the scope's; the rows under them are the whole wing's and are kept for the next mission.";

        public string Alert => null;

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            Rect body = WmcUi.Page(page, shellBody, shellBody.height);
            scope.Build(page, body.x, body.y, body.width, "SET FOR");
            float top = BezelLayout.ScopeRow + BezelLayout.ScopeGap;
            WmcScroll scroll = WmcScroll.Build(page, new Rect(body.x, body.y - top, body.width + 8f, Mathf.Max(40f, body.height - top)), "OptionsScroll");
            RectTransform s = scroll.Content;
            float w = scroll.Width, y = 0f;

            Section(s, ref y, w, "ENGAGEMENT");
            AvStyled.Label(s, new Rect(0f, y, KeyWidth, ToggleH), "PROFILE", "metric-key");
            profileButton = AvStyled.Button(s, new Rect(KeyWidth, y, 150f, ToggleH), "RESERVE ›", "btn", OpenProfiles);
            profileButton.WithTooltip("Sets the rows below at once: RESERVE holds fire in close formation, ESCORT covers you, SWEEP hunts wide.");
            ids["opt.profile"] = profileButton;
            y -= Pitch;
            targets = Row(s, ref y, w, "TARGETS", new[] { "HOLD FIRE", "AIR", "GROUND", "BOTH", "COVER" },
                new[]
                {
                    "Hold fire: shoot only when ordered.", "Shoot at enemy aircraft in reach on their own.",
                    "Shoot at ground targets in reach on their own.", "Shoot at air and ground targets in reach on their own.",
                    "Cover: take the one air threat nearest the protected aircraft.",
                }, "opt.targets.", new[] { "hold", "air", "ground", "both", "cover" }, i => Axis(DoctrineAxis.Targets, i));
            reach = Row(s, ref y, w, "RANGE", new[] { "CLOSE 6 KM", "LONG 12 KM" },
                new[] { "Shoot at what comes within 6 km on their own.", "Reach out to 12 km on their own." },
                "opt.reach.", new[] { "slot", "long" }, i => Axis(DoctrineAxis.Reach, i));
            weapons = Row(s, ref y, w, "WEAPONS", new[] { "AUTO", "MISSILES", "GUNS", "NO A-G" },
                new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only." },
                "opt.weapons.", new[] { "auto", "missiles", "guns", "noag" }, i => Axis(DoctrineAxis.Weapons, i));
            radar = Row(s, ref y, w, "RADAR", new[] { "ON", "SILENT", "OFF" },
                new[]
                {
                    "Radar on: the wing sees and shares its picture.",
                    "Silent: radar off until engaged, then on (enemy warners stay quiet).",
                    "Off: no radar, no radar-guided (SARH) shots. The aircraft finds little on its own.",
                }, "opt.radar.", new[] { "on", "silent", "off" }, i => Axis(DoctrineAxis.Radar, i));

            Section(s, ref y, w, "DEFENCE · WHOLE WING");
            fallBack = Row(s, ref y, w, "FALL BACK", new[] { "NEVER", "1.5 : 1", "2 : 1", "3 : 1" },
                new[]
                {
                    "Never fall back, however outnumbered.", "Fall back into formation facing 1.5 enemy aircraft per fighting wingman.",
                    "Fall back facing 2 enemy aircraft per fighting wingman.", "Fall back facing 3 enemy aircraft per fighting wingman.",
                }, "opt.fallback.", new[] { "never", "1.5", "2", "3" }, PickFallBack);

            Section(s, ref y, w, "FOLLOW-ONS · WHOLE WING");
            winchester = Row(s, ref y, w, "WINCHESTER", new[] { "REJOIN", "RTB", "REFIT" },
                new[]
                {
                    "Out of ammunition in a fight: rejoin the formation.", "Out of ammunition: land and return to the reserve.",
                    "Out of ammunition: land, rearm and take off again.",
                }, "opt.winchester.", new[] { "rejoin", "rtb", "refit" }, PickWinchester);
            bingo = Row(s, ref y, w, "BINGO", new[] { "RTB", "REFIT" },
                new[] { "At bingo fuel: land and return to the reserve.", "At bingo fuel: land, refuel and take off again." },
                "opt.bingo.", new[] { "rtb", "refit" }, PickBingo);

            Section(s, ref y, w, "FLIGHT · WHOLE WING");
            power = Row(s, ref y, w, "POWER", new[] { "BUSTER", "GATE" },
                new[] { "Full power, no afterburner.", "Afterburner allowed." }, "opt.power.", new[] { "buster", "gate" },
                i => WmcUi.Order(last, () => WingCommands.Afterburner(i == 1)));

            Section(s, ref y, w, "RADIO");
            radio = Row(s, ref y, w, "CALLS", new[] { "OFF", "ESSENTIAL", "FULL" },
                new[]
                {
                    "No wingman radio calls.", "Emergencies, tactical and status calls only.", "Everything, chatter included.",
                }, "opt.radio.", new[] { "off", "essential", "full" }, PickRadio);
            contacts = Row(s, ref y, w, "CONTACTS", new[] { "CALL", "QUIET" },
                new[]
                {
                    "Wingmen call new enemy aircraft within 40 km with bearing, range, altitude and aspect.",
                    "No contact calls (SCOUT still reports ground contacts).",
                }, "opt.contacts.", new[] { "call", "quiet" }, PickContacts);
            scroll.SetContentHeight(-y + 4f);
            popup = new AvKit.Popup(page, shellBody.width);
        }

        private static void Section(RectTransform s, ref float y, float w, string title)
        {
            if (y < 0f) y -= 6f;
            AvStyled.Label(s, new Rect(0f, y, w, 16f), title, "section-title");
            y -= 20f;
        }

        private SegmentRow Row(RectTransform s, ref float y, float w, string key, string[] labels, string[] tips, string prefix, string[] keys,
            System.Action<int> pick)
        {
            SegmentRow row = SegmentRow.Build(s, new Rect(0f, y, w, ToggleH), KeyWidth, key, labels, tips, prefix, keys, ids, pick);
            y -= Pitch;
            return row;
        }

        public void Shown(WmcContext c) => profileShown = null;

        public void Refresh(WmcContext c)
        {
            last = c;
            scope.Refresh(c);
            bool host = c.CanOrder;
            string cannot = c.Client ? HostOnly : "Wing Command is not ready";
            bool same = c.ScopeDoctrine(out WingDoctrine d);
            string pattern = c.Client ? WmcText.Unknown : same ? d.PatternName : "MIXED";
            if (pattern != profileShown)
            {
                profileShown = pattern;
                profileButton.SetText(pattern + " ›");
            }
            profileButton.SetEnabled(host);
            Doctrine(targets, c, DoctrineAxis.Targets, host, cannot);
            Doctrine(reach, c, DoctrineAxis.Reach, host, cannot);
            Doctrine(weapons, c, DoctrineAxis.Weapons, host, cannot);
            Doctrine(radar, c, DoctrineAxis.Radar, host, cannot);

            WingConfig cfg = Plugin.Settings;
            if (cfg == null) return;
            fallBack.Set(FallBackIndex(cfg.FallBackRatio.Value));
            fallBack.SetEnabled(host, cannot);
            winchester.Set((int)cfg.AfterWinchester.Value);
            winchester.SetEnabled(host, cannot);
            bingo.Set((int)cfg.AfterBingo.Value);
            bingo.SetEnabled(host, cannot);
            power.Set(c.Wing != null && !c.Client ? (c.Wing.AfterburnerAllowed ? 1 : 0) : -1);
            power.SetEnabled(host, cannot);
            radio.Set((int)cfg.Radio.Value);
            contacts.Set(cfg.ContactCalls.Value ? 0 : 1);
        }

        private static void Doctrine(SegmentRow row, WmcContext c, DoctrineAxis axis, bool host, string cannot)
        {
            row.Set(host ? c.ScopeValue(axis) : -1);
            row.SetEnabled(host, cannot);
        }

        private static int FallBackIndex(float ratio)
        {
            for (int i = 0; i < FallBackRatios.Length; i++)
                if (Mathf.Abs(ratio - FallBackRatios[i]) < 0.05f) return i;
            return -1;
        }

        private void OpenProfiles()
        {
            if (last == null || !last.CanOrder) return;
            string current = last.ScopeDoctrine(out WingDoctrine scoped) ? scoped.PatternName : null;
            popupEntries.Clear();
            foreach (WingDoctrine d in Profiles)
                popupEntries.Add(new AvKit.PopupEntry(d.PatternName, null, d.PatternName == current));
            popup.Show(WmcKit.RectIn(page, (RectTransform)profileButton.transform), popupEntries,
                i => WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetDoctrine, Text = Profiles[i].ToString(), Scope = last.Scope })));
        }

        /// <summary>One setting at the scope's level (spec WMC rebuild R3): the rest of each aircraft's doctrine stays.</summary>
        private void Axis(DoctrineAxis axis, int i)
        {
            if (last == null) return;
            string word = WingDoctrine.ValueName(axis, (byte)i);
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder { Kind = OrderKind.SetOverride, Number = (int)axis, Text = word, Scope = last.Scope }));
        }

        private void PickFallBack(int i) => WmcUi.Order(last, () => Plugin.Settings.FallBackRatio.Value = FallBackRatios[i]);

        private void PickWinchester(int i) => WmcUi.Order(last, () => Plugin.Settings.AfterWinchester.Value = (WinchesterAction)i);

        private void PickBingo(int i) => WmcUi.Order(last, () => Plugin.Settings.AfterBingo.Value = (BingoAction)i);

        // The radio is this player's own: no host needed.
        private void PickRadio(int i)
        {
            if (Plugin.Settings != null) Plugin.Settings.Radio.Value = (RadioLevel)i;
        }

        private void PickContacts(int i)
        {
            if (Plugin.Settings != null) Plugin.Settings.ContactCalls.Value = i == 0;
        }
    }
}
