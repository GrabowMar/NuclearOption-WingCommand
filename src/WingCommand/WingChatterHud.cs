using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Dedicated radio subtitle surface. Operational notices still use MessageUI; separating
    /// the two lets a transmission give the pilot and spoken line different visual weight.
    /// </summary>
    internal static class WingChatterHud
    {
        private sealed class Transmission
        {
            public string Identity;
            public string Message;
            public bool Urgent;
            public float QueuedAt;
        }

        private const int MaxQueued = 10;
        private const float FadeIn = 0.14f;
        private const float FadeOut = 0.24f;

        private static readonly List<Transmission> queue = new List<Transmission>();
        private static Transmission current;
        private static float currentAt;
        private static float currentDuration;

        private static GameObject canvasRoot;
        private static RectTransform card;
        private static CanvasGroup group;
        private static TMP_Text identityLabel;
        private static TMP_Text messageLabel;

        public static void Enqueue(string identity, string message, bool urgent = false)
        {
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(message)) return;

            var transmission = new Transmission
            {
                Identity = identity.Trim(),
                Message = message.Trim(),
                Urgent = urgent,
                QueuedAt = Time.unscaledTime,
            };

            if (Same(current, transmission) && Time.unscaledTime - currentAt < 1f) return;
            for (int i = 0; i < queue.Count; i++)
                if (Same(queue[i], transmission) && Time.unscaledTime - queue[i].QueuedAt < 1f)
                    return;

            if (queue.Count >= MaxQueued)
            {
                // Preserve danger calls. Routine chatter is cosmetic and may be discarded
                // when the radio is already busy.
                if (!urgent) return;
                queue.RemoveAt(queue.Count - 1);
            }

            if (urgent) queue.Insert(0, transmission);
            else queue.Add(transmission);
        }

        public static void Tick()
        {
            if (!Plugin.Config2.RadioChatter.Value)
            {
                queue.Clear();
                current = null;
                if (canvasRoot != null) canvasRoot.SetActive(false);
                return;
            }

            if (current == null)
            {
                if (queue.Count == 0)
                {
                    if (canvasRoot != null) canvasRoot.SetActive(false);
                    return;
                }

                ShowNext();
            }

            if (canvasRoot == null || current == null) return;

            float elapsed = Time.unscaledTime - currentAt;
            if (elapsed >= currentDuration)
            {
                current = null;
                group.alpha = 0f;
                if (queue.Count > 0) ShowNext();
                else canvasRoot.SetActive(false);
                return;
            }

            float fadeOutAt = currentDuration - FadeOut;
            if (elapsed < FadeIn) group.alpha = Mathf.Clamp01(elapsed / FadeIn);
            else if (elapsed > fadeOutAt) group.alpha = Mathf.Clamp01((currentDuration - elapsed) / FadeOut);
            else group.alpha = 1f;

            // A small settle-in movement gives the card the clipped radio-subtitle feel
            // without making it swim around during combat.
            float enter = Mathf.Clamp01(elapsed / FadeIn);
            card.anchoredPosition = new Vector2(0f, Mathf.Lerp(-20f, -26f, enter));
        }

        public static void Reset()
        {
            queue.Clear();
            current = null;
            currentAt = 0f;
            currentDuration = 0f;
            if (canvasRoot != null) Object.Destroy(canvasRoot);
            canvasRoot = null;
            card = null;
            group = null;
            identityLabel = null;
            messageLabel = null;
        }

        private static void ShowNext()
        {
            Build();
            if (canvasRoot == null || queue.Count == 0) return;

            current = queue[0];
            queue.RemoveAt(0);
            currentAt = Time.unscaledTime;
            currentDuration = Mathf.Clamp(2.35f + current.Message.Length * 0.018f, 2.65f, 4.1f);

            identityLabel.text = current.Identity;
            messageLabel.text = "<<  " + current.Message + "  >>";
            identityLabel.color = current.Urgent ? WingUi.Warning : Cyan();
            group.alpha = 0f;
            canvasRoot.SetActive(true);
        }

        private static void Build()
        {
            if (canvasRoot != null) return;

            canvasRoot = new GameObject("WingCommand_Chatter", typeof(RectTransform),
                                        typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(canvasRoot);

            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1200;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var cardObject = new GameObject("RadioSubtitle", typeof(RectTransform),
                                            typeof(CanvasGroup));
            card = cardObject.GetComponent<RectTransform>();
            card.SetParent(canvasRoot.transform, worldPositionStays: false);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.sizeDelta = new Vector2(760f, 68f);
            card.anchoredPosition = new Vector2(0f, -26f);
            card.localScale = Vector3.one;

            group = cardObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Color cyan = Cyan();
            identityLabel = WingUi.Label(card, "", new Rect(0f, -2f, 760f, 24f),
                cyan, 14f, FontStyles.Bold, TextAlignmentOptions.Center);
            messageLabel = WingUi.Label(card, "", new Rect(0f, -29f, 760f, 28f),
                new Color(0.90f, 0.97f, 1f, 1f), 16f, FontStyles.Italic,
                TextAlignmentOptions.Center);

            canvasRoot.SetActive(false);
        }

        private static Color Cyan(float alpha = 1f) => new Color(0.25f, 0.88f, 1f, alpha);

        private static bool Same(Transmission a, Transmission b) =>
            a != null && b != null && a.Identity == b.Identity && a.Message == b.Message;
    }
}
