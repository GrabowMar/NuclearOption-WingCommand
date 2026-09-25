using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>A single, non-interactive glass finish above an MFD's complete UI tree.</summary>
    public sealed class AvDisplayGlass : MonoBehaviour
    {
        private Image image;
        private float nextLightSample;
        private float opacity = -1f;

        public static Image Attach(RectTransform content)
        {
            return Attach(content, "DisplayGlass", AvSprites.DisplayGlass, 6f);
        }

        /// <summary>One passive finish for the entire maximized display, including the map.</summary>
        public static Image AttachFullDisplay(RectTransform content)
        {
            return Attach(content, "DisplayScreenFinish", AvSprites.DisplayScreen, 0f);
        }

        private static Image Attach(RectTransform content, string name, Sprite sprite, float inset)
        {
            if (content == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(content, false);
            AvKit.Stretch(rect);
            // Panel glass stays inside its bezel; the display finish spans every column.
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            Image glass = go.GetComponent<Image>();
            glass.sprite = sprite;
            glass.type = Image.Type.Simple;
            glass.raycastTarget = false;
            glass.color = new Color(1f, 1f, 1f, 0.72f);
            go.AddComponent<AvDisplayGlass>();
            return glass;
        }

        public void Awake() => image = GetComponent<Image>();

        public void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextLightSample || image == null) return;
            nextLightSample = now + 0.75f;

            float ambient = Mathf.Clamp01(RenderSettings.ambientIntensity);
            Light sun = RenderSettings.sun;
            float daylight = sun != null && sun.enabled
                ? Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * sun.intensity)
                : 0f;
            float level = 0.45f + 0.55f * Mathf.Clamp01(ambient * 0.55f + daylight * 0.45f);
            if (Mathf.Abs(level - opacity) < 0.025f) return;

            opacity = level;
            image.color = new Color(1f, 1f, 1f, level);
        }
    }
}
