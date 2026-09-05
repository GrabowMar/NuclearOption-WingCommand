using System;
using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------------- MAP panel

        private sealed class MapPresenter : Presenter
        {
            private sealed class ToggleBinding
            {
                public AvButton Button;
                public Func<bool> IsOn;
            }

            private readonly MapOptions options;
            private readonly List<ToggleBinding> toggles = new List<ToggleBinding>();

            public MapPresenter(MFDScreen screen, MapOptions options)
                : base(screen, VanillaMfdPanelId.Map)
            {
                this.options = options;
            }

            protected override void BuildContent()
            {
                RectTransform page = Shell.CreatePage("MapOptions");
                DrawSpine(page);

                float y = -AvTokens.Space1;
                y = Heading(page, y, Shell.Body.width, "MARKERS", "LIVE MAP LAYERS");
                AddGrid(page, ref y, 4,
                    new[] { "OBJECTIVES", "TARGETS", "JAMMING", "LABELS" },
                    new Func<bool>[]
                    {
                        () => options.showObjectives,
                        () => options.showTargetInfo,
                        () => options.showJamming,
                        () => options.showGridLabels,
                    },
                    new Action[]
                    {
                        options.ToggleShowObjectives,
                        options.ToggleShowTargetInfo,
                        options.ToggleShowJamming,
                        options.ToggleShowGridLabels,
                    });

                y = Heading(page, y, Shell.Body.width, "TOOLTIP", "MAP HOVER DETAIL");
                AddGrid(page, ref y, 4,
                    new[] { "HIDE", "INFO", "AMMO", "ORDERS" },
                    new Func<bool>[]
                    {
                        () => options.tooltipType == MapOptions.TooltipType.None,
                        () => options.tooltipType == MapOptions.TooltipType.Info,
                        () => options.tooltipType == MapOptions.TooltipType.Ammo,
                        () => options.tooltipType == MapOptions.TooltipType.Order,
                    },
                    new Action[]
                    {
                        () => options.SetToolTipType((int)MapOptions.TooltipType.None),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Info),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Ammo),
                        () => options.SetToolTipType((int)MapOptions.TooltipType.Order),
                    });

                y = Heading(page, y, Shell.Body.width, "ICON SCALE", "TACTICAL SYMBOL SIZE");
                AddGrid(page, ref y, 3,
                    new[] { "SMALL", "MEDIUM", "LARGE" },
                    new Func<bool>[]
                    {
                        () => Mathf.Approximately(options.iconSize, 0.6f),
                        () => Mathf.Approximately(options.iconSize, 0.8f),
                        () => Mathf.Approximately(options.iconSize, 1.0f),
                    },
                    new Action[]
                    {
                        () => options.SetIconSize(0),
                        () => options.SetIconSize(1),
                        () => options.SetIconSize(2),
                    });

                y = Heading(page, y, Shell.Body.width, "SPECIAL ICONS");
                AddGrid(page, ref y, 2,
                    new[] { "PILOTS", "AIRBASES" },
                    new Func<bool>[] { () => options.showPilotIcons, () => options.showAirbaseIcon },
                    new Action[] { options.ToggleShowPilotIcons, options.ToggleShowAirbaseIcons });
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "DISPLAY FILTERS";
                Shell.DataBar.SetChip(0, options.showObjectives ? "OBJ ON" : "OBJ OFF", options.showObjectives);
                Shell.DataBar.SetChip(1, options.showTargetInfo ? "TGT ON" : "TGT OFF", options.showTargetInfo);
                Shell.DataBar.SetChip(2, "SCALE " + IconSizeLabel(), true);
                for (int i = 0; i < toggles.Count; i++)
                    toggles[i].Button.SetLatched(toggles[i].IsOn());
            }

            protected override string AmbientStatus() =>
                "MAP FILTERS — " + (options.showObjectives ? "OBJECTIVES" : "OBJECTIVES HIDDEN");

            private string IconSizeLabel()
            {
                if (options.iconSize < 0.7f) return "S";
                if (options.iconSize < 0.9f) return "M";
                return "L";
            }

            private void AddGrid(RectTransform parent, ref float y, int columns,
                                 string[] labels, Func<bool>[] states, Action[] actions)
            {
                float gap = AvTokens.Gap;
                float width = (Shell.Body.width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    int row = i / columns;
                    int column = i % columns;
                    var button = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + column * (width + gap),
                                 y - row * (AvTokens.RowHeight + gap), width, AvTokens.RowHeight),
                        labels[i], "toggle", () =>
                        {
                            actions[index]();
                            RequestRefresh();
                        }, AvButtonStyle.Toggle);
                    button.WithTooltip(labels[i] + " MAP OPTION");
                    toggles.Add(new ToggleBinding { Button = button, IsOn = states[i] });
                }
                int rows = Mathf.CeilToInt(labels.Length / (float)columns);
                y -= rows * (AvTokens.RowHeight + gap) + AvTokens.Space3;
            }
        }
    }
}
