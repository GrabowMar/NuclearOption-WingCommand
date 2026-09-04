using System;
using System.Collections.Generic;
using System.IO;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// "SET" — MFD settings screen for cockpit display preferences, backdrop transparency,
    /// and user-uploaded wallpaper backgrounds.
    /// </summary>
    internal static class SetScreen
    {
        private const float PanelWidth = AvTokens.PanelWidth;
        private const float Pad = WingUi.Pad;
        private const float Gap = WingUi.Gap;
        private const float StatusStripHeight = AvTokens.StatusStripHeight;
        private const string HoverPrompt = "Hover a control to see what it does.";

        private static MFDScreen screen;
        private static float panelHeight;

        private static TMP_Text opacityValueLabel;
        private static WingButton gridToggleButton;
        private static WingButton wallpaperToggleButton;
        private static TMP_Text wallpaperFileLabel;
        private static TMP_Text wallpaperIndexLabel;
        private static TMP_Text statusLabel;

        private static string lastTooltip;
        private static float nextRefresh;
        private static float nextAttempt;
        private static bool gaveUp;

        private static readonly List<string> availableImages = new List<string>();
        private static int selectedImageIndex = -1;
        private static Sprite customImageSprite;
        private static Texture2D customImageTexture;

        public static Sprite CurrentUserSprite => customImageSprite;

        // ------------------------------------------------------------------- lifecycle

        public static void Tick()
        {
            if (gaveUp || !GameAccess.MfdAvailable || Plugin.Settings == null || !Plugin.Settings.UseSetPanel.Value)
                return;

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            if (!screen.isActive)
            {
                if (WingKeyboardGuard.Captured)
                {
                    WingKeyboardGuard.Defocus();
                    WingKeyboardGuard.ForceRelease();
                }
                return;
            }

            string tooltip = WingButton.HoveredTooltip;
            if (!ReferenceEquals(tooltip, lastTooltip))
            {
                lastTooltip = tooltip;
                nextRefresh = 0f;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + WingBrain.Interval(0.2f);
                Refresh();
            }
        }

        public static void Reset()
        {
            BezelRegistry.Release(BezelRegistry.Set);
            screen = null;
            panelHeight = 0f;
            WingButton.ClearTooltip();

            opacityValueLabel = null;
            gridToggleButton = null;
            wallpaperToggleButton = null;
            wallpaperFileLabel = null;
            wallpaperIndexLabel = null;
            statusLabel = null;
            lastTooltip = null;
            gaveUp = false;

            DestroyCustomSprite();
        }

        private static void DestroyCustomSprite()
        {
            if (customImageSprite != null)
            {
                UnityEngine.Object.Destroy(customImageSprite);
                customImageSprite = null;
            }
            if (customImageTexture != null)
            {
                UnityEngine.Object.Destroy(customImageTexture);
                customImageTexture = null;
            }
        }

        // ---------------------------------------------------------------- installation

        private static void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdBezel.TryClaim(BezelRegistry.Set, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    Fail("no free bezel button for SET screen");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    BezelRegistry.Release(BezelRegistry.Set);
                    return;
                }

                ScanBackgroundImages();
                LoadSavedImage();

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    BezelRegistry.Release(BezelRegistry.Set);
                    return;
                }

                MfdBezel.Bind(mfd, buttons, screens, slot, left, screen);
                Plugin.Logger.LogInfo("SET screen installed on " + (left ? "left" : "right") +
                                      " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Plugin.Logger.LogError("SET screen install failed: " + e);
            }
        }

        private static void Fail(string reason)
        {
            gaveUp = true;
            screen = null;
            Plugin.Logger.LogWarning("Could not install the SET MFD screen (" + reason + ").");
        }

        // ---------------------------------------------------------------- UI building

        private static MFDScreen Build(MFDScreen template, Button bezelButton)
        {
            var root = new GameObject("SET_Screen", typeof(RectTransform), typeof(Image));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.SetParent(template.transform.parent, worldPositionStays: false);

            var templateRt = (RectTransform)template.transform;
            rt.anchorMin = templateRt.anchorMin;
            rt.anchorMax = templateRt.anchorMax;
            rt.pivot = templateRt.pivot;
            rt.localScale = templateRt.localScale;

            Image bg = root.GetComponent<Image>();
            bg.sprite = WingUi.PanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform));
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.SetParent(rt, worldPositionStays: false);
            WingUi.Stretch(contentRt);

            float y = -Pad;
            y = AddHeader(contentRt, y);
            y = AddOpacitySection(contentRt, y);
            y = AddStyleSection(contentRt, y);
            y = AddWallpaperSection(contentRt, y);

            const float stripBlock = StatusStripHeight + Gap;
            panelHeight = Mathf.Max(AvTokens.PanelHeight, Mathf.Abs(y) + stripBlock + Pad);
            rt.sizeDelta = new Vector2(PanelWidth, panelHeight);

            float stripY = -(panelHeight - Pad - StatusStripHeight);
            AddStatusStrip(contentRt, stripY);

            MFDScreen s = root.AddComponent<MFDScreen>();
            s.shortName = "SET";
            s.displayPanel = content;
            s.aircraftOnly = false;
            s.label = FindLabel(bezelButton);
            s.highlight = FindHighlight(bezelButton, template);

            Refresh();
            return s;
        }

        private static float AddHeader(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            var headerRect = new Rect(Pad, y, w, 28f);
            WingUi.Label(parent, "SETTINGS · MFD CONFIGURATION", headerRect,
                AvTheme.Accent, AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            y -= 32f;

            WingUi.Rule(parent, new Rect(Pad, y, w, 1f), AvTheme.Hairline);
            y -= Gap;
            return y;
        }

        private static float AddOpacitySection(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            y = WingUi.Heading(parent, y, "BACKGROUND OPACITY", PanelWidth);

            var (cardFill, _) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, 76f), AvTheme.RailInfo);
            RectTransform cardRt = cardFill.rectTransform;

            // Value display row
            WingUi.Label(cardRt, "OPACITY LEVEL", new Rect(Pad, -Pad, 140f, 20f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            opacityValueLabel = WingUi.Label(cardRt, "40%", new Rect(w - Pad - 100f, -Pad, 100f, 20f),
                AvTheme.Accent, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Step buttons & Quick presets row
            float btnY = -38f;
            float btnH = 26f;

            WingUi.Button(cardRt, "[-5%]", new Rect(Pad, btnY, 44f, btnH), () => AdjustOpacity(-0.05f),
                AvTokens.FontSmall, UiButtonStyle.Quiet)
                .WithTooltip("Decrease MFD background opacity by 5%.");

            WingUi.Button(cardRt, "[+5%]", new Rect(Pad + 48f, btnY, 44f, btnH), () => AdjustOpacity(0.05f),
                AvTokens.FontSmall, UiButtonStyle.Quiet)
                .WithTooltip("Increase MFD background opacity by 5%.");

            float presetX = Pad + 104f;
            float presetW = (w - presetX - Pad - Gap * 3f) / 4f;

            WingUi.Button(cardRt, "CLEAR", new Rect(presetX, btnY, presetW, btnH), () => SetOpacity(0f),
                AvTokens.FontMicro, UiButtonStyle.Default)
                .WithTooltip("Set fully transparent background (0% opacity).");

            WingUi.Button(cardRt, "25%", new Rect(presetX + (presetW + Gap), btnY, presetW, btnH), () => SetOpacity(0.25f),
                AvTokens.FontMicro, UiButtonStyle.Default)
                .WithTooltip("Set subtle translucent background (25% opacity).");

            WingUi.Button(cardRt, "50%", new Rect(presetX + (presetW + Gap) * 2f, btnY, presetW, btnH), () => SetOpacity(0.50f),
                AvTokens.FontMicro, UiButtonStyle.Default)
                .WithTooltip("Set balanced glass background (50% opacity).");

            WingUi.Button(cardRt, "SOLID", new Rect(presetX + (presetW + Gap) * 3f, btnY, presetW, btnH), () => SetOpacity(1.0f),
                AvTokens.FontMicro, UiButtonStyle.Default)
                .WithTooltip("Set fully opaque background (100% opacity).");

            y -= 76f + Gap;
            return y;
        }

        private static float AddStyleSection(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            y = WingUi.Heading(parent, y, "STYLE & DATUM GRID", PanelWidth);

            var (cardFill, _) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, 52f), AvTheme.RailReady);
            RectTransform cardRt = cardFill.rectTransform;

            WingUi.Label(cardRt, "CHECKERED DATUM GRID", new Rect(Pad, -16f, 220f, 20f),
                AvTheme.TextPrimary, AvTokens.FontBody, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            gridToggleButton = WingUi.Button(cardRt, "GRID: OFF", new Rect(w - Pad - 110f, -13f, 110f, 26f),
                ToggleGrid, AvTokens.FontSmall, UiButtonStyle.Toggle);
            gridToggleButton.WithTooltip("Toggle the 64px checkered tactical datum grid overlay on or off.");

            y -= 52f + Gap;
            return y;
        }

        private static float AddWallpaperSection(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            y = WingUi.Heading(parent, y, "USER WALLPAPER IMAGE", PanelWidth);

            var (cardFill, _) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, 116f), AvTheme.RailCaution);
            RectTransform cardRt = cardFill.rectTransform;

            // Toggle row
            WingUi.Label(cardRt, "CUSTOM BACKGROUND", new Rect(Pad, -14f, 180f, 20f),
                AvTheme.TextPrimary, AvTokens.FontBody, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            wallpaperToggleButton = WingUi.Button(cardRt, "IMAGE: OFF", new Rect(w - Pad - 110f, -11f, 110f, 26f),
                ToggleWallpaper, AvTokens.FontSmall, UiButtonStyle.Toggle);
            wallpaperToggleButton.WithTooltip("Enable or disable rendering the custom user wallpaper image.");

            // File selection row
            float fileY = -44f;
            WingUi.Button(cardRt, "< PREV", new Rect(Pad, fileY, 68f, 26f), () => CycleImage(-1),
                AvTokens.FontSmall, UiButtonStyle.Quiet)
                .WithTooltip("Select previous wallpaper image in the Backgrounds folder.");

            wallpaperFileLabel = WingUi.Label(cardRt, "No images found", new Rect(Pad + 74f, fileY, w - Pad * 2f - 148f, 26f),
                AvTheme.Accent, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);

            WingUi.Button(cardRt, "NEXT >", new Rect(w - Pad - 68f, fileY, 68f, 26f), () => CycleImage(1),
                AvTokens.FontSmall, UiButtonStyle.Quiet)
                .WithTooltip("Select next wallpaper image in the Backgrounds folder.");

            // Folder & Reload row
            float actionY = -78f;
            wallpaperIndexLabel = WingUi.Label(cardRt, "0 / 0 images", new Rect(Pad, actionY, 140f, 26f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

            float actBtnW = 100f;
            WingUi.Button(cardRt, "RELOAD", new Rect(w - Pad - actBtnW * 2f - Gap, actionY, actBtnW, 26f),
                ScanBackgroundImages, AvTokens.FontSmall, UiButtonStyle.Default)
                .WithTooltip("Rescan BepInEx/config/WingCommand/Backgrounds/ for newly added images.");

            WingUi.Button(cardRt, "OPEN FOLDER", new Rect(w - Pad - actBtnW, actionY, actBtnW, 26f),
                OpenBackgroundsFolder, AvTokens.FontSmall, UiButtonStyle.Default)
                .WithTooltip("Open the Backgrounds directory in Windows File Explorer.");

            y -= 116f + Gap;
            return y;
        }

        private static void AddStatusStrip(RectTransform parent, float y)
        {
            float w = PanelWidth - Pad * 2f;
            var (cardFill, _) = WingUi.TacticalCard(parent, new Rect(Pad, y, w, StatusStripHeight), AvTheme.RailInfo, hasRail: false);
            statusLabel = WingUi.Label(cardFill.rectTransform, HoverPrompt,
                new Rect(Pad, -Pad, w - Pad * 2f, StatusStripHeight - Pad * 2f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.Center);
            statusLabel.enableWordWrapping = true;
        }

        private static TextMeshProUGUI FindLabel(Button button)
        {
            return button == null
                ? null
                : button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        }

        private static Image FindHighlight(Button button, MFDScreen template)
        {
            if (button == null) return null;

            if (template != null && template.highlight != null)
            {
                string path = PathUnder(template.highlight.transform, out Transform root);
                if (root != null && !string.IsNullOrEmpty(path))
                {
                    Transform found = button.transform.Find(path);
                    if (found != null)
                    {
                        Image img = found.GetComponent<Image>();
                        if (img != null) return img;
                    }
                }
            }

            foreach (Image img in button.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (img != null && img.name.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0)
                    return img;
            }

            return null;
        }

        private static string PathUnder(Transform leaf, out Transform root)
        {
            root = null;
            if (leaf == null) return null;

            var parts = new List<string>();
            Transform current = leaf;
            while (current.parent != null && current.parent.GetComponent<Button>() == null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            root = current.parent;
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // ---------------------------------------------------------------- actions

        private static void AdjustOpacity(float delta)
        {
            if (Plugin.Settings == null) return;
            float current = Plugin.Settings.MfdBackgroundOpacity.Value;
            float next = Mathf.Clamp(Mathf.Round((current + delta) * 100f) / 100f, 0f, 1f);
            SetOpacity(next);
        }

        private static void SetOpacity(float val)
        {
            if (Plugin.Settings == null) return;
            Plugin.Settings.MfdBackgroundOpacity.Value = val;
            MfdMapDeck.ApplyAppearance();
            Refresh();
        }

        private static void ToggleGrid()
        {
            if (Plugin.Settings == null) return;
            Plugin.Settings.MfdCheckeredGrid.Value = !Plugin.Settings.MfdCheckeredGrid.Value;
            MfdMapDeck.ApplyAppearance();
            Refresh();
        }

        private static void ToggleWallpaper()
        {
            if (Plugin.Settings == null) return;
            Plugin.Settings.MfdCustomImageEnabled.Value = !Plugin.Settings.MfdCustomImageEnabled.Value;
            if (Plugin.Settings.MfdCustomImageEnabled.Value && customImageSprite == null)
            {
                LoadSelectedImage();
            }
            MfdMapDeck.ApplyAppearance();
            Refresh();
        }

        private static string GetBackgroundsDirectory()
        {
            string dir = Path.Combine(BepInEx.Paths.ConfigPath, "WingCommand", "Backgrounds");
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string readme = Path.Combine(dir, "README.txt");
                    if (!File.Exists(readme))
                    {
                        File.WriteAllText(readme,
                            "Nuclear Option - Wing Command MFD Wallpapers\n" +
                            "============================================\n\n" +
                            "Place your custom .png, .jpg, or .jpeg images here.\n" +
                            "Recommended resolution: 1920x1080 (or your display resolution).\n" +
                            "Select them in-game via the MFD SET (Settings) panel.\n");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning("Could not create backgrounds folder: " + e.Message);
                }
            }
            return dir;
        }

        private static void ScanBackgroundImages()
        {
            string dir = GetBackgroundsDirectory();
            availableImages.Clear();

            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir);
                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        availableImages.Add(f);
                    }
                }
            }

            availableImages.Sort(StringComparer.OrdinalIgnoreCase);

            string saved = Plugin.Settings?.MfdCustomImageFile?.Value;
            if (!string.IsNullOrEmpty(saved))
            {
                selectedImageIndex = availableImages.FindIndex(f =>
                    string.Equals(Path.GetFileName(f), saved, StringComparison.OrdinalIgnoreCase));
            }

            if (selectedImageIndex < 0 && availableImages.Count > 0)
            {
                selectedImageIndex = 0;
            }

            LoadSelectedImage();
            Refresh();
        }

        private static void CycleImage(int delta)
        {
            if (availableImages.Count == 0) return;
            selectedImageIndex = (selectedImageIndex + delta + availableImages.Count) % availableImages.Count;
            string fileName = Path.GetFileName(availableImages[selectedImageIndex]);
            if (Plugin.Settings != null) Plugin.Settings.MfdCustomImageFile.Value = fileName;
            LoadSelectedImage();
            MfdMapDeck.ApplyAppearance();
            Refresh();
        }

        private static void LoadSavedImage()
        {
            if (availableImages.Count == 0) return;
            if (selectedImageIndex < 0 || selectedImageIndex >= availableImages.Count)
                selectedImageIndex = 0;
            LoadSelectedImage();
        }

        private static void LoadSelectedImage()
        {
            DestroyCustomSprite();

            if (selectedImageIndex < 0 || selectedImageIndex >= availableImages.Count)
            {
                MfdMapDeck.ApplyAppearance();
                return;
            }

            string filePath = availableImages[selectedImageIndex];
            if (!File.Exists(filePath)) return;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                customImageTexture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    name = Path.GetFileNameWithoutExtension(filePath),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };

                if (ImageConversion.LoadImage(customImageTexture, data, false))
                {
                    customImageSprite = Sprite.Create(
                        customImageTexture,
                        new Rect(0f, 0f, customImageTexture.width, customImageTexture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    customImageSprite.name = customImageTexture.name;
                    customImageSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                else
                {
                    DestroyCustomSprite();
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Failed loading MFD custom image " + filePath + ": " + e.Message);
                DestroyCustomSprite();
            }

            MfdMapDeck.ApplyAppearance();
        }

        private static void OpenBackgroundsFolder()
        {
            string dir = GetBackgroundsDirectory();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                });
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Could not open backgrounds directory: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- refresh

        private static void Refresh()
        {
            if (Plugin.Settings == null) return;

            float opacity = Plugin.Settings.MfdBackgroundOpacity.Value;
            if (opacityValueLabel != null)
            {
                opacityValueLabel.text = Mathf.RoundToInt(opacity * 100f) + "%";
            }

            bool gridOn = Plugin.Settings.MfdCheckeredGrid.Value;
            if (gridToggleButton != null)
            {
                gridToggleButton.SetText(gridOn ? "GRID: ON" : "GRID: OFF");
                gridToggleButton.SetLatched(gridOn);
            }

            bool wallpaperOn = Plugin.Settings.MfdCustomImageEnabled.Value;
            if (wallpaperToggleButton != null)
            {
                wallpaperToggleButton.SetText(wallpaperOn ? "IMAGE: ON" : "IMAGE: OFF");
                wallpaperToggleButton.SetLatched(wallpaperOn);
            }

            if (availableImages.Count > 0 && selectedImageIndex >= 0 && selectedImageIndex < availableImages.Count)
            {
                string name = Path.GetFileName(availableImages[selectedImageIndex]);
                if (wallpaperFileLabel != null) wallpaperFileLabel.text = name;
                if (wallpaperIndexLabel != null)
                    wallpaperIndexLabel.text = (selectedImageIndex + 1) + " / " + availableImages.Count + " images";
            }
            else
            {
                if (wallpaperFileLabel != null) wallpaperFileLabel.text = "No images in folder";
                if (wallpaperIndexLabel != null) wallpaperIndexLabel.text = "0 images";
            }

            string hovered = WingButton.HoveredTooltip;
            if (statusLabel != null)
            {
                statusLabel.text = !string.IsNullOrEmpty(hovered)
                    ? hovered
                    : (wallpaperOn && customImageSprite != null
                        ? "Wallpaper active: " + Path.GetFileName(availableImages[selectedImageIndex]) + " (" + Mathf.RoundToInt(opacity * 100f) + "% opacity)"
                        : "MFD background: " + (gridOn ? "Checkered Grid" : "Transparent Gradient") + " (" + Mathf.RoundToInt(opacity * 100f) + "% opacity)");
            }
        }
    }
}
