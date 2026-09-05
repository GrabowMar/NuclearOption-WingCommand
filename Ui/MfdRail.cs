using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// The control rail: one thin column on the right of the maximised map holding every
    /// button on the screen.
    ///
    /// The stock layout hangs a bezel column down each side of the map and leaves the mods'
    /// map-overlay toggles floating somewhere else again, so the controls for one screen sit
    /// in three places. Collecting them into a single rail gives the map one contiguous
    /// rectangle, the panels one column of their own, and the player one place to look for
    /// anything clickable.
    ///
    /// <para><b>Cross-mod seam.</b> The rail object is named <see cref="RailName"/> and that
    /// name is the whole contract. Boscali Summer finds it with <c>GameObject.Find</c> and
    /// parents its overlay toggles in; it does not reference this assembly, and nothing of a
    /// custom type crosses the boundary. The <c>AppDomain</c> channel the rest of
    /// <c>NOAvionics</c> uses cannot carry a <c>RectTransform</c>, which is why this seam is
    /// a name rather than a registry entry.</para>
    /// </summary>
    internal static class MfdRail
    {
        /// <summary>
        /// The well-known name Boscali Summer looks for. Changing it is a breaking change to
        /// the dual-mod contract and belongs in
        /// <c>nomodkit/shared/avionics/README.md</c> along with the bezel and picker rules.
        /// </summary>
        public const string RailName = "NOAvionics.Rail";

        /// <summary>
        /// Height of one button in the rail.
        ///
        /// Deliberately larger than the stock bezel button. Those are small because they are
        /// squeezed into a cockpit frame; a rail with a whole screen edge to itself has no
        /// reason to inherit that constraint, and these are clicked mid-flight.
        /// </summary>
        private const float ButtonHeight = 58f;

        /// <summary>Vertical gap between buttons.</summary>
        private const float ButtonGap = 12f;

        /// <summary>Inset from the rail's own edges to a button.</summary>
        private const float RailPad = 8f;

        /// <summary>
        /// Label size for a rail button. Bigger than <c>AvTokens.FontTitle</c> — the panel
        /// type scale tops out at a size chosen to fit a 470px column of running text, and a
        /// button that is itself the whole control has no reason to share that ceiling.
        /// </summary>
        private const float LabelSize = 24f;

        /// <summary>Distance from the top of the rail to the first button.</summary>
        private const float RailTop = 8f;

        private static RectTransform rail;

        /// <summary>Y offset of the next free slot, measured down from the rail's top.</summary>
        private static float cursor;

        /// <summary>
        /// The rail, if one has been built this mission.
        ///
        /// Wing Command's own callers use this; Boscali finds the same object by name.
        /// </summary>
        public static bool TryGetRail(out RectTransform rect)
        {
            rect = rail;
            return rail != null;
        }

        /// <summary>Build (or re-find) the rail on the maximised map canvas.</summary>
        public static RectTransform Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return null;

            if (rail == null)
            {
                // A previous mission's rail can outlive this static field if the scene kept
                // the canvas; adopt it rather than building a second one.
                Transform existing = canvas.transform.Find(RailName);
                rail = existing as RectTransform;
            }

            if (rail == null)
            {
                var go = new GameObject(RailName, typeof(RectTransform));
                rail = go.GetComponent<RectTransform>();
                rail.SetParent(canvas.transform, worldPositionStays: false);
            }

            rail.anchorMin = rail.anchorMax = rail.pivot = new Vector2(0.5f, 0.5f);
            rail.sizeDelta = new Vector2(columns.Rail.width, columns.Rail.height);
            rail.anchoredPosition = MfdLayout.CentreOf(columns.Rail);
            rail.localScale = Vector3.one;
            rail.SetAsLastSibling();

            cursor = RailTop;
            return rail;
        }

        /// <summary>
        /// Claim the next slot down the rail.
        ///
        /// Both this mod's buttons and Boscali's overlay toggles advance the same cursor, so
        /// the two mods' controls stack rather than overlapping — without either needing to
        /// know how many buttons the other added.
        /// </summary>
        public static Rect NextSlot(float height = ButtonHeight)
        {
            float width = rail != null ? rail.rect.width - RailPad * 2f : MfdLayout.RailWidth - RailPad * 2f;
            var slot = new Rect(RailPad, -cursor, width, height);
            cursor += height + ButtonGap;
            return slot;
        }

        /// <summary>Forget the rail at the end of a mission; the next scene builds its own.</summary>
        public static void Reset()
        {
            rail = null;
            cursor = RailTop;
        }

        // ------------------------------------------------------------------- restyling

        /// <summary>
        /// What a vanilla bezel button looked like before the rail repainted it, so
        /// <c>Minimize</c> can put it back exactly.
        /// </summary>
        internal sealed class ButtonSkin
        {
            public Image Background;
            public Sprite Sprite;
            public Image.Type Type;
            public Color Color;

            public TMP_Text Label;
            public Color LabelColor;
            public float LabelSize;
            public float LabelSizeMin;
            public float LabelSizeMax;
            public FontStyles LabelStyle;
            public bool LabelAutoSize;
            public float LabelSpacing;

            /// <summary>The crisp frame drawn over the fill, added once the button has its
            /// final rail size; destroyed on restore rather than undone value-by-value.</summary>
            public GameObject Decoration;

            public void Restore()
            {
                if (Background != null)
                {
                    Background.sprite = Sprite;
                    Background.type = Type;
                    Background.color = Color;
                }

                if (Label != null)
                {
                    Label.enableAutoSizing = LabelAutoSize;
                    Label.fontSizeMin = LabelSizeMin;
                    Label.fontSizeMax = LabelSizeMax;
                    Label.color = LabelColor;
                    Label.fontSize = LabelSize;
                    Label.fontStyle = LabelStyle;
                    Label.characterSpacing = LabelSpacing;
                }

                if (Decoration != null) Object.Destroy(Decoration);
                Decoration = null;
            }
        }

        /// <summary>
        /// Repaint a stock bezel button in the panels' language, and return what it was.
        ///
        /// A vanilla <c>BDF</c> button next to the mod's <c>WMC</c> button used to be two
        /// visibly different controls doing the same job. Only the button's own
        /// <c>Image</c> and label are touched — no hierarchy changes — so restoring is a
        /// matter of putting four values back.
        /// </summary>
        public static ButtonSkin Restyle(Button button)
        {
            if (button == null) return null;

            var skin = new ButtonSkin();

            Image bg = button.GetComponent<Image>();
            if (bg != null)
            {
                skin.Background = bg;
                skin.Sprite = bg.sprite;
                skin.Type = bg.type;
                skin.Color = bg.color;

                // A dark, near-opaque key rather than a washed-out grey: the rail sits over
                // a bright map, so anything translucent reads as murky. The accent frame is
                // baked into the sliced Control sprite; the fill just needs to be black
                // enough to make the label pop.
                bg.sprite = AvSprites.Control;
                bg.type = Image.Type.Sliced;
                bg.color = AvTheme.Unity(Rgba.Shade(0.55f));
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                skin.Label = label;
                skin.LabelColor = label.color;
                skin.LabelSize = label.fontSize;
                skin.LabelSizeMin = label.fontSizeMin;
                skin.LabelSizeMax = label.fontSizeMax;
                skin.LabelStyle = label.fontStyle;
                skin.LabelAutoSize = label.enableAutoSizing;
                skin.LabelSpacing = label.characterSpacing;

                // The stock bezel label is auto-sized to fit a small cockpit button, and
                // while enableAutoSizing is on TMP recomputes the size every layout and
                // discards whatever fontSize is assigned. Turning it off — and pinning the
                // min/max it would otherwise clamp to — is what makes the rail's type take.
                label.enableAutoSizing = false;
                label.fontSizeMin = LabelSize;
                label.fontSizeMax = LabelSize;
                label.color = AvTheme.TextPrimary;
                label.fontSize = LabelSize;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 1f;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
            }

            // A stock bezel button is a Selectable, so a flight stick can steer focus onto it
            // and fire it. Every control the mods build already disables that; the vanilla
            // ones the rail adopts have to be disarmed too.
            AvInput.StripNavigation(button);

            return skin;
        }

        /// <summary>Place a button into a rail slot.</summary>
        public static void Place(Button button, Rect slot)
        {
            if (button == null || rail == null) return;

            Transform t = button.transform;
            if (t.parent != rail) t.SetParent(rail, worldPositionStays: false);

            var rt = button.GetComponent<RectTransform>();
            if (rt == null) return;

            AvKit.Place(rt, slot);
            rt.localRotation = Quaternion.identity;
            t.SetAsLastSibling();
        }

        /// <summary>
        /// A crisp frame and corner ticks over the flat fill, sized to the button's own rail
        /// slot. <c>AvSprites.Control</c>'s baked edge is tinted the same colour as the fill,
        /// so a single flat Image reads as one dark blob with no definition; this draws the
        /// accent border and ticks as a child instead, and is torn down as one object on
        /// restore rather than undone value by value.
        /// </summary>
        private static void Decorate(Button button, ButtonSkin skin, Rect slot)
        {
            if (button == null || skin == null) return;

            var go = new GameObject("AvDecoration", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(button.transform, worldPositionStays: false);
            AvKit.Stretch(rt);
            rt.SetAsLastSibling();

            var area = new Rect(0f, 0f, slot.width, slot.height);
            AvKit.Outline(rt, area, AvTheme.Frame);
            AvKit.CornerTicks(rt, area, AvTheme.Hairline, 5f);

            skin.Decoration = go;
        }

        /// <summary>
        /// Lay a list of bezel buttons down the rail, restyling each one. Returns how many
        /// were adopted, so a caller can avoid drawing a separator before nothing.
        /// </summary>
        public static int Adopt(List<Button> buttons, List<MFDScreen> screens, List<ButtonSkin> skins)
        {
            if (buttons == null) return 0;

            int adopted = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (button == null) continue;

                // The stock bezel carries six slots per column with about three filled. A
                // spare is a live GameObject with no screen behind it — vanilla only clears
                // its Behaviour.enabled — so filtering on activeSelf would have put five
                // blank buttons in the rail. Ask whether the slot drives a screen instead.
                if (screens == null || i >= screens.Count || screens[i] == null) continue;

                ButtonSkin skin = Restyle(button);
                if (skin != null && skins != null) skins.Add(skin);

                Rect slot = NextSlot();
                Place(button, slot);
                Decorate(button, skin, slot);
                adopted++;
            }

            return adopted;
        }
    }
}
