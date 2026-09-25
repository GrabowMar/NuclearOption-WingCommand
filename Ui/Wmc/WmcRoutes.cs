using System;
using System.Collections.Generic;
using System.IO;

namespace WingCommand
{
    /// <summary>Saved routes on disk (spec WMC program §4): <c>routes.user.json</c> under the v1 data root, read once, written
    /// whole through a temp file.</summary>
    internal static class WmcRoutes
    {
        private static RouteStore store;

        private static string FilePath => Path.Combine(WingConfig.DataRoot, "routes.user.json");

        public static RouteStore Store
        {
            get
            {
                if (store != null) return store;
                var errors = new List<string>();
                try
                {
                    store = RouteStore.FromJson(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null, errors);
                }
                catch (Exception e)
                {
                    errors.Add(e.Message);
                    store = new RouteStore();
                }
                foreach (string e in errors) Plugin.Logger.LogWarning("[Routes] routes.user.json: " + e);
                return store;
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(WingConfig.DataRoot);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Store.ToJson());
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Routes] could not save routes.user.json: " + e.Message);
            }
        }
    }
}
