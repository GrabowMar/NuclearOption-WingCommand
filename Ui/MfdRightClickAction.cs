using System;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WingCommand
{
    /// <summary>Restores the native target filter's right-click "only this" behavior.</summary>
    internal sealed class MfdRightClickAction : MonoBehaviour, IPointerClickHandler
    {
        private Action action;

        public void Configure(Action onRightClick) => action = onRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            AvInput.Deselect(gameObject);
            action?.Invoke();
        }
    }
}
