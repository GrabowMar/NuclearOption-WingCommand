using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Input isolation helpers to strip controller/joystick axis steering from Selectables
    /// and neutralize persistent EventSystem selection after clicks.
    /// </summary>
    public static class AvInput
    {
        public static void StripNavigation(Selectable selectable)
        {
            if (selectable != null)
            {
                selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }

        public static void Deselect(GameObject go = null)
        {
            if (EventSystem.current != null)
            {
                if (go == null || EventSystem.current.currentSelectedGameObject == go)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }
    }
}
