using System;

namespace WingCommand
{
    /// <summary>Owns temporary panel time scale until close or an external change.</summary>
    internal struct TacticalPauseState
    {
        private bool requested;
        private bool ownsScale;
        private float previousScale;
        private float appliedScale;

        public float Update(bool shouldPause, float currentScale, float configuredScale)
        {
            bool opening = shouldPause && !requested;
            requested = shouldPause;

            // Relinquish time scale after native or external changes until next opening; closing must
            // not undo another owner's pause.
            if (ownsScale && currentScale != appliedScale) ownsScale = false;
            if (!shouldPause)
            {
                if (!ownsScale) return currentScale;
                ownsScale = false;
                return previousScale;
            }

            if (!ownsScale)
            {
                if (!opening || currentScale <= 0.5f ||
                    float.IsNaN(currentScale) || float.IsInfinity(currentScale))
                    return currentScale;
                previousScale = currentScale;
                ownsScale = true;
            }

            appliedScale = float.IsNaN(configuredScale)
                ? 0.25f : Math.Max(0f, Math.Min(0.5f, configuredScale));
            return appliedScale;
        }
    }

    internal static class MfdPresentationRules
    {
        internal readonly struct Placement
        {
            public readonly float X, Top, Scale;
            public Placement(float x, float top, float scale)
            {
                X = x;
                Top = top;
                Scale = scale;
            }
        }

        public static Placement FitBesideBezel(float width, float height, bool left,
            float viewportLeft, float viewportRight, float viewportBottom, float viewportTop,
            float bezelLeft, float bezelRight, float gap, float? centerY = null)
        {
            float min = left ? viewportLeft : Math.Max(viewportLeft, bezelRight + gap);
            float max = left ? Math.Min(viewportRight, bezelLeft - gap) : viewportRight;
            float center = centerY ?? (viewportTop + viewportBottom) * 0.5f;
            // Fit around the map's visual centre using the smaller vertical clearance; asymmetric
            // reserves must not shift it.
            float availableHeight = 2f * Math.Min(viewportTop - center, center - viewportBottom);
            float scale = FitScale(width, height, max - min, availableHeight);
            // Position beside the native button column instead of an off-screen prefab origin.
            float top = center + height * scale * 0.5f;
            return new Placement(left ? max - width * scale : min, top, scale);
        }

        // Defer installation when dimensions are invalid; an invisible or enormous screen must not
        // capture map input.
        public static float FitScale(float width, float height, float availableWidth, float availableHeight)
        {
            if (!PositiveFinite(width) || !PositiveFinite(height) ||
                !PositiveFinite(availableWidth) || !PositiveFinite(availableHeight)) return 0f;
            return Math.Min(availableWidth / width, availableHeight / height);
        }

        /// <summary>Compute aspect-preserving image dimensions that fully cover the container.</summary>
        public static (float RenderedWidth, float RenderedHeight) CalculateAspectFill(
            float containerWidth, float containerHeight, float spriteWidth, float spriteHeight)
        {
            if (!PositiveFinite(containerWidth) || !PositiveFinite(containerHeight)) return (0f, 0f);
            float sW = PositiveFinite(spriteWidth) ? spriteWidth : 1f;
            float sH = PositiveFinite(spriteHeight) ? spriteHeight : 1f;
            float scale = Math.Max(containerWidth / sW, containerHeight / sH);
            return (sW * scale, sH * scale);
        }

        private static bool PositiveFinite(float value) =>
            value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
