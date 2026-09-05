using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// A bounded, pooled control grid. It mutates labels and latch state in place, so a
        /// changing mission inventory cannot create or lay out an unbounded widget tree.
        /// </summary>
        private sealed class MfdPagingGrid
        {
            private readonly AvButton[] buttons;
            private readonly int perPage;
            private readonly TMP_Text pageLabel;
            private readonly AvButton previous;
            private readonly AvButton next;

            private int page;
            private int count;
            private Func<int, string> label;
            private Func<int, bool> selected;
            private Func<int, bool> enabled;
            private Action<int> clicked;

            public MfdPagingGrid(RectTransform parent, float y, float width, int columns, int rows,
                                  bool pager = true)
            {
                perPage = Mathf.Max(1, columns * rows);
                buttons = new AvButton[perPage];
                float gap = AvTokens.Gap;
                float cellWidth = (width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < perPage; i++)
                {
                    int slot = i;
                    int row = i / columns;
                    int column = i % columns;
                    buttons[i] = AvStyled.Button(parent,
                        new Rect(AvTokens.Space3 + column * (cellWidth + gap),
                                 y - row * (AvTokens.RowHeight + gap),
                                 cellWidth, AvTokens.RowHeight),
                        "", "toggle", () => Click(slot), AvButtonStyle.Toggle);
                }

                if (pager)
                {
                    float pagerY = y - rows * (AvTokens.RowHeight + gap) - AvTokens.Space1;
                    AvButton[] pagerButtons = AvKit.Stepper(parent, AvTokens.Space3, pagerY,
                                                             width - AvTokens.Space3,
                                                             out pageLabel, Previous, Next);
                    previous = pagerButtons[0];
                    next = pagerButtons[1];
                }
            }

            public int CurrentIndex(int slot) => page * perPage + slot;

            public AvButton ButtonAt(int slot) =>
                slot >= 0 && slot < buttons.Length ? buttons[slot] : null;

            public void SetData(int newCount, Func<int, string> labels,
                                Func<int, bool> isSelected, Action<int> onClick,
                                Func<int, bool> isEnabled = null)
            {
                count = Mathf.Max(0, newCount);
                label = labels;
                selected = isSelected;
                clicked = onClick;
                enabled = isEnabled;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            public void ResetPage()
            {
                page = 0;
                Refresh();
            }

            /// <summary>Temporarily fence controls while a native model is still initializing.</summary>
            public void SetInteractable(bool on)
            {
                if (on)
                {
                    Refresh();
                    return;
                }

                for (int i = 0; i < buttons.Length; i++) buttons[i].SetEnabled(false);
                previous?.SetEnabled(false);
                next?.SetEnabled(false);
            }

            private int PageCount => Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));

            private void Previous()
            {
                if (page > 0) page--;
                Refresh();
            }

            private void Next()
            {
                if (page < PageCount - 1) page++;
                Refresh();
            }

            private void Click(int slot)
            {
                int index = CurrentIndex(slot);
                if (index < 0 || index >= count) return;
                if (enabled != null && !enabled(index)) return;
                clicked?.Invoke(index);
                Refresh();
            }

            private void Refresh()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    bool canUse = exists && (enabled == null || enabled(index));
                    buttons[i].SetEnabled(canUse);
                    buttons[i].SetLatched(exists && selected != null && selected(index));
                    buttons[i].SetText(exists && label != null ? label(index) : "");
                }

                if (pageLabel != null) pageLabel.text = count == 0 ? "NO ENTRIES" :
                    (page + 1).ToString() + " / " + PageCount.ToString();
                previous?.SetEnabled(page > 0);
                next?.SetEnabled(page < PageCount - 1);
            }
        }
    }
}
