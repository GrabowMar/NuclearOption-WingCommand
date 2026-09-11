namespace WingCommand
{
    /// <summary>Click-vs-drag discriminator for the tactical-map right-click. A right-drag pans
    /// the maximised map; it must not also place a wing point-order at where the drag began.
    /// Pure (no Unity types) so it unit-tests alongside the map-order policy.</summary>
    internal sealed class MapGesture
    {
        /// <summary>Release travel, in pixels, still read as a click rather than a map drag.</summary>
        public const float ClickSlopPixels = 8f;

        // Screen position of the pending right-button press. NaN means no press is tracked, so a
        // stray button-up cannot be read as a click.
        private float pressX = float.NaN;
        private float pressY = float.NaN;

        /// <summary>Remember where the right button went down, to measure travel on release.</summary>
        public void NotePointerDown(float screenX, float screenY)
        {
            pressX = screenX;
            pressY = screenY;
        }

        /// <summary>Whether the release at <paramref name="screenX"/>/<paramref name="screenY"/>
        /// is close enough to the tracked press to count as a click. False when no press was
        /// tracked. Consumes the tracked press either way.</summary>
        public bool ReleasedAsClick(float screenX, float screenY, float slopPixels = ClickSlopPixels)
        {
            if (float.IsNaN(pressX) || float.IsNaN(pressY)) return false;
            float dx = screenX - pressX;
            float dy = screenY - pressY;
            Clear();
            return dx * dx + dy * dy <= slopPixels * slopPixels;
        }

        public void Clear()
        {
            pressX = float.NaN;
            pressY = float.NaN;
        }
    }
}
