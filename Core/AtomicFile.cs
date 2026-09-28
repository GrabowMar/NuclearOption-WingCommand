using System;
using System.IO;

namespace WingCommand
{
    /// <summary>Whole-file writes that never leave a half-written file (critic resolution 11): the text goes to a temp file first, then
    /// replaces the target in one step. R9 moves the other stores onto it.</summary>
    internal static class AtomicFile
    {
        public static bool WriteAllText(string path, string text, out string error)
        {
            error = null;
            string tmp = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, text ?? "");
                if (!File.Exists(path)) File.Move(tmp, path);
                else
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (Exception)
                    {
                        // ponytail: Mono's Replace can refuse some filesystems; the routes pattern leaves a crash window between the
                        // delete and the move. Upgrade when a write-through rename exists on every platform we ship to.
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch (Exception)
                {
                    // The temp file is left for the next write to replace.
                }
                return false;
            }
        }
    }
}
