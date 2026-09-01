using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Dedicated radio subtitle surface. It stays frameless and clear of the game's legacy
    /// message boxes while giving speaker, aircraft context and dialogue distinct weight.
    /// </summary>
    internal static class WingChatterHud
    {
        private sealed class Transmission
        {
            public string Identity;
            public string Context;
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
        private static TMP_Text contextLabel;
        private static TMP_Text messageLabel;

        public static bool IsIdle => current == null && queue.Count == 0;

        public static void Enqueue(string identity, string context, string message,
                                   bool urgent = false)
        {
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(message)) return;

            var transmission = new Transmission
            {
                Identity = identity.Trim(),
                Context = context?.Trim() ?? string.Empty,
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
            if (Plugin.Settings.Radio.Value == ChatterLevel.Off)
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
            WingRadioAudio.Reset();
            queue.Clear();
            current = null;
            currentAt = 0f;
            currentDuration = 0f;
            if (canvasRoot != null) Object.Destroy(canvasRoot);
            canvasRoot = null;
            card = null;
            group = null;
            identityLabel = null;
            contextLabel = null;
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

            // Voiced here rather than at Enqueue: a queued line may wait several seconds
            // behind the flight ahead of it, and a click that arrives before its own
            // subtitle belongs to nothing the player can read.
            WingRadioAudio.Transmission();

            identityLabel.text = current.Identity;
            contextLabel.text = current.Context;
            messageLabel.text = "<<  " + current.Message + "  >>";
            identityLabel.color = current.Urgent ? WingUi.Warning : Cyan();
            contextLabel.color = current.Urgent ? WingUi.Warning.WithAlpha(0.75f) : Cyan(0.62f);
            messageLabel.color = current.Urgent
                ? Color.Lerp(MessageColor(), WingUi.Warning, 0.28f)
                : MessageColor();
            messageLabel.fontStyle = current.Urgent
                ? FontStyles.Bold | FontStyles.Italic
                : FontStyles.Italic;
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
            card.sizeDelta = new Vector2(900f, 88f);
            card.anchoredPosition = new Vector2(0f, -26f);
            card.localScale = Vector3.one;

            group = cardObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Color cyan = Cyan();
            identityLabel = WingUi.Label(card, "", new Rect(0f, -1f, 900f, 22f),
                cyan, 13.5f, FontStyles.Bold, TextAlignmentOptions.Center);
            identityLabel.characterSpacing = 0.8f;
            contextLabel = WingUi.Label(card, "", new Rect(0f, -21f, 900f, 16f),
                Cyan(0.62f), 9f, FontStyles.Normal, TextAlignmentOptions.Center);
            contextLabel.characterSpacing = 1.8f;
            messageLabel = WingUi.Label(card, "", new Rect(0f, -42f, 900f, 30f),
                MessageColor(), 16.5f, FontStyles.Italic,
                TextAlignmentOptions.Center);
            messageLabel.enableWordWrapping = false;
            messageLabel.overflowMode = TextOverflowModes.Ellipsis;

            canvasRoot.SetActive(false);
        }

        private static Color Cyan(float alpha = 1f) => new Color(0.25f, 0.88f, 1f, alpha);

        private static Color MessageColor() => new Color(0.92f, 0.98f, 1f, 1f);

        private static bool Same(Transmission a, Transmission b) =>
            a != null && b != null && a.Identity == b.Identity && a.Message == b.Message;
    }
}
