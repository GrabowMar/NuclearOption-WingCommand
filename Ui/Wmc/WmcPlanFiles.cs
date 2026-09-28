using System;
using System.Collections.Generic;
using System.IO;

namespace WingCommand
{
    /// <summary>The saved plans on disk (<c>plans.user.json</c> under the data root, written through a temporary file), read once, and
    /// the theatre the mission is in (its map), so PLANS › lists only the plans made for it.</summary>
    internal static class WmcPlanFiles
    {
        private static PlanStore store;

        private static string FilePath => Path.Combine(WingConfig.RecordsRoot, "plans.user.json");

        /// <summary>The mission's map (MissionManager.CurrentMission.MapKey.Path), or "" outside a mission.</summary>
        public static string Theatre => MissionManager.CurrentMission?.MapKey.Path ?? "";

        public static PlanStore Store
        {
            get
            {
                if (store != null) return store;
                var errors = new List<string>();
                try
                {
                    store = PlanStore.FromJson(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null, errors);
                }
                catch (Exception e)
                {
                    errors.Add(e.Message);
                    store = new PlanStore();
                }
                foreach (string e in errors) Plugin.Logger.LogWarning("[Plans] plans.user.json: " + e);
                // Review minor: a file that could not be read is kept aside before any SAVE writes a fresh one.
                if (errors.Count > 0 && File.Exists(FilePath))
                    try
                    {
                        File.Copy(FilePath, FilePath + ".bad", true);
                        Plugin.Logger.LogWarning("[Plans] kept the unreadable plans.user.json as plans.user.json.bad");
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogWarning("[Plans] could not keep the unreadable plans.user.json aside: " + e.Message);
                    }
                return store;
            }
        }

        /// <summary>Writes the store; false (logged) when the write failed.</summary>
        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(WingConfig.RecordsRoot);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Store.ToJson());
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Plans] could not save plans.user.json: " + e.Message);
                return false;
            }
        }
    }
}
