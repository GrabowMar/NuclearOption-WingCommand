using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Owns the live <c>.avss</c> sheet and turns its declarations into Unity colours.
    ///
    /// Two sources, in order: the copy embedded in the plugin (always present, always
    /// valid), then an optional file the player can edit. The override is a whole sheet
    /// rather than a patch, because a partial sheet that silently inherits half its rules
    /// from a build is harder to reason about than one file you can read top to bottom.
    ///
    /// Nothing here throws. A missing file, an unreadable file and a sheet full of typos
    /// all resolve to "keep what we had and log why", because the caller is a panel build
    /// path and its failure mode is a player staring at an empty bezel.
    /// </summary>
    public static class AvStyleHost
    {
        /// <summary>The name the embedded default is registered under in both plugins.</summary>
        public const string ResourceName = "NOAvionics.avionics.avss";

        /// <summary>Where a player-editable override is looked for, under the BepInEx config dir.</summary>
        public const string OverrideRelativePath = "NOAvionics/avionics.avss";

        private static AvStyleSheet sheet;
        private static string overridePath;
        private static Action<string> log;
        private static Action<string> warn;

        /// <summary>Bumped on every successful load, so panels can tell they are stale.</summary>
        public static int Generation { get; private set; }

        public static AvStyleSheet Sheet
        {
            get
            {
                if (sheet == null) Load();
                return sheet;
            }
        }

        /// <summary>
        /// Point the host at this plugin's config directory and loggers.
        ///
        /// Called once at startup by each mod. Both mods may call it; last writer wins and
        /// they agree on the path, which is the point — one sheet, both panels.
        /// </summary>
        public static void Configure(string bepInExConfigDir, Action<string> info, Action<string> warning)
        {
            log = info;
            warn = warning;
            overridePath = string.IsNullOrEmpty(bepInExConfigDir)
                ? null
                : Path.Combine(bepInExConfigDir, OverrideRelativePath);
        }

        /// <summary>
        /// Re-read the sheet. Returns true if the active sheet changed.
        ///
        /// This is the hot-reload entry point: edit the file, call this, rebuild the open
        /// panel. It is the reason the whole layer exists — retuning the panels used to
        /// cost a build, a deploy and a relaunch.
        /// </summary>
        public static bool Reload()
        {
            AvStyleSheet previous = sheet;
            Load();
            return !ReferenceEquals(previous, sheet);
        }

        private static void Load()
        {
            string text = ReadOverride();
            string source = "override";

            if (text == null)
            {
                text = ReadEmbedded();
                source = "embedded default";
            }

            if (string.IsNullOrEmpty(text))
            {
                // Nothing readable anywhere. An empty sheet declares nothing, and every
                // widget falls back to the token defaults it had before this layer existed.
                if (sheet == null) sheet = AvStyleSheet.Parse("");
                Warn("no stylesheet could be read; panels fall back to built-in token defaults");
                return;
            }

            AvStyleSheet parsed = AvStyleSheet.Parse(text);

            if (parsed.HasErrors)
            {
                foreach (string error in parsed.Errors)
                    Warn("avionics.avss (" + source + ") " + error);

                // Errors are per-declaration, not fatal: the rules that did parse are still
                // better than nothing. But if we already had a good sheet and the *override*
                // is the broken one, keeping the good sheet is the kinder failure.
                if (sheet != null && source == "override" && parsed.RuleCount == 0)
                {
                    Warn("keeping the previous stylesheet");
                    return;
                }
            }

            sheet = parsed;
            Generation++;
            Info("avionics.avss loaded from " + source + " (" + parsed.RuleCount + " rules)");
        }

        private static string ReadOverride()
        {
            if (string.IsNullOrEmpty(overridePath)) return null;
            try
            {
                return File.Exists(overridePath) ? File.ReadAllText(overridePath) : null;
            }
            catch (Exception e)
            {
                Warn("could not read " + overridePath + ": " + e.Message);
                return null;
            }
        }

        private static string ReadEmbedded()
        {
            try
            {
                Assembly asm = typeof(AvStyleHost).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Warn("could not read the embedded stylesheet: " + e.Message);
                return null;
            }
        }

        private static void Info(string message)
        {
            if (log != null) log(message);
        }

        private static void Warn(string message)
        {
            if (warn != null) warn(message);
            else if (log != null) log(message);
        }

        // ---------------------------------------------------------------- resolution

        /// <summary>The style for a class set, optionally in a state.</summary>
        public static AvStyle Style(string classes, string state = null) => Sheet.Resolve(classes, state);

        /// <summary>
        /// Turn a declared paint into a colour, resolving live theme references against
        /// whatever the player's mission theme currently is.
        /// </summary>
        public static Color Resolve(AvPaint paint, Color fallback)
        {
            switch (paint.Kind)
            {
                case AvColorRef.Fixed:
                    return AvTheme.Unity(paint.Value);

                case AvColorRef.Accent: return WithAlpha(AvTheme.Accent, paint.Alpha);
                case AvColorRef.Friendly: return WithAlpha(AvTheme.Friendly, paint.Alpha);
                case AvColorRef.Warning: return WithAlpha(AvTheme.Warning, paint.Alpha);
                case AvColorRef.Alert: return WithAlpha(AvTheme.Alert, paint.Alpha);

                default:
                    return fallback;
            }
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>The sprite a style asked for, or null for a flat fill.</summary>
        public static Sprite ResolveSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name)
            {
                case "panel": return AvSprites.Panel;
                case "card": return AvSprites.Card;
                case "control": return AvSprites.Control;
                default: return null;
            }
        }

        public static TMPro.TextAlignmentOptions ResolveAlign(AvAlign align)
        {
            switch (align)
            {
                case AvAlign.Center: return TMPro.TextAlignmentOptions.Center;
                case AvAlign.Right: return TMPro.TextAlignmentOptions.Right;
                default: return TMPro.TextAlignmentOptions.Left;
            }
        }
    }
}
