using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    // SUPPLY step 4 LAUNCH BASE: NEAREST or ANY FIELD, and the friendly fields a page at a time (only the ON button toggles one).
    internal sealed partial class WmcSupply
    {
        private sealed class BaseView
        {
            public GameObject Root;
            public Image Fill, Rail;
            public AvButton Toggle;
            public TMP_Text Name, Status;
            public Airbase Field;
            public string StatusShown;
            public bool On, Picked, Client, Set;
        }

        private readonly BaseView[] baseViews = new BaseView[BezelLayout.BaseRows];
        private AvButton nearest, anyField;
        private WmcPager basePager;
        private TMP_Text baseState, baseEmpty;
        private Image baseRail;
        private int basePage, baseChipKey = -1;
        private bool modeClient = true;

        private void BuildBase(RectTransform r, float y)
        {
            baseState = WmcKit.StepHeader(r, new Rect(0f, y, width, BezelLayout.StepHead), 4, SupplyWords.BaseTitle, out baseRail);
            float my = y - BezelLayout.StepHead - BezelLayout.HeadGap;
            nearest = AvStyled.Button(r, new Rect(0f, my, 120f, BezelLayout.BaseMode), "NEAREST", "btn", () => SetMode(LaunchMode.Nearest),
                AvButtonStyle.Toggle);
            ids["sup.base.nearest"] = nearest;
            anyField = AvStyled.Button(r, new Rect(124f, my, 120f, BezelLayout.BaseMode), "ANY FIELD", "btn", () => SetMode(LaunchMode.Any),
                AvButtonStyle.Toggle);
            ids["sup.base.any"] = anyField;
            basePager = WmcPager.Build(r, new Rect(width - 100f, my, 100f, BezelLayout.BaseMode), "sup.bases.", ids, TurnBases);
            float ry = my - BezelLayout.BaseMode - BezelLayout.HeadGap;
            baseEmpty = WmcKit.Text(r, new Rect(8f, ry, width - 16f, BezelLayout.BaseRowH), "row-sub");
            for (int i = 0; i < baseViews.Length; i++) baseViews[i] = BuildBaseRow(r, new Rect(0f, ry - i * BezelLayout.BasePitch, width, BezelLayout.BaseRowH), i);
        }

        private BaseView BuildBaseRow(RectTransform parent, Rect r, int i)
        {
            var go = new GameObject("Base" + i, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AvKit.Place(rt, r);
            var v = new BaseView { Root = go };
            v.Fill = AvStyled.Box(rt, new Rect(0f, 0f, r.width, r.height), "row");
            if (v.Fill != null) v.Fill.raycastTarget = false;
            v.Rail = AvStyled.Rail(rt, new Rect(0f, 0f, 3f, r.height), "inert");
            v.Toggle = AvStyled.Button(rt, new Rect(6f, -2f, 44f, 24f), "ON", "btn", () => ToggleBase(i), AvButtonStyle.Toggle);
            ids["sup.base" + i] = v.Toggle;
            v.Name = WmcKit.Text(rt, new Rect(56f, 0f, 312f, r.height), "row-name");
            v.Status = WmcKit.Text(rt, new Rect(376f, 0f, r.width - 382f, r.height), "row-sub", TextAlignmentOptions.MidlineRight);
            go.SetActive(false);
            return v;
        }

        /// <summary>The mode, the step chip and this page of fields: each row's name, ON/OFF and status (READY, APRON, NO JETS,
        /// OFF; ON/OFF alone with no airframe picked), the field the requisition will use highlighted.</summary>
        private void RefreshBase()
        {
            List<Airbase> fields = WingRequisition.Fields;
            nearest.SetLatched(WingRequisition.Mode == LaunchMode.Nearest);
            anyField.SetLatched(WingRequisition.Mode == LaunchMode.Any);
            if (client != modeClient)
            {
                modeClient = client;
                nearest.SetEnabled(!client);
                anyField.SetEnabled(!client);
                nearest.WithTooltip(client ? ClientWhy : "Launch from the field picked on the radial when it is ON, else the nearest ON field.");
                anyField.WithTooltip(client ? ClientWhy : "Launch from the nearest ON field with a hangar ready for this airframe, else as NEAREST.");
            }
            int per = baseViews.Length, on = 0;
            basePage = Pages.Clamp(basePage, fields.Count, per);
            basePager.Set(basePage, Pages.Count(fields.Count, per));
            foreach (Airbase f in fields)
                if (f != null && WingRequisition.IsOn(f)) on++;
            int chip = on * 1000 + fields.Count;
            if (chip != baseChipKey)
            {
                baseChipKey = chip;
                WmcKit.SetStep(baseState, baseRail, SupplyWords.BaseChip(on, fields.Count), on > 0 ? "live" : "warn");
                bool none = fields.Count == 0;
                baseEmpty.gameObject.SetActive(none);
                if (none) WmcKit.Set(baseEmpty, caller == null ? SupplyWords.NotFlyingBases : SupplyWords.NoField);
            }
            int first = Pages.First(basePage, per);
            for (int i = 0; i < per; i++)
            {
                BaseView v = baseViews[i];
                int k = first + i;
                Airbase f = k < fields.Count ? fields[k] : null;
                if (f == null)
                {
                    if (v.Root.activeSelf) v.Root.SetActive(false);
                    v.Field = null;
                    continue;
                }
                if (!v.Root.activeSelf) v.Root.SetActive(true);
                bool isOn = WingRequisition.IsOn(f), picked = ReferenceEquals(f, field);
                string status = selected == null ? (isOn ? "ON" : "OFF") : WingRequisition.Status(f, selected);
                bool named = ReferenceEquals(f, v.Field);
                if (!named)
                {
                    v.Field = f;
                    WmcKit.Set(v.Name, BaseName.Short(WingRequisition.NameOf(f), 40));
                }
                if (v.Set && named && isOn == v.On && picked == v.Picked && ReferenceEquals(status, v.StatusShown) && client == v.Client) continue;
                v.Set = true;
                v.On = isOn;
                v.Picked = picked;
                v.StatusShown = status;
                v.Client = client;
                v.Toggle.SetText(isOn ? "ON" : "OFF");
                v.Toggle.SetLatched(isOn);
                v.Toggle.SetEnabled(!client);
                v.Toggle.WithTooltip(client ? ClientWhy : isOn ? "Turn this field OFF: requisitions will not launch from it." : "Turn this field ON.");
                WmcKit.Set(v.Status, status);
                v.Status.color = status == "READY" ? WmcUi.LevelColor("ok") : status == "NO JETS" || status == "OFF" ? WmcUi.LevelColor("warn") : AvTheme.Dim;
                WmcUi.SetRail(v.Rail, picked ? "live" : isOn ? "info" : "inert");
                if (v.Fill != null) v.Fill.color = WmcUi.RowColor(picked ? "selected" : "rest");
            }
        }

        private void SetMode(LaunchMode mode)
        {
            if (client) return;
            WingRequisition.Mode = mode;
            WmcPanel.Instance?.Refresh();
        }

        private void ToggleBase(int i)
        {
            Airbase f = baseViews[i].Field;
            if (client || f == null) return;
            WingRequisition.SetOn(f, !WingRequisition.IsOn(f));
            WmcPanel.Instance?.Refresh();
        }

        private void TurnBases(int dir)
        {
            basePage = Pages.Clamp(basePage + dir, WingRequisition.Fields.Count, baseViews.Length);
            WmcPanel.Instance?.Refresh();
        }
    }
}
