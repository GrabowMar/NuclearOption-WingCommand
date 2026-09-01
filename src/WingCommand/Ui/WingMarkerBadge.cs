using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Marks the wingmen the next order will go to: corner brackets round their map icon.
    ///
    /// The game's silhouette remains untouched: it still shows aircraft type and heading.
    /// Wing membership itself is carried by the tint (<see cref="WingMapTint"/>), so the
    /// badge only adds the "who is selected" mark on top of that colour.
    /// </summary>
    internal static class WingMarkerBadge
    {
        private const string SelectionObjectName = "WingCommand_SelectionBadge";

        /// <summary>
        /// Corner brackets round the icon, for the wingmen the next order will go to.
        ///
        /// Drawn well outside the silhouette rather than snug against it: the icon itself
        /// is already carrying type, heading, faction colour and the membership tint, and
        /// a further mark competing for the same few pixels reads as clutter instead of as
        /// an answer to "who is selected".
        /// </summary>
        public static void ApplyCommandSelection(Image host, bool selected)
        {
            if (host == null) return;
            Transform child = host.transform.Find(SelectionObjectName);
            Image badge = child != null ? child.GetComponent<Image>() : null;

            if (!selected)
            {
                if (badge != null) badge.gameObject.SetActive(false);
                return;
            }

            if (badge == null)
            {
                var go = new GameObject(SelectionObjectName, typeof(RectTransform), typeof(Image));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(host.rectTransform, worldPositionStays: false);
                rt.anchorMin = new Vector2(-0.62f, -0.62f);
                rt.anchorMax = new Vector2(1.62f, 1.62f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                // Left to inherit the host's transform, which UpdateIcon drives to the
                // unit's heading. The brackets therefore turn with the silhouette they
                // frame, which is what a box drawn around an oriented icon should do —
                // and it comes free, where holding them world-aligned would mean
                // counter-rotating against the map every frame.
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;

                badge = go.GetComponent<Image>();
                badge.sprite = IconFactory.Get("selection");
                badge.preserveAspect = true;
                badge.raycastTarget = false;
            }

            badge.gameObject.SetActive(true);

            // Near-white rather than a brighter green. The membership tint is already
            // green, and "selected" has to survive being read against it.
            Color color = WingMarkers.MemberColor;
            badge.color = new Color(
                Mathf.Clamp01(color.r + 0.65f), Mathf.Clamp01(color.g + 0.65f),
                Mathf.Clamp01(color.b + 0.65f), Mathf.Min(1f, host.color.a + 0.25f));
        }
    }
}
