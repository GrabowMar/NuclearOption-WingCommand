using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace WingCommand
{
    /// <summary>Pilot files on disk, read only (R7: the studio's IMPORT; 0.9's folder included): the top-level <c>*.json</c> of a folder,
    /// pilots only. Nothing here writes, seeds samples or registers chatter — saved pilots live in <see cref="WingSavedPilots"/>.</summary>
    internal static class WingCustomPilots
    {
        /// <summary>0.9's pilot folder (outside the v1 root).</summary>
        public static string LegacyDirectory => Path.Combine(BepInEx.Paths.ConfigPath, "WingCommand", "Pilots");

        /// <summary>The pilots in every readable top-level JSON file of <paramref name="dir"/>; <paramref name="files"/> counts the files.</summary>
        public static List<CustomPilotRecord> LoadFolder(string dir, out int files)
        {
            var pilots = new List<CustomPilotRecord>();
            files = 0;
            try
            {
                if (!Directory.Exists(dir)) return pilots;
                foreach (string file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        pilots.AddRange(CustomPilotCodec.Decode(File.ReadAllText(file)).Pilots);
                        files++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogWarning("[Pilots] could not read " + Path.GetFileName(file) + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Pilots] could not list " + dir + ": " + e.Message);
            }
            return pilots;
        }

        /// <summary>Shows <paramref name="dir"/> in the file browser (created when missing).</summary>
        public static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                WingToast.Show("Opened the Pilots folder");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Pilots] could not open the folder: " + e.Message);
                WingToast.Show("Pilots folder: " + dir);
            }
        }
    }
}
