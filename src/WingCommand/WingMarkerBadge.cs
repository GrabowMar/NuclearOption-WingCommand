using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Adds the wing's small caret identifier above an existing aircraft icon.
    ///
    /// The game's silhouette remains untouched: it still shows aircraft type and heading.
    /// The badge supplies the one piece colour alone cannot — a recognisable wing accent on
    /// the map and HUD, including against backgrounds close to the tint.
    /// </summary>
    internal static class WingMarkerBadge
    {
        private const string ObjectName = "WingCommand_MemberBadge";

        public static void Apply(Image host, WingMarkers.Role role)
        {
            if (host == null) return;

            Image badge = Find(host);
            if (role != WingMarkers.Role.Member)
            {
                if (badge != null) badge.gameObject.SetActive(false);
                return;
            }

            if (badge == null) badge = Create(host);
            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);

            Color tint = WingMarkers.MemberColor;
            badge.color = new Color(tint.r, tint.g, tint.b,
                                    Mathf.Min(0.90f, host.color.a));
        }

        public static void Clear(Image host)
        {
            Image badge = Find(host);
            if (badge != null) badge.gameObject.SetActive(false);
        }

        private static Image Find(Image host)
        {
            Transform child = host.transform.Find(ObjectName);
            return child != null ? child.GetComponent<Image>() : null;
        }

        private static Image Create(Image host)
        {
            var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(host.rectTransform, worldPositionStays: false);

            // Only slightly larger than the stock silhouette. The old four-sided bracket
            // was 1.64x the icon and dominated it; this keeps the accent subordinate.
            rt.anchorMin = new Vector2(-0.12f, -0.12f);
            rt.anchorMax = new Vector2(1.12f, 1.12f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            Image badge = go.GetComponent<Image>();
            badge.sprite = IconFactory.Get("wing-badge");
            badge.preserveAspect = true;
            badge.raycastTarget = false;
            return badge;
        }
    }
}
